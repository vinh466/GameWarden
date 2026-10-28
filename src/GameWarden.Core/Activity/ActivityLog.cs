using System.Threading.Channels;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Activity;

/// <summary>
/// The activity feed (docs/11-web-panel.md §13): instance and job events become lines through
/// <see cref="ActivityRules"/>, commands are added by the API with the user who asked. Lines are written in order by
/// one background loop, then announced on the event bus with their id. The feed keeps the newest 10 000 lines.
/// </summary>
public sealed class ActivityLog : BackgroundService
{
    public const int KeptLines = 10_000;

    private readonly IDbContextFactory<GameWardenDb> _db;
    private readonly CoreEvents _events;
    private readonly TimeProvider _time;
    private readonly ILogger<ActivityLog> _logger;
    private readonly Channel<ActivityRecord> _queue = Channel.CreateUnbounded<ActivityRecord>(new UnboundedChannelOptions { SingleReader = true });

    public ActivityLog(IDbContextFactory<GameWardenDb> db, CoreEvents events, TimeProvider time, ILogger<ActivityLog> logger)
    {
        _db = db;
        _events = events;
        _time = time;
        _logger = logger;
        events.InstanceChanged += (before, after) =>
        {
            if (ActivityRules.ForInstance(before, after) is { } entry)
            {
                Add(entry, after.Id);
            }
        };
        events.InstanceRemoved += id => Add("instance.deleted", "info", "Deleted with all its files.", id);
        events.JobChanged += job =>
        {
            if (job.FinishedAt is not null && ActivityRules.ForJob(job) is { } entry)
            {
                Add(entry, job.InstanceId, job.Id);
            }
        };
    }

    public void Add(string kind, string level, string message, string? instanceId = null, string? jobId = null, string? user = null) =>
        _queue.Writer.TryWrite(new ActivityRecord
        {
            At = _time.GetUtcNow(),
            Kind = kind,
            Level = level,
            Message = message.Length > 1024 ? message[..1024] : message,
            InstanceId = instanceId,
            JobId = jobId,
            User = user,
        });

    /// <summary>Newest first; <paramref name="before"/> pages back from an id.</summary>
    public async Task<IReadOnlyList<ActivityRecord>> ListAsync(string? instanceId, long? before, int limit, CancellationToken cancellationToken)
    {
        await using var db = await _db.CreateDbContextAsync(cancellationToken);
        return await db.Activity.AsNoTracking()
            .Where(item => (instanceId == null || item.InstanceId == instanceId) && (before == null || item.Id < before))
            .OrderByDescending(item => item.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    // The feed is a convenience: a database problem here is logged and never stops Core.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PruneAsync(stoppingToken);
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var db = await _db.CreateDbContextAsync(stoppingToken);
                    db.Activity.Add(item);
                    await db.SaveChangesAsync(stoppingToken);
                    _events.RaiseActivityAdded(item);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not save an activity line ({Kind}).", item.Kind);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The activity log stopped.");
        }
    }

    private void Add(ActivityEntry entry, string? instanceId, string? jobId = null) =>
        Add(entry.Kind, entry.Level, entry.Message, instanceId, jobId);

    /// <summary>Keeps the newest <see cref="KeptLines"/>; ids grow with time, so the cut is one delete by id.</summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var db = await _db.CreateDbContextAsync(cancellationToken);
        var newest = await db.Activity.MaxAsync(item => (long?)item.Id, cancellationToken) ?? 0;
        await db.Activity.Where(item => item.Id <= newest - KeptLines).ExecuteDeleteAsync(cancellationToken);
    }
}
