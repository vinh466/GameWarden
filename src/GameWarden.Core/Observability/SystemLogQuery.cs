using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Observability;

public sealed record SystemLogItem(
    long Id,
    DateTimeOffset At,
    string Level,
    string Component,
    string Category,
    string Message,
    string? Exception,
    string? InstanceId,
    string? JobId,
    string? RequestId,
    string? AgentClientId,
    string Source,
    string? Data);

public sealed record SystemLogPage(IReadOnlyList<SystemLogItem> Items, long? NextBefore);

public sealed record SystemLogCount(string Key, int Count);

public sealed record SystemLogComponent(string Component, int Count, DateTimeOffset LastAt);

public sealed record SystemLogFingerprint(string Message, int Count);

public sealed record SystemLogSummary(
    DateTimeOffset Since,
    int Total,
    IReadOnlyList<SystemLogCount> Levels,
    IReadOnlyList<SystemLogCount> Components,
    IReadOnlyList<SystemLogFingerprint> TopMessages,
    SystemLogItem? NewestError,
    SystemLogItem? NewestCritical);

/// <summary>
/// The one filter used by the Owner and Agent pages (docs/19): time, level, component, instance, job, request and text,
/// newest first with an id cursor. The table is redacted before insert; this service only selects.
/// </summary>
public sealed class SystemLogQuery(IDbContextFactory<GameWardenDb> db, TimeProvider time)
{
    public const int MaxLimit = 500;

    public async Task<SystemLogPage> QueryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? levels,
        string? component,
        string? instanceId,
        string? jobId,
        string? requestId,
        string? text,
        long? before,
        int? limit,
        CancellationToken cancellationToken)
    {
        var wantedLevels = Split(levels);
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var rows = context.SystemLog.AsNoTracking();
        if (from is { } start)
        {
            var startUtc = start.UtcDateTime;
            rows = rows.Where(row => row.At >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = end.UtcDateTime;
            rows = rows.Where(row => row.At <= endUtc);
        }

        if (wantedLevels.Length > 0)
        {
            rows = rows.Where(row => wantedLevels.Contains(row.Level));
        }

        if (field(component) is { } componentFilter)
        {
            rows = rows.Where(row => row.Component == componentFilter || row.Category == componentFilter);
        }

        if (field(instanceId) is { } instanceFilter)
        {
            var instanceComponent = "GameWarden.Instance." + instanceFilter;
            rows = rows.Where(row => row.InstanceId == instanceFilter || row.Component == instanceComponent);
        }

        if (field(jobId) is { } jobFilter)
        {
            // Older rows and job services that do not push a scope still carry the job id in the message.
            rows = rows.Where(row => row.JobId == jobFilter || row.Message.Contains(jobFilter));
        }

        if (field(requestId) is { } requestFilter)
        {
            rows = rows.Where(row => row.RequestId == requestFilter);
        }

        if (field(text) is { } textFilter)
        {
            var pattern = LikePattern(textFilter);
            rows = rows.Where(row =>
                EF.Functions.Like(row.Message, pattern, "\\") ||
                (row.Exception != null && EF.Functions.Like(row.Exception, pattern, "\\")) ||
                (row.Data != null && EF.Functions.Like(row.Data, pattern, "\\")));
        }

        if (before is { } cursor)
        {
            rows = rows.Where(row => row.Id < cursor);
        }

        var take = Math.Clamp(limit ?? 100, 1, MaxLimit);
        var items = await rows.OrderByDescending(row => row.Id).Take(take + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > take;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new SystemLogPage(
            [.. items.Select(ToItem)],
            hasMore ? items[^1].Id : null);
    }

    public async Task<SystemLogSummary> SummaryAsync(int? minutes, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().AddMinutes(-Math.Clamp(minutes ?? 60, 1, 24 * 7));
        var sinceUtc = since.UtcDateTime;
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var rows = context.SystemLog.AsNoTracking().Where(row => row.At >= sinceUtc);
        var levels = (await rows.GroupBy(row => row.Level)
                .Select(group => new { Key = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ToListAsync(cancellationToken))
            .Select(item => new SystemLogCount(item.Key, item.Count))
            .ToList();
        var components = (await rows.GroupBy(row => row.Component)
                .Select(group => new { Key = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .Take(20)
                .ToListAsync(cancellationToken))
            .Select(item => new SystemLogCount(item.Key, item.Count))
            .ToList();
        var topMessages = (await rows.GroupBy(row => row.Message)
                .Select(group => new { Key = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .Take(10)
                .ToListAsync(cancellationToken))
            .Select(item => new SystemLogFingerprint(item.Key, item.Count))
            .ToList();
        var newestError = await rows.Where(row => row.Level == "error").OrderByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        var newestCritical = await rows.Where(row => row.Level == "critical").OrderByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        return new SystemLogSummary(
            since,
            levels.Sum(item => item.Count),
            levels,
            components,
            topMessages,
            newestError is null ? null : ToItem(newestError),
            newestCritical is null ? null : ToItem(newestCritical));
    }

    /// <summary>Known components with their count and newest timestamp, for the panel's filter menu.</summary>
    public async Task<IReadOnlyList<SystemLogComponent>> ComponentsAsync(CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return (await context.SystemLog.AsNoTracking()
                .GroupBy(row => row.Component)
                .Select(group => new { Component = group.Key, Count = group.Count(), LastAt = group.Max(row => row.At) })
                .OrderByDescending(item => item.LastAt)
                .Take(200)
                .ToListAsync(cancellationToken))
            .Select(item => new SystemLogComponent(item.Component, item.Count, new DateTimeOffset(item.LastAt.Ticks, TimeSpan.Zero)))
            .ToList();
    }

    private static string? field(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string[] Split(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(level => level.ToLowerInvariant() switch
            {
                "warning" => "warn",
                "err" => "error",
                "crit" => "critical",
                var name => name,
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

    private static string LikePattern(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    internal static string? InstanceId(SystemLogRecord row) =>
        row.InstanceId ?? (row.Component.StartsWith("GameWarden.Instance.", StringComparison.Ordinal)
            ? row.Component["GameWarden.Instance.".Length..]
            : null);

    private static SystemLogItem ToItem(SystemLogRecord row) => new(
        row.Id, new DateTimeOffset(row.At.Ticks, TimeSpan.Zero), row.Level, row.Component, row.Category,
        row.Message, row.Exception, InstanceId(row), row.JobId, row.RequestId, row.AgentClientId,
        row.Source, row.Data);
}
