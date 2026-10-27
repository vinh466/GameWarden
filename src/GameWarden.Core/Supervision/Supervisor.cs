using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameWarden.Contracts;
using GameWarden.Core.Adapters;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Supervision;

/// <summary>
/// Keeps every instance in its desired state (docs/02-architecture.md §5). Stopping the supervisor — a service
/// stop or an update — detaches from the Runners and leaves the games running (ADR-0003).
/// </summary>
public sealed partial class Supervisor(SupervisorContext context, ILogger<Supervisor> logger) : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, InstanceActor> _actors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        context.Adapters.Reload();
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var records = await db.Instances.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var record in records)
        {
            _actors[record.Id] = new InstanceActor(record, context);
        }

        await Task.WhenAll(_actors.Values.Select(actor => actor.InitializeAsync()));
        logger.LogInformation("Supervising {Count} instance(s).", _actors.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var actor in _actors.Values)
        {
            await actor.DetachAsync();
            await actor.DisposeAsync();
        }

        _actors.Clear();
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

        await _createLock.WaitAsync(cancellationToken);
        try
        {
            if (_actors.ContainsKey(request.Id))
            {
                return CreateInstanceResult.Conflict($"Instance '{request.Id}' already exists.");
            }

            var record = new InstanceRecord
            {
                Id = request.Id,
                AdapterId = adapter.Id,
                AdapterVersion = adapter.Version,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Id : request.DisplayName,
                DesiredState = DesiredState.Stopped,
                PersistedState = adapter.Source?.Type == "manual" ? ActualState.Stopped : ActualState.NotInstalled,
                PortsJson = JsonSerializer.Serialize(LaunchSpecBuilder.DefaultPorts(adapter), ContractJson.Options),
                SettingsJson = JsonSerializer.Serialize(request.Settings ?? new Dictionary<string, string>(), ContractJson.Options),
                CreatedAt = context.Time.GetUtcNow(),
            };

            await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
            {
                db.Instances.Add(record);
                await db.SaveChangesAsync(cancellationToken);
            }

            InstancePaths.For(context.Options.DataRoot, record.Id).Create();
            var actor = new InstanceActor(record, context);
            _actors[record.Id] = actor;
            logger.LogInformation("Created instance {Id} from adapter {Adapter} {Version}.", record.Id, adapter.Id, adapter.Version);
            return CreateInstanceResult.Created(actor.Snapshot);
        }
        finally
        {
            _createLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _createLock.Dispose();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex InstanceId();
}

public sealed record CreateInstanceRequest(
    string Id,
    string AdapterId,
    string? DisplayName = null,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record CreateInstanceResult(InstanceSnapshot? Instance, string? Error, int StatusCode)
{
    public static CreateInstanceResult Created(InstanceSnapshot instance) => new(instance, null, StatusCodes.Status201Created);

    public static CreateInstanceResult Invalid(string error) => new(null, error, StatusCodes.Status400BadRequest);

    public static CreateInstanceResult Conflict(string error) => new(null, error, StatusCodes.Status409Conflict);
}
