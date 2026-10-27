using GameWarden.Core.Adapters;
using GameWarden.Core.Checks;
using GameWarden.Core.Hosting;
using GameWarden.Core.Isolation;
using GameWarden.Core.Network;
using GameWarden.Core.Secrets;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Api;

/// <summary>The part of docs/05-api.md implemented so far.</summary>
public static class ApiEndpoints
{
    public static void MapGameWardenApi(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        var api = app.MapGroup("/api/v1").AddEndpointFilter<CliTokenFilter>();

        api.MapGet("/system", (IOptions<GameWardenOptions> options, IsolationMode isolation, AdapterCatalog catalog) =>
        {
            var o = options.Value;
            return Results.Ok(new
            {
                version = CoreInfo.Version,
                startedAt = CoreInfo.StartedAt,
                dataRoot = o.DataRoot,
                isolation = isolation.Enabled,
                runAsUser = isolation.Enabled ? o.RunAsUserName : null,
                vpnRanges = o.ResolvedVpnRanges,
                machine = MachineResources.Read(o.DataRoot),
                steamCmdInstalled = File.Exists(o.ResolvedSteamCmdPath),
                prerequisites = Prerequisites.Known.Select(Prerequisites.Check),
                adapterProblems = catalog.Problems.Count,
            });
        });

        // Setup calls this before removing the service (08 §4): games stop gracefully, rules and the run-as user go.
        api.MapPost("/system/uninstall-cleanup", async (Supervisor supervisor, IFirewall firewall, IsolationMode isolation, IServiceProvider services, ILogger<Supervisor> logger) =>
        {
            var stopped = await supervisor.StopAllAsync(Supervisor.ShutdownStopBudget);
            firewall.RemoveAll();
            string? accountProblem = null;
            if (isolation.Enabled)
            {
                try
                {
                    services.GetRequiredService<RunAsAccount>().Remove();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "Removing the run-as user failed.");
                    accountProblem = ex.Message;
                }
            }

            return Results.Ok(new { allInstancesStopped = stopped, firewallRulesRemoved = true, runAsUserProblem = accountProblem });
        });

        api.MapGet("/adapters", (AdapterCatalog catalog) => Results.Ok(new
        {
            adapters = catalog.Adapters.Select(adapter => new { adapter.Id, adapter.Name, adapter.Version, adapter.Level, adapter.IsBuiltIn }),
            problems = catalog.Problems,
        }));

        api.MapPost("/adapters/reload", (AdapterCatalog catalog) =>
        {
            catalog.Reload();
            return Results.Ok(new { adapters = catalog.Adapters.Count, problems = catalog.Problems });
        });

        api.MapGet("/instances", (Supervisor supervisor) => Results.Ok(supervisor.List()));

        api.MapGet("/instances/{id}", (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(actor.Snapshot) : NotFound(id));

        api.MapPost("/instances", async (CreateInstanceRequest request, Supervisor supervisor, CancellationToken cancellationToken) =>
        {
            var result = await supervisor.CreateAsync(request, cancellationToken);
            return result.Instance is { } instance
                ? Results.Created($"/api/v1/instances/{instance.Id}", instance)
                : ApiProblems.Create(result.StatusCode, result.Code ?? ProblemCodes.ValidationFailed, result.Error!);
        });

        api.MapDelete("/instances/{id}", async (string id, string? mode, string? confirm, Supervisor supervisor, CancellationToken cancellationToken) =>
        {
            if (mode != "purge")
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "Only mode=purge is available in this version (archive comes with backups).");
            }

            if (confirm != id)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"mode=purge deletes every file of the instance, saves included; confirm with confirm={id}.");
            }

            var result = await supervisor.PurgeAsync(id, cancellationToken);
            return result.Error is null
                ? Results.Ok(new { deleted = id, leftovers = result.Leftovers })
                : result.StatusCode == StatusCodes.Status404NotFound ? NotFound(id) : ApiProblems.Create(result.StatusCode, result.Code!, result.Error);
        });

        api.MapPost("/instances/{id}/start", (string id, bool? force, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.StartAsync(force == true)));
        api.MapPost("/instances/{id}/stop", (string id, Supervisor supervisor) => Command(supervisor, id, actor => actor.StopAsync()));
        api.MapPost("/instances/{id}/restart", (string id, bool? force, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.RestartAsync(force == true)));
        api.MapPost("/instances/{id}/console", (string id, ConsoleCommand body, Supervisor supervisor) =>
            Command(supervisor, id, actor => actor.SendConsoleAsync(body.Command)));

        api.MapGet("/instances/{id}/console", async (string id, int? tail, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor
                ? Results.Ok(new { lines = await actor.TailAsync(Math.Clamp(tail ?? 200, 1, 1000)) })
                : NotFound(id));

        api.MapGet("/instances/{id}/settings", async (string id, Supervisor supervisor) =>
            supervisor.Find(id) is { } actor ? Results.Ok(await actor.GetSettingsAsync()) : NotFound(id));

        api.MapPut("/instances/{id}/settings", async (string id, SettingsUpdate body, Supervisor supervisor, ISecretStore secrets) =>
        {
            if (supervisor.Find(id) is not { } actor)
            {
                return NotFound(id);
            }

            var settings = body.Settings ?? new Dictionary<string, string?>();
            var secretChanges = body.Secrets ?? new Dictionary<string, string?>();
            if (SettingsValidation.Problem(settings) is { } problem)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, problem);
            }

            if (secretChanges.Keys.FirstOrDefault(name => !SecretScopes.IsValidName(name)) is { } badName)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"Secret name '{badName}' must be 1-64 letters, digits, '.', '_' or '-'.");
            }

            foreach (var (name, value) in secretChanges)
            {
                if (string.IsNullOrEmpty(value))
                {
                    secrets.Delete(SecretScopes.Instance(id), name);
                }
                else
                {
                    secrets.Set(SecretScopes.Instance(id), name, value);
                }
            }

            return Results.Ok(settings.Count > 0 ? await actor.UpdateSettingsAsync(settings) : await actor.GetSettingsAsync());
        });
    }

    private static async Task<IResult> Command(Supervisor supervisor, string id, Func<InstanceActor, Task<CommandResult>> command)
    {
        if (supervisor.Find(id) is not { } actor)
        {
            return NotFound(id);
        }

        var result = await command(actor);
        return result.Outcome switch
        {
            CommandOutcome.Accepted => Results.Accepted($"/api/v1/instances/{id}", new { message = result.Message, instance = result.Instance }),
            CommandOutcome.NothingToDo => Results.Ok(new { message = result.Message, instance = result.Instance }),
            _ => ApiProblems.Create(StatusFor(result.Code), result.Code ?? ProblemCodes.InvalidState, result.Message),
        };
    }

    private static int StatusFor(string? code) => code switch
    {
        ProblemCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ProblemCodes.PrerequisiteMissing or ProblemCodes.ResourcesInsufficient => StatusCodes.Status412PreconditionFailed,
        _ => StatusCodes.Status409Conflict,
    };

    private static IResult NotFound(string id) => ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", $"No instance '{id}'.");
}

public sealed record ConsoleCommand(string Command);

/// <summary>Body of <c>PUT /instances/{id}/settings</c>: a null value removes a setting; an empty or null secret removes it.</summary>
public sealed record SettingsUpdate(IReadOnlyDictionary<string, string?>? Settings, IReadOnlyDictionary<string, string?>? Secrets);

internal static class ApiProblems
{
    /// <summary>RFC 7807 ProblemDetails with the stable <c>code</c> of docs/05-api.md §5.</summary>
    public static IResult Create(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
