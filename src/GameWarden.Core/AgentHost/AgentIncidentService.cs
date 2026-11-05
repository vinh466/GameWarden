using System.Text.Json;
using System.Threading.Channels;
using GameWarden.Core.Data;
using GameWarden.Core.Events;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.AgentHost;

/// <summary>
/// First-class incidents (docs/21 §2.7). Activity is the trigger: a crash, a faulted instance or a failed job opens
/// or attaches to an incident, posts an incident card in the mapped channel and runs a read-only triage runbook.
/// Write actions remain approvals.
/// </summary>
public sealed class AgentIncidentService(
    IDbContextFactory<GameWardenDb> db,
    CoreEvents events,
    Supervisor supervisor,
    AgentRunbookService runbooks,
    AgentApprovalService approvals,
    AgentTools tools,
    IAgentMessenger messenger,
    AgentTrace trace,
    TimeProvider time,
    ILogger<AgentIncidentService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly Channel<ActivityRecord> _queue = Channel.CreateBounded<ActivityRecord>(
        new BoundedChannelOptions(1000) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the activity subscription is active; tests use it before raising an event.</summary>
    public Task Started => _started.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        events.ActivityAdded += OnActivity;
        _started.TrySetResult();
        logger.LogInformation("Operations Kernel incident engine started.");
        try
        {
            await foreach (var activity in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await HandleAsync(activity, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            events.ActivityAdded -= OnActivity;
        }
    }

    private void OnActivity(ActivityRecord activity) => _queue.Writer.TryWrite(activity);

    /// <summary>Process one activity directly; used by the event subscription and by tests.</summary>
    public Task ProcessAsync(ActivityRecord activity, CancellationToken cancellationToken) =>
        HandleAsync(activity, cancellationToken);

    private async Task HandleAsync(ActivityRecord activity, CancellationToken cancellationToken)
    {
        try
        {
            var traceId = trace.NewTraceId();
            switch (activity.Kind)
            {
                case "instance.crashed":
                case "instance.faulted":
                    await OpenOrAttachAsync(activity, traceId, cancellationToken);
                    break;
                case "job.failed" when activity.Level == "error":
                    await OpenOrAttachAsync(activity, traceId, cancellationToken);
                    break;
                case "instance.running":
                    await RecoverAsync(activity, traceId, cancellationToken);
                    break;
                case "job.succeeded":
                    await ResolveJobAsync(activity, traceId, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not process incident activity {Kind}.", activity.Kind);
        }
    }

    private async Task OpenOrAttachAsync(ActivityRecord activity, string traceId, CancellationToken cancellationToken)
    {
        if (activity.InstanceId is not { Length: > 0 } instanceId)
        {
            return;
        }

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var triggers = activity.Kind is "instance.crashed" or "instance.faulted"
            ? new[] { "instance.crashed", "instance.faulted" }
            : new[] { activity.Kind };
        var existing = await context.AgentIncidents
            .Where(row => row.InstanceId == instanceId
                && triggers.Contains(row.Trigger)
                && row.State != "closed"
                && row.State != "false-alarm"
                && row.OpenedAt > now.AddMinutes(-10))
            .OrderByDescending(row => row.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            await AddEventAsync(context, existing, "activity", activity.Message, traceId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await PostNoteAsync(existing, activity.Message, cancellationToken);
            return;
        }

        var display = supervisor.Find(instanceId)?.Snapshot.DisplayName ?? instanceId;
        var incident = new AgentIncidentRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            Severity = activity.Kind == "instance.faulted" ? "critical" : "error",
            State = "open",
            Title = $"{display}: {activity.Message}",
            Summary = activity.Message,
            Trigger = activity.Kind,
            TriggerId = activity.Id.ToString(),
            TraceId = traceId,
            OpenedAt = now,
            UpdatedAt = now,
        };
        context.AgentIncidents.Add(incident);
        await AddEventAsync(context, incident, "opened", activity.Message, traceId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await trace.AddEventAsync(traceId, "incident.opened", incident.Title, instanceId, incidentId: incident.Id, cancellationToken: cancellationToken);
        var refMessage = await messenger.PostIncidentAsync(incident, display, cancellationToken);
        if (refMessage is not null)
        {
            incident.ChannelId = refMessage.ChannelId.ToString();
            incident.MessageId = refMessage.MessageId.ToString();
            incident.ThreadId = refMessage.ThreadId?.ToString();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RecoverAsync(ActivityRecord activity, string traceId, CancellationToken cancellationToken)
    {
        if (activity.InstanceId is not { Length: > 0 } instanceId)
        {
            return;
        }

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents
            .Where(row => row.InstanceId == instanceId
                && row.State != "closed"
                && row.State != "false-alarm"
                && (row.Trigger == "instance.crashed" || row.Trigger == "instance.faulted"))
            .OrderByDescending(row => row.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (incident is null)
        {
            return;
        }

        await AddEventAsync(context, incident, "recovered", activity.Message, traceId, cancellationToken);
        incident.State = "resolved";
        incident.ResolvedAt = time.GetUtcNow().UtcDateTime;
        incident.UpdatedAt = time.GetUtcNow().UtcDateTime;
        incident.Summary = "Server is running again.";
        await context.SaveChangesAsync(cancellationToken);
        await trace.AddEventAsync(traceId, "incident.resolved", incident.Title, instanceId, incidentId: incident.Id, cancellationToken: cancellationToken);
        await PostNoteAsync(incident, "✅ Server đã chạy lại; incident được đánh dấu resolved.", cancellationToken);
    }

    private async Task ResolveJobAsync(ActivityRecord activity, string traceId, CancellationToken cancellationToken)
    {
        if (activity.InstanceId is not { Length: > 0 } instanceId)
        {
            return;
        }

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents
            .Where(row => row.InstanceId == instanceId
                && row.Trigger == "job.failed"
                && row.State != "closed"
                && row.State != "false-alarm")
            .OrderByDescending(row => row.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (incident is null)
        {
            return;
        }

        await AddEventAsync(context, incident, "recovered", activity.Message, traceId, cancellationToken);
        incident.State = "resolved";
        incident.ResolvedAt = time.GetUtcNow().UtcDateTime;
        incident.UpdatedAt = time.GetUtcNow().UtcDateTime;
        incident.Summary = activity.Message;
        await context.SaveChangesAsync(cancellationToken);
        await PostNoteAsync(incident, $"✅ Job đã thành công trở lại: {activity.Message}", cancellationToken);
    }

    /// <summary>Run the read-only crash triage runbook and post its summary in the incident thread.</summary>
    public async Task<string> DiagnoseAsync(string incidentId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.FirstOrDefaultAsync(row => row.Id == incidentId, cancellationToken)
            ?? throw new AgentToolException($"No incident '{incidentId}'.");
        incident.State = "diagnosing";
        incident.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await AddEventAsync(context, incident, "diagnosis.started", "Read-only triage started.", incident.TraceId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await PostNoteAsync(incident, "🔎 Đang chạy chẩn đoán read-only...", cancellationToken);

        var outcome = await runbooks.RunAsync("generic.crash-triage", incident.Id, incident.InstanceId, incident.TraceId ?? trace.NewTraceId(), cancellationToken);
        var summary = outcome.Error is { Length: > 0 }
            ? $"Chẩn đoán chưa hoàn tất: {outcome.Error}"
            : Summary(outcome.Output);
        await using (var update = await db.CreateDbContextAsync(cancellationToken))
        {
            var row = await update.AgentIncidents.FirstAsync(item => item.Id == incidentId, cancellationToken);
            row.State = "mitigating";
            row.Summary = summary;
            row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            update.AgentIncidentEvents.Add(new AgentIncidentEventRecord
            {
                IncidentId = incidentId,
                At = time.GetUtcNow().UtcDateTime,
                Type = "diagnosis",
                Text = summary,
                Data = outcome.Output,
                TraceId = incident.TraceId,
            });
            await update.SaveChangesAsync(cancellationToken);
        }

        await PostNoteAsync(incident, $"**Chẩn đoán**\n{summary}", cancellationToken);
        return summary;
    }

    public async Task<AgentApprovalRecord> ProposeRestartAsync(string incidentId, string requestedBy, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.AsNoTracking().FirstOrDefaultAsync(row => row.Id == incidentId, cancellationToken)
            ?? throw new AgentToolException($"No incident '{incidentId}'.");
        var tool = tools.Find("instance.control") ?? throw new AgentToolException("instance.control is not registered.");
        var instanceId = incident.InstanceId ?? throw new AgentToolException("The incident has no instance.");
        var arguments = JsonSerializer.Serialize(new { instanceId, action = "restart" });
        var conversationChannel = incident.ThreadId is { Length: > 0 } thread
            ? thread
            : incident.ChannelId;
        var conversationId = ulong.TryParse(conversationChannel, out var channelId)
            ? AgentRuntime.ConversationId(channelId)
            : $"incident:{incident.Id}";
        var approval = await approvals.CreateForToolAsync(
            conversationId,
            tool,
            arguments,
            instanceId,
            "incident",
            incident.Id,
            incident.TraceId ?? trace.NewTraceId(),
            cancellationToken);
        var display = supervisor.Find(instanceId)?.Snapshot.DisplayName ?? instanceId;
        var message = await messenger.PostApprovalAsync(approval, display, cancellationToken);
        await using (var update = await db.CreateDbContextAsync(cancellationToken))
        {
            var row = await update.AgentIncidents.FirstAsync(item => item.Id == incidentId, cancellationToken);
            row.State = "mitigating";
            row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            update.AgentIncidentEvents.Add(new AgentIncidentEventRecord
            {
                IncidentId = incidentId,
                At = time.GetUtcNow().UtcDateTime,
                Type = "approval.requested",
                Text = $"Restart requested by {requestedBy}.",
                Data = message is null ? null : $"discord:{message.ChannelId}/{message.MessageId}",
                TraceId = approval.TraceId,
            });
            await update.SaveChangesAsync(cancellationToken);
        }

        return approval;
    }

    public async Task OnApprovalExecutedAsync(AgentApprovalRecord approval, string? result, CancellationToken cancellationToken)
    {
        if (approval.Source != "incident" || approval.SourceId is not { Length: > 0 } incidentId)
        {
            return;
        }

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.FirstOrDefaultAsync(row => row.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return;
        }

        var snapshot = approval.InstanceId is { Length: > 0 } instanceId ? supervisor.Find(instanceId)?.Snapshot : null;
        var healthy = snapshot?.State == ActualState.Running;
        var verification = healthy
            ? $"ok: {approval.InstanceId} is {snapshot!.State}."
            : $"not verified: {approval.InstanceId} is {(snapshot is null ? "unknown" : snapshot.State.ToString())}.";
        await approvals.SetVerificationAsync(approval.Id, verification, cancellationToken);
        await AddEventAsync(context, incident, "approval.executed", verification, approval.TraceId, cancellationToken);
        incident.State = healthy ? "resolved" : "mitigating";
        incident.ResolvedAt = healthy ? time.GetUtcNow().UtcDateTime : null;
        incident.UpdatedAt = time.GetUtcNow().UtcDateTime;
        incident.Summary = verification;
        await context.SaveChangesAsync(cancellationToken);
        await PostNoteAsync(incident, healthy ? $"✅ Đã restart và xác minh: {verification}" : $"⚠️ Chưa xác minh được: {verification}", cancellationToken);
    }

    public async Task OnApprovalDeniedAsync(AgentApprovalRecord approval, CancellationToken cancellationToken)
    {
        if (approval.Source != "incident" || approval.SourceId is not { Length: > 0 } incidentId)
        {
            return;
        }

        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.FirstOrDefaultAsync(row => row.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return;
        }

        await AddEventAsync(context, incident, "approval.denied", $"Restart denied by {approval.DecidedBy}.", approval.TraceId, cancellationToken);
        incident.State = "open";
        incident.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        await PostNoteAsync(incident, "Restart đã bị từ chối; incident vẫn mở.", cancellationToken);
    }

    public async Task SilenceAsync(string incidentId, string user, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.FirstOrDefaultAsync(row => row.Id == incidentId, cancellationToken)
            ?? throw new AgentToolException($"No incident '{incidentId}'.");
        incident.State = "false-alarm";
        incident.ClosedAt = time.GetUtcNow().UtcDateTime;
        incident.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await AddEventAsync(context, incident, "silenced", $"Silenced by {user}.", incident.TraceId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await PostNoteAsync(incident, $"🔇 Incident đã được {user} tắt.", cancellationToken);
    }

    public async Task<IReadOnlyList<AgentIncidentRecord>> ListAsync(
        string? instanceId,
        string? state,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentIncidents.AsNoTracking()
            .Where(row => (instanceId == null || row.InstanceId == instanceId)
                && (state == null || row.State == state))
            .OrderByDescending(row => row.OpenedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task<AgentIncidentRecord?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentIncidents.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
    }

    public async Task<AgentIncidentView?> ViewAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var incident = await context.AgentIncidents.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        var timeline = await context.AgentIncidentEvents.AsNoTracking()
            .Where(row => row.IncidentId == id)
            .OrderBy(row => row.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        return new AgentIncidentView(incident, timeline);
    }

    private async Task PostNoteAsync(AgentIncidentRecord incident, string text, CancellationToken cancellationToken)
    {
        try
        {
            await messenger.PostIncidentNoteAsync(incident, text, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not post incident {Incident} note.", incident.Id);
        }
    }

    private static async Task AddEventAsync(
        GameWardenDb context,
        AgentIncidentRecord incident,
        string type,
        string text,
        string? traceId,
        CancellationToken cancellationToken)
    {
        context.AgentIncidentEvents.Add(new AgentIncidentEventRecord
        {
            IncidentId = incident.Id,
            At = DateTime.UtcNow,
            Type = type,
            Text = text.Length > 4000 ? text[..4000] : text,
            TraceId = traceId,
        });
        await Task.CompletedTask;
    }

    private static string Summary(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.TryGetProperty("summary", out var summary)
                && summary.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? output;
            }
        }
        catch (JsonException)
        {
        }

        return output.Length > 4000 ? output[..4000] + "…" : output;
    }
}
