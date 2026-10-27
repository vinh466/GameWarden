using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameWarden.Contracts;
using GameWarden.Core.Adapters;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Network;
using GameWarden.Core.Secrets;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Supervision;

/// <summary>
/// Keeps every instance in its desired state (docs/02-architecture.md §5). Stopping the supervisor for a service
/// stop or an update detaches from the Runners and leaves the games running (ADR-0003); when Windows shuts down
/// it first stops the games gracefully, keeping their desired state (ADR-0014).
/// </summary>
public sealed partial class Supervisor(SupervisorContext context, ShutdownSignal shutdown, ILogger<Supervisor> logger) : IHostedService, IAsyncDisposable
{
    /// <summary>Under Setup's 120 s pre-shutdown timeout, leaving time to save the database and exit.</summary>
    public static readonly TimeSpan ShutdownStopBudget = TimeSpan.FromSeconds(110);

    private readonly ConcurrentDictionary<string, InstanceActor> _actors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        context.Adapters.Reload();
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var records = await db.Instances.AsNoTracking().ToListAsync(cancellationToken);
        var rules = new List<FirewallRule>();
        foreach (var record in records)
        {
            // Re-apply ADR-0009's instance rules where missing, e.g. after restoring the data root on a new VM.
            context.DataRoot.PrepareInstance(InstancePaths.For(context.Options.DataRoot, record.Id));
            rules.AddRange(FirewallRules(record.Id, record.AdapterId, ReadPorts(record.PortsJson)));
            _actors[record.Id] = new InstanceActor(record, context);
        }

        context.Firewall.Reconcile(rules);
        await Task.WhenAll(_actors.Values.Select(actor => actor.InitializeAsync()));
        logger.LogInformation("Supervising {Count} instance(s).", _actors.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (shutdown.SystemShuttingDown && !_actors.IsEmpty)
        {
            logger.LogInformation("Windows is shutting down: stopping {Count} instance(s) gracefully.", _actors.Count);
            var stopped = await StopAllAsync(ShutdownStopBudget);
            if (!stopped)
            {
                logger.LogWarning("Not every instance stopped within {Budget}; the rest are left to Windows.", ShutdownStopBudget);
            }
        }

        foreach (var actor in _actors.Values)
        {
            await actor.DetachAsync();
            await actor.DisposeAsync();
        }

        _actors.Clear();
    }

    /// <summary>Stops every instance gracefully, in parallel, keeping desired states; false when <paramref name="budget"/> ran out.</summary>
    public async Task<bool> StopAllAsync(TimeSpan budget)
    {
        var stops = Task.WhenAll(_actors.Values.Select(actor => actor.StopForShutdownAsync()));
        return await Task.WhenAny(stops, Task.Delay(budget)) == stops;
    }

    public IReadOnlyList<InstanceSnapshot> List() =>
        [.. _actors.Values.Select(actor => actor.Snapshot).OrderBy(snapshot => snapshot.Id, StringComparer.Ordinal)];

    public InstanceActor? Find(string id) => _actors.GetValueOrDefault(id);

    public async Task<CreateInstanceResult> CreateAsync(CreateInstanceRequest request, CancellationToken cancellationToken)
    {
        if (!InstanceId().IsMatch(request.Id))
        {
            return CreateInstanceResult.Invalid("id must be 2-63 characters: lowercase letters, digits and '-', starting with a letter or digit.");
        }

        var adapter = context.Adapters.Find(request.AdapterId);
        if (adapter is null)
        {
            return CreateInstanceResult.Invalid($"Unknown or invalid adapter '{request.AdapterId}'.");
        }

        if (request.Settings is not null
            && SettingsValidation.Problem(request.Settings.Select(pair => KeyValuePair.Create(pair.Key, (string?)pair.Value))) is { } settingsProblem)
        {
            return CreateInstanceResult.Invalid(settingsProblem);
        }

        await _createLock.WaitAsync(cancellationToken);
        try
        {
            if (_actors.ContainsKey(request.Id))
            {
                return CreateInstanceResult.Conflict($"Instance '{request.Id}' already exists.");
            }

            var used = _actors.Values.SelectMany(actor => actor.Snapshot.Ports.Values).ToHashSet();
            IReadOnlyDictionary<string, int> ports;
            if (request.Ports is { } requested)
            {
                var expected = LaunchSpecBuilder.DefaultPorts(adapter).Keys.Order(StringComparer.Ordinal);
                if (!requested.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected))
                {
                    return CreateInstanceResult.Invalid($"ports must name exactly: {string.Join(", ", expected)}.");
                }

                var bad = requested.Values.Where(port => port is < 1 or > ushort.MaxValue || used.Contains(port)).ToArray();
                if (bad.Length > 0)
                {
                    return new CreateInstanceResult(null, $"Port {bad[0]} is out of range or used by another instance.", StatusCodes.Status409Conflict, ProblemCodes.PortConflict);
                }

                ports = requested;
            }
            else
            {
                ports = Ports.Allocate(adapter, used, context.IsPortListening);
            }

            var record = new InstanceRecord
            {
                Id = request.Id,
                AdapterId = adapter.Id,
                AdapterVersion = adapter.Version,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Id : request.DisplayName,
                DesiredState = DesiredState.Stopped,
                PersistedState = adapter.Source?.Type == "manual" ? ActualState.Stopped : ActualState.NotInstalled,
                PortsJson = JsonSerializer.Serialize(ports, ContractJson.Options),
                SettingsJson = JsonSerializer.Serialize(request.Settings ?? new Dictionary<string, string>(), ContractJson.Options),
                CreatedAt = context.Time.GetUtcNow(),
            };

            await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
            {
                db.Instances.Add(record);
                await db.SaveChangesAsync(cancellationToken);
            }

            context.DataRoot.PrepareInstance(InstancePaths.For(context.Options.DataRoot, record.Id));
            context.Firewall.SetInstance(record.Id, FirewallRules(record.Id, adapter.Id, ports));
            var actor = new InstanceActor(record, context);
            _actors[record.Id] = actor;
            logger.LogInformation(
                "Created instance {Id} from adapter {Adapter} {Version} with ports {Ports}.",
                record.Id,
                adapter.Id,
                adapter.Version,
                string.Join(", ", ports.Select(pair => $"{pair.Key}={pair.Value}")));
            return CreateInstanceResult.Created(actor.Snapshot);
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Deletes a stopped instance with everything it has: database row, secrets, firewall rules and folders
    /// (05 §2 <c>mode=purge</c>). Archiving comes with backups (M3).
    /// </summary>
    public async Task<DeleteInstanceResult> PurgeAsync(string id, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } actor)
        {
            return new DeleteInstanceResult(null, $"No instance '{id}'.", ProblemCodes.ValidationFailed, StatusCodes.Status404NotFound);
        }

        var retired = await actor.RetireAsync();
        if (retired.Outcome == CommandOutcome.Rejected)
        {
            return new DeleteInstanceResult(null, retired.Message, retired.Code, StatusCodes.Status409Conflict);
        }

        _actors.TryRemove(id, out _);
        await actor.DisposeAsync();
        await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
        {
            await db.Instances.Where(record => record.Id == id).ExecuteDeleteAsync(cancellationToken);
        }

        context.Firewall.SetInstance(id, []);
        context.Secrets.DeleteScope(SecretScopes.Instance(id));
        var leftovers = DeleteFolders(InstancePaths.For(context.Options.DataRoot, id));
        logger.LogInformation("Deleted instance {Id}.", id);
        return new DeleteInstanceResult(leftovers, null, null, StatusCodes.Status200OK);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _createLock.Dispose();
    }

    public FirewallRule[] FirewallRules(string instanceId, string adapterId, IReadOnlyDictionary<string, int> ports)
    {
        var adapter = context.Adapters.Find(adapterId);
        return [.. ports.Select(pair => new FirewallRule(instanceId, pair.Value, adapter is null ? "udp" : Ports.Protocol(adapter, pair.Key)))];
    }

    /// <summary>Game folders as gamewarden-run (ADR-0009 §3), then the now nearly empty instance folder as Core.</summary>
    private string? DeleteFolders(InstancePaths paths)
    {
        try
        {
            context.Files.Run(() =>
            {
                foreach (var folder in paths.RunAsFolders.Where(Directory.Exists))
                {
                    Directory.Delete(folder, recursive: true);
                }
            });
            if (Directory.Exists(paths.Root))
            {
                foreach (var file in Directory.EnumerateFiles(paths.Root))
                {
                    File.Delete(file);
                }

                Directory.Delete(paths.Root, recursive: false);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogWarning(ex, "Some files of {Root} could not be deleted.", paths.Root);
            return $"Some files could not be deleted ({ex.Message}); remove {paths.Root} by hand.";
        }
    }

    private static IReadOnlyDictionary<string, int> ReadPorts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(json, ContractJson.Options) ?? new Dictionary<string, int>();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex InstanceId();
}

public sealed record CreateInstanceRequest(
    string Id,
    string AdapterId,
    string? DisplayName = null,
    IReadOnlyDictionary<string, string>? Settings = null,
    IReadOnlyDictionary<string, int>? Ports = null);

public sealed record CreateInstanceResult(InstanceSnapshot? Instance, string? Error, int StatusCode, string? Code = null)
{
    public static CreateInstanceResult Created(InstanceSnapshot instance) => new(instance, null, StatusCodes.Status201Created);

    public static CreateInstanceResult Invalid(string error) => new(null, error, StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed);

    public static CreateInstanceResult Conflict(string error) => new(null, error, StatusCodes.Status409Conflict, "conflict");
}

/// <param name="Leftovers">Set when the instance is gone but some of its files could not be deleted.</param>
public sealed record DeleteInstanceResult(string? Leftovers, string? Error, string? Code, int StatusCode);

/// <summary>Setting names and values stay plain: they become single launch arguments, never parsed by a shell.</summary>
public static partial class SettingsValidation
{
    public const int MaxValueLength = 1024;

    public static string? Problem(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        foreach (var (name, value) in settings)
        {
            if (!SettingName().IsMatch(name))
            {
                return $"Setting name '{name}' must be 1-64 letters, digits, '_' or '-'.";
            }

            if (value is not null && (value.Length > MaxValueLength || value.Any(char.IsControl)))
            {
                return $"Setting '{name}' must be at most {MaxValueLength} characters without control characters.";
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SettingName();
}
