using System.Text.Json;
using System.Threading.Channels;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Agents;

/// <summary>
/// The authoritative Agent audit trail (docs/15): one row per request, including reads, denials and failures.
/// Writes are queued so a slow disk never delays the Agent; queries use the same table.
/// </summary>
public sealed class AgentAuditLog : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(180);

    private readonly Channel<AgentAuditRecord> _queue = Channel.CreateBounded<AgentAuditRecord>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly IDbContextFactory<GameWardenDb> _db;
    private readonly TimeProvider _time;
    private readonly ILogger<AgentAuditLog> _logger;

    public AgentAuditLog(IDbContextFactory<GameWardenDb> db, TimeProvider time, ILogger<AgentAuditLog> logger)
    {
        _db = db;
        _time = time;
        _logger = logger;
    }

    public void Record(AgentAuditRecord record) => _queue.Writer.TryWrite(record);

    public async Task<IReadOnlyList<AgentAuditView>> ListAsync(string? clientId, long? before, int limit, CancellationToken cancellationToken)
    {
        await using var context = await _db.CreateDbContextAsync(cancellationToken);
        var rows = await context.AgentAudit.AsNoTracking()
            .Where(row => (clientId == null || row.ClientId == clientId) && (before == null || row.Id < before))
            .OrderByDescending(row => row.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return [.. rows.Select(AgentAuditView.From)];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PruneAsync(stoppingToken);
            await foreach (var record in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var context = await _db.CreateDbContextAsync(stoppingToken);
                    context.AgentAudit.Add(record);
                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not save an Agent audit row ({Action}).", record.Action);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The Agent audit log stopped.");
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = _time.GetUtcNow() - Retention;
            await using var context = await _db.CreateDbContextAsync(cancellationToken);
            // SQLite cannot compare DateTimeOffset in SQL; audit rows are small and the id list is bounded by the
            // retention policy, so the comparison is done here.
            var rows = await context.AgentAudit.AsNoTracking()
                .Select(row => new { row.Id, row.At })
                .ToListAsync(cancellationToken);
            var ids = rows.Where(row => row.At < cutoff).Select(row => row.Id).ToArray();
            if (ids.Length > 0)
            {
                await context.AgentAudit.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not prune the Agent audit log.");
        }
    }

    public static string? Details(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return json.Length <= 4096 ? json : json[..4096];
    }
}
