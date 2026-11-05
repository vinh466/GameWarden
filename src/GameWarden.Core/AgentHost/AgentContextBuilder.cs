using System.Text;
using GameWarden.Core.Activity;
using GameWarden.Core.Data;
using GameWarden.Core.Jobs;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// A bounded, redacted snapshot of the machine and the conversation inserted into the prompt before every model call
/// (docs/21 §2.2). It removes the "call five tools before you know where you are" step and makes answers consistent.
/// </summary>
public sealed class AgentContextBuilder(
    Supervisor supervisor,
    JobService jobs,
    ActivityLog activity,
    IDbContextFactory<GameWardenDb> db,
    AgentHostSettingsService settings,
    TimeProvider time)
{
    public async Task<string> BuildAsync(
        string? instanceId,
        string? channelName,
        string userName,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var vietnamese = settings.Get().Discord.Language == "vi";
        var builder = new StringBuilder();
        builder.AppendLine(vietnamese ? "Bối cảnh hiện tại:" : "Current context:");
        builder.AppendLine(vietnamese
            ? $"- Kênh: {(channelName is { Length: > 0 } ? $"#{channelName}" : "không rõ")} → {(instanceId ?? "system")}"
            : $"- Channel: {(channelName is { Length: > 0 } ? $"#{channelName}" : "unknown")} → {(instanceId ?? "system")}");
        builder.AppendLine(vietnamese
            ? $"- Người dùng: {userName}"
            : $"- User: {userName}");
        builder.AppendLine(vietnamese
            ? $"- Giờ địa phương: {time.GetLocalNow():yyyy-MM-dd HH:mm zzz} ({time.GetUtcNow():HH:mm} UTC)"
            : $"- Local time: {time.GetLocalNow():yyyy-MM-dd HH:mm zzz} ({time.GetUtcNow():HH:mm} UTC)");

        if (instanceId is { Length: > 0 } && supervisor.Find(instanceId) is { } actor)
        {
            var snapshot = actor.Snapshot;
            builder.AppendLine(vietnamese
                ? $"- Server: {snapshot.Id} ({snapshot.DisplayName}) · state {snapshot.State} · desired {snapshot.DesiredState} · restarts {snapshot.RecentRestarts}"
                : $"- Server: {snapshot.Id} ({snapshot.DisplayName}) · state {snapshot.State} · desired {snapshot.DesiredState} · restarts {snapshot.RecentRestarts}");
            if (snapshot.LastProblem is { Length: > 0 } problem)
            {
                builder.AppendLine(vietnamese ? $"- Lỗi gần nhất: {Clip(problem, 300)}" : $"- Last problem: {Clip(problem, 300)}");
            }
        }
        else if (instanceId is { Length: > 0 })
        {
            builder.AppendLine(vietnamese ? $"- Server: {instanceId} (không tìm thấy trong Supervisor)" : $"- Server: {instanceId} (not found in Supervisor)");
        }

        var openJobs = (await jobs.ListAsync(instanceId, cancellationToken))
            .Where(job => job.State is JobState.Queued or JobState.Running)
            .Take(3)
            .Select(job => $"{job.Type} {job.State} {job.Progress}%")
            .ToList();
        builder.AppendLine(vietnamese
            ? $"- Job đang mở: {(openJobs.Count == 0 ? "không" : string.Join(", ", openJobs))}"
            : $"- Open jobs: {(openJobs.Count == 0 ? "none" : string.Join(", ", openJobs))}");

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var approvals = await context.AgentApprovals.AsNoTracking()
            .Where(row => row.ConversationId == conversationId && row.State == "pending")
            .OrderBy(row => row.ExpiresAt)
            .Take(3)
            .Select(row => new { row.Tool, row.ExpiresAt, row.Impact })
            .ToListAsync(cancellationToken);
        builder.AppendLine(vietnamese
            ? $"- Approval đang chờ: {(approvals.Count == 0 ? "không" : string.Join("; ", approvals.Select(row => $"{row.Tool} hết hạn {row.ExpiresAt.ToLocalTime():HH:mm}")))}"
            : $"- Pending approvals: {(approvals.Count == 0 ? "none" : string.Join("; ", approvals.Select(row => $"{row.Tool} expires {row.ExpiresAt.ToLocalTime():HH:mm}")))}");

        if (instanceId is { Length: > 0 })
        {
            var incidents = await context.AgentIncidents.AsNoTracking()
                .Where(row => row.InstanceId == instanceId && row.State != "closed" && row.State != "false-alarm")
                .OrderByDescending(row => row.OpenedAt)
                .Take(3)
                .Select(row => new { row.Id, row.State, row.Title })
                .ToListAsync(cancellationToken);
            builder.AppendLine(vietnamese
                ? $"- Incident đang mở: {(incidents.Count == 0 ? "không" : string.Join("; ", incidents.Select(row => $"{row.Id[..Math.Min(8, row.Id.Length)]} {row.State}: {row.Title}")))}"
                : $"- Open incidents: {(incidents.Count == 0 ? "none" : string.Join("; ", incidents.Select(row => $"{row.Id[..Math.Min(8, row.Id.Length)]} {row.State}: {row.Title}")))}");
        }

        var recent = await activity.ListAsync(instanceId, null, 5, cancellationToken);
        if (recent.Count > 0)
        {
            builder.AppendLine(vietnamese ? "- Hoạt động gần nhất:" : "- Recent activity:");
            foreach (var item in recent.OrderBy(item => item.At))
            {
                builder.AppendLine($"  - {item.At:HH:mm} {item.Kind} {Clip(item.Message, 180)}");
            }
        }

        return builder.ToString();
    }

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
