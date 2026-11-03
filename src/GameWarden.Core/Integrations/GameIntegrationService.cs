using System.Text.Json;
using GameWarden.Core.Adapters;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Secrets;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Integrations;

/// <summary>The answer to a game API read or action: its value, or why not.</summary>
public sealed record GameIntegrationResult<T>(T? Value, string? Error, string? Code, int StatusCode)
    where T : class
{
    public static GameIntegrationResult<T> Ok(T value) => new(value, null, null, StatusCodes.Status200OK);

    public static GameIntegrationResult<T> Problem(int status, string code, string error) => new(null, error, code, status);
}

/// <summary>
/// One request path for every game API (docs/16): finds the instance, its adapter's <c>integration</c> section and the
/// provider Core has, renders the provider's auth from the instance's secrets, and calls it. A game that is not
/// running answers with <see cref="GameIntegrationView.Online"/> false instead of an error, so the tab is calm while
/// the server is stopped; a running game that does not answer is a 502/504.
/// </summary>
public sealed class GameIntegrationService(
    Supervisor supervisor,
    SupervisorContext context,
    AdapterCatalog adapters,
    GameIntegrationRegistry registry,
    IHttpClientFactory httpClients,
    ILogger<GameIntegrationService> logger)
{
    /// <summary>The named client for game APIs; tests replace its primary handler.</summary>
    public const string HttpClientName = "game-integration";

    public async Task<GameIntegrationResult<GameIntegrationView>> GetAsync(string instanceId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return GameIntegrationResult<GameIntegrationView>.Problem(problem!.StatusCode, problem.Code!, problem.Error!);
        }

        if (target.Snapshot.State is not (ActualState.Starting or ActualState.Running))
        {
            return GameIntegrationResult<GameIntegrationView>.Ok(Offline(target));
        }

        var data = await CallAsync(
            target,
            (provider, integrationContext) => provider.ReadAsync(integrationContext, cancellationToken),
            cancellationToken);
        return data.Value is { } integrationData
            ? GameIntegrationResult<GameIntegrationView>.Ok(new GameIntegrationView(
                target.Provider.Id,
                target.Integration.Label,
                target.Provider.Capabilities,
                Online: true,
                integrationData.Info,
                integrationData.Metrics,
                integrationData.Players))
            : GameIntegrationResult<GameIntegrationView>.Problem(data.StatusCode, data.Code!, data.Error!);
    }

    public async Task<GameIntegrationResult<GameIntegrationActionResult>> ActAsync(
        string instanceId,
        string action,
        GameIntegrationActionRequest request,
        CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return GameIntegrationResult<GameIntegrationActionResult>.Problem(problem!.StatusCode, problem.Code!, problem.Error!);
        }

        if (target.Snapshot.State is not ActualState.Running)
        {
            return GameIntegrationResult<GameIntegrationActionResult>.Problem(
                StatusCodes.Status409Conflict,
                ProblemCodes.InvalidState,
                $"The server is {target.Snapshot.State}; start it before using its game API.");
        }

        var result = await CallAsync(
            target,
            (provider, integrationContext) => provider.ActAsync(integrationContext, action, request, cancellationToken),
            cancellationToken);
        if (result.Value is not null)
        {
            logger.LogInformation(
                "Game API action {Action} succeeded for {Instance}.",
                LogText.Clean(action),
                LogText.Clean(instanceId));
        }

        return result;
    }

    private static GameIntegrationView Offline(GameIntegrationTarget target) => new(
        target.Provider.Id,
        target.Integration.Label,
        target.Provider.Capabilities,
        Online: false,
        Info: null,
        Metrics: null,
        Players: []);

    private async Task<GameIntegrationResult<T>> CallAsync<T>(
        GameIntegrationTarget target,
        Func<IGameIntegrationProvider, GameIntegrationContext, Task<T>> call,
        CancellationToken cancellationToken)
        where T : class
    {
        var settings = await target.Actor.GetSettingsAsync();
        var secrets = context.Secrets.Names(SecretScopes.Instance(target.Snapshot.Id))
            .ToDictionary(
                name => name,
                name => context.Secrets.Get(SecretScopes.Instance(target.Snapshot.Id), name) ?? string.Empty,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> values;
        try
        {
            values = LaunchSpecBuilder.TemplateValues(
                target.Adapter,
                target.Snapshot.Id,
                target.Snapshot.DisplayName,
                InstancePaths.For(context.Options.DataRoot, target.Snapshot.Id),
                target.Snapshot.Ports,
                settings.Settings,
                secrets);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "Game API templates of {Instance} cannot be rendered: {Reason}",
                LogText.Clean(target.Snapshot.Id),
                LogText.Clean(ex.Message));
            return GameIntegrationResult<T>.Problem(
                StatusCodes.Status409Conflict,
                "integration_not_configured",
                "The game API authentication cannot be rendered; check the instance's secrets.");
        }

        string? username = null;
        string? password = null;
        if (target.Integration.Auth is { } auth)
        {
            try
            {
                username = LaunchSpecBuilder.Render(auth.Username, values);
                password = LaunchSpecBuilder.Render(auth.Password, values);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(
                    "Game API auth of {Instance} cannot be rendered: {Reason}",
                    LogText.Clean(target.Snapshot.Id),
                    LogText.Clean(ex.Message));
                return GameIntegrationResult<T>.Problem(
                    StatusCodes.Status409Conflict,
                    "integration_not_configured",
                    "The game API authentication needs a secret that is not set.");
            }
        }

        var baseUri = target.Port is { } port
            ? new Uri($"http://127.0.0.1:{port}/")
            : null;
        var integrationContext = new GameIntegrationContext(
            target.Snapshot.Id,
            baseUri,
            httpClients.CreateClient(HttpClientName),
            username,
            password,
            target.Integration.TimeoutSec,
            settings.Settings,
            target.Snapshot.DisplayName);
        try
        {
            return GameIntegrationResult<T>.Ok(await call(target.Provider, integrationContext));
        }
        catch (GameIntegrationException ex)
        {
            return GameIntegrationResult<T>.Problem(ex.Status, ex.Code, ex.Message);
        }
        catch (JsonException)
        {
            return GameIntegrationResult<T>.Problem(
                StatusCodes.Status502BadGateway,
                "integration_invalid_response",
                "The game API answered with data this version does not understand.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "Game API call of {Instance} failed: {Reason}",
                LogText.Clean(target.Snapshot.Id),
                LogText.Clean(ex.Message));
            return GameIntegrationResult<T>.Problem(
                StatusCodes.Status502BadGateway,
                "integration_unavailable",
                "The game API request failed.");
        }
    }

    private (GameIntegrationTarget? Target, GameIntegrationResult<GameIntegrationView>? Problem) FindTarget(string instanceId)
    {
        if (supervisor.Find(instanceId) is not { } actor)
        {
            return (null, GameIntegrationResult<GameIntegrationView>.Problem(
                StatusCodes.Status404NotFound,
                "not_found",
                $"No instance '{instanceId}'."));
        }

        var adapter = adapters.Find(actor.Snapshot.AdapterId);
        if (adapter?.Integration is not { } integration)
        {
            return (null, GameIntegrationResult<GameIntegrationView>.Problem(
                StatusCodes.Status409Conflict,
                "integration_not_supported",
                "This game's adapter has no game API integration."));
        }

        if (registry.Find(integration.Provider) is not { } provider)
        {
            return (null, GameIntegrationResult<GameIntegrationView>.Problem(
                StatusCodes.Status409Conflict,
                "integration_provider_missing",
                $"Core has no provider '{integration.Provider}' for this game API."));
        }

        if (integration.Transport is not ("http" or "console"))
        {
            return (null, GameIntegrationResult<GameIntegrationView>.Problem(
                StatusCodes.Status409Conflict,
                "integration_not_configured",
                $"The adapter's integration transport '{integration.Transport}' is not supported."));
        }

        if (integration.Transport == "http")
        {
            if (string.IsNullOrWhiteSpace(integration.Port)
                || !actor.Snapshot.Ports.TryGetValue(integration.Port, out var port))
            {
                return (null, GameIntegrationResult<GameIntegrationView>.Problem(
                    StatusCodes.Status409Conflict,
                    "integration_not_configured",
                    $"The adapter's integration port '{integration.Port}' is not allocated."));
            }

            return (new GameIntegrationTarget(actor, actor.Snapshot, adapter, integration, provider, port), null);
        }

        return (new GameIntegrationTarget(actor, actor.Snapshot, adapter, integration, provider, null), null);
    }

    private sealed record GameIntegrationTarget(
        InstanceActor Actor,
        InstanceSnapshot Snapshot,
        AdapterDefinition Adapter,
        AdapterIntegration Integration,
        IGameIntegrationProvider Provider,
        int? Port);
}
