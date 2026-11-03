using System.Text.Json;
using System.Threading.Channels;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Observability;

/// <summary>
/// Queues system-log records and writes them in small SQLite batches (docs/19). Callers only enqueue; a slow or broken
/// database never delays a request or a server transition. Redaction happens here, before a record can reach SQLite.
/// </summary>
public sealed class SystemLogWriter(
    IDbContextFactory<GameWardenDb> db,
    TimeProvider time) : BackgroundService
{
    public const int Capacity = 20_000;
    public const int KeptRows = 250_000;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Channel<SystemLogRecord> _queue = Channel.CreateBounded<SystemLogRecord>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    public bool Write(SystemLogRecord record) => _queue.Writer.TryWrite(Sanitize(record));

    /// <summary>Structured record for a component that is not just a formatted log line.</summary>
    public bool Write(
        string source,
        string component,
        string level,
        string message,
        string? exception = null,
        string? instanceId = null,
        string? jobId = null,
        string? requestId = null,
        string? agentClientId = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        Write(new SystemLogRecord
        {
            At = time.GetUtcNow().UtcDateTime,
            Level = level,
            Component = component,
            Category = component,
            Message = message,
            Exception = exception,
            InstanceId = instanceId,
            JobId = jobId,
            RequestId = requestId,
            AgentClientId = agentClientId,
            Source = source,
            Data = data is null ? null : JsonSerializer.Serialize(data, Json),
        });

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ReadLoopAsync(stoppingToken), PruneLoopAsync(stoppingToken));

    private async Task ReadLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new List<SystemLogRecord>(100);
                while (batch.Count < 100 && _queue.Reader.TryRead(out var record))
                {
                    batch.Add(record);
                }

                if (batch.Count > 0)
                {
                    await SaveAsync(batch, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Never route this through ILogger: that would enqueue another record and could loop.
            Console.Error.WriteLine($"GameWarden system log stopped: {ex.Message}");
        }
    }

    /// <summary>Pruning runs at startup and hourly, even when no new records arrive (docs/19 §7).</summary>
    private async Task PruneLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PruneAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PruneAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GameWarden system log pruning stopped: {ex.Message}");
        }
    }

    private async Task SaveAsync(IReadOnlyList<SystemLogRecord> batch, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            context.SystemLog.AddRange(batch);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The file log remains the fallback; losing a diagnostic batch must not touch a game or a request.
            Console.Error.WriteLine($"GameWarden system log dropped {batch.Count} record(s): {ex.Message}");
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await db.CreateDbContextAsync(cancellationToken);
            var cutoff = time.GetUtcNow().UtcDateTime - Retention;
            await context.SystemLog.Where(record => record.At < cutoff).ExecuteDeleteAsync(cancellationToken);
            var newest = await context.SystemLog.MaxAsync(record => (long?)record.Id, cancellationToken) ?? 0;
            await context.SystemLog
                .Where(record => record.Id <= newest - KeptRows)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"GameWarden system log prune failed: {ex.Message}");
        }
    }

    private SystemLogRecord Sanitize(SystemLogRecord record)
    {
        record.Level = record.Level.ToLowerInvariant() switch
        {
            "warning" => "warn",
            "err" => "error",
            "crit" => "critical",
            var level => level,
        };
        record.Message = Cap(LogSanitizer.Clean(record.Message), 4000);
        record.Exception = record.Exception is null ? null : Cap(LogSanitizer.Clean(record.Exception), 8000);
        record.Data = record.Data is null ? null : Cap(LogSanitizer.Clean(record.Data), 8000);
        return record;
    }

    private static string Cap(string text, int length) =>
        text.Length <= length ? text : length <= 1 ? text[..length] : text[..(length - 1)] + "…";
}
