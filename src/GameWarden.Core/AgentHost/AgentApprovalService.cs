using System.Text.Json;
using GameWarden.Core.Data;
using GameWarden.Core.Activity;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.AgentHost;

public sealed record AgentApprovalResult(AgentApprovalRecord Approval, string? Result, string? Error);

/// <summary>
/// Durable approvals for write tools (docs/20). An approval is one-shot, expires, and records who decided it. The
/// tool itself runs only here, after the decision.
/// </summary>
public sealed class AgentApprovalService(
    IDbContextFactory<GameWardenDb> db,
    AgentTools tools,
    AgentPolicyService policy,
    AgentHostSettingsService settings,
    ActivityLog activity,
    AgentTrace trace,
    TimeProvider time,
    ILogger<AgentApprovalService> logger)
{
    public Task<AgentApprovalRecord> CreateAsync(
        long runId,
        string conversationId,
        AgentTool tool,
        string arguments,
        CancellationToken cancellationToken) =>
        CreateAsync(runId, conversationId, tool, arguments, null, "chat", null, InstanceOf(arguments), null, cancellationToken);

    public async Task<AgentApprovalRecord> CreateAsync(
        long runId,
        string conversationId,
        AgentTool tool,
        string arguments,
        AgentPolicyDecision? decision,
        string source,
        string? sourceId,
        string? instanceId,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var approval = new AgentApprovalRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            RunId = runId,
            ConversationId = conversationId,
            Tool = tool.Name,
            Arguments = arguments,
            State = "pending",
            RequestedAt = now,
            ExpiresAt = now.AddMinutes(settings.Get().Autonomy.ApprovalTimeoutMinutes),
            PolicyRule = decision?.Rule,
            Impact = decision?.Impact,
            Scope = conversationId,
            Source = source,
            SourceId = sourceId,
            InstanceId = instanceId,
            ExecutionState = "notStarted",
            TraceId = traceId,
        };
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        context.AgentApprovals.Add(approval);
        await context.SaveChangesAsync(cancellationToken);
        await trace.AddEventAsync(
            traceId ?? Guid.NewGuid().ToString("N"),
            "approval.created",
            $"Approval {approval.Id} requested for {tool.Name}.",
            instanceId,
            incidentId: source == "incident" ? sourceId : null,
            cancellationToken: cancellationToken);
        return approval;
    }

    /// <summary>Create an approval from an incident card or another non-chat surface.</summary>
    public async Task<AgentApprovalRecord> CreateForToolAsync(
        string conversationId,
        AgentTool tool,
        string arguments,
        string? instanceId,
        string source,
        string? sourceId,
        string? traceId,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(arguments);
        var decision = await policy.EvaluateAsync(tool, document.RootElement, instanceId, null, traceId ?? Guid.NewGuid().ToString("N"), cancellationToken);
        if (!decision.Allowed)
        {
            throw new AgentToolException(decision.Reason);
        }

        if (!decision.RequiresApproval)
        {
            throw new AgentToolException("The policy allows this write without approval; use the runtime path.");
        }

        return await CreateAsync(0, conversationId, tool, arguments, decision, source, sourceId, instanceId, traceId, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentApprovalRecord>> PendingAsync(string? conversationId, int limit, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentApprovals.AsNoTracking()
            .Where(row => row.State == "pending"
                && (conversationId == null || row.ConversationId == conversationId)
                && row.ExpiresAt > time.GetUtcNow().UtcDateTime)
            .OrderBy(row => row.ExpiresAt)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Mark approvals that were executing when Core stopped as unknown; never retry a non-idempotent write blindly.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var stuck = await context.AgentApprovals
            .Where(row => row.State == "approved" && row.ExecutionState == "running")
            .ToListAsync(cancellationToken);
        foreach (var approval in stuck)
        {
            approval.State = "unknown";
            approval.ExecutionState = "unknown";
            approval.Verification = "Core restarted while this write was executing; verify it manually before retrying.";
            await trace.AddEventAsync(
                approval.TraceId ?? Guid.NewGuid().ToString("N"),
                "approval.unknown",
                approval.Verification,
                approval.InstanceId,
                incidentId: approval.Source == "incident" ? approval.SourceId : null,
                cancellationToken: cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentApprovalRecord>> ListAsync(string? state, int limit, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentApprovals.AsNoTracking()
            .Where(row => state == null || row.State == state)
            .OrderByDescending(row => row.RequestedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task SetVerificationAsync(string id, string verification, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var row = await context.AgentApprovals.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Verification = verification.Length > 2000 ? verification[..2000] : verification;
        row.VerifiedAt = time.GetUtcNow().UtcDateTime;
        row.ExecutionState = verification.StartsWith("ok", StringComparison.OrdinalIgnoreCase) ? "succeeded" : "failed";
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string? InstanceOf(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("instanceId", out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<int> RecentCountAsync(string conversationId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var since = time.GetUtcNow().UtcDateTime.AddHours(-1);
        return await context.AgentApprovals.CountAsync(
            row => row.ConversationId == conversationId && row.RequestedAt >= since,
            cancellationToken);
    }

    public async Task<AgentApprovalRecord?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        return await context.AgentApprovals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
    }

    public async Task SetMessageAsync(string id, string messageId, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var row = await context.AgentApprovals.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is not null)
        {
            row.MessageId = messageId;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<AgentApprovalResult> ApproveAsync(string id, string decidedBy, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var approval = await context.AgentApprovals.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (approval is null)
        {
            return new AgentApprovalResult(new AgentApprovalRecord { Id = id }, null, "Approval not found.");
        }

        if (approval.State != "pending")
        {
            return new AgentApprovalResult(approval, null, $"Approval is {approval.State}.");
        }

        if (approval.ExpiresAt < time.GetUtcNow().UtcDateTime)
        {
            approval.State = "expired";
            await context.SaveChangesAsync(cancellationToken);
            return new AgentApprovalResult(approval, null, "Approval expired.");
        }

        var tool = tools.Find(approval.Tool);
        if (tool is null || tool.Risk != AgentToolRisk.Write)
        {
            approval.State = "failed";
            await context.SaveChangesAsync(cancellationToken);
            return new AgentApprovalResult(approval, null, "The tool is no longer allowed.");
        }

        approval.State = "approved";
        approval.DecidedBy = decidedBy;
        approval.DecidedAt = time.GetUtcNow().UtcDateTime;
        approval.ExecutionState = "running";
        await context.SaveChangesAsync(cancellationToken);
        await trace.AddEventAsync(
            approval.TraceId ?? Guid.NewGuid().ToString("N"),
            "approval.approved",
            $"Approval {approval.Id} approved by {decidedBy}.",
            approval.InstanceId,
            incidentId: approval.Source == "incident" ? approval.SourceId : null,
            cancellationToken: cancellationToken);

        var started = time.GetUtcNow();
        try
        {
            var args = JsonDocument.Parse(approval.Arguments).RootElement.Clone();
            var result = await tools.ExecuteAsync(tool, args, cancellationToken);
            approval.ExecutionState = "succeeded";
            approval.ExecutedAt = time.GetUtcNow().UtcDateTime;
            context.AgentToolCalls.Add(new AgentToolCallRecord
            {
                RunId = approval.RunId,
                Tool = tool.Name,
                Arguments = approval.Arguments,
                Result = result,
                Risk = "write",
                State = "ok",
                DurationMs = (int)(time.GetUtcNow() - started).TotalMilliseconds,
                CreatedAt = started.UtcDateTime,
                TraceId = approval.TraceId,
                PolicyRule = approval.PolicyRule,
                ApprovalId = approval.Id,
                InstanceId = approval.InstanceId,
            });
            await context.SaveChangesAsync(cancellationToken);
            activity.Add("command", "info", $"{tool.Name} approved by {decidedBy} and sent.", approval.InstanceId, null, "agent-host");
            await trace.AddEventAsync(
                approval.TraceId ?? Guid.NewGuid().ToString("N"),
                "approval.executed",
                $"Approval {approval.Id} executed: {tool.Name}.",
                approval.InstanceId,
                incidentId: approval.Source == "incident" ? approval.SourceId : null,
                cancellationToken: cancellationToken);
            return new AgentApprovalResult(approval, result, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            approval.State = "failed";
            approval.ExecutionState = "failed";
            approval.ExecutedAt = time.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
            logger.LogWarning(ex, "Approved Agent tool {Tool} failed.", tool.Name);
            await trace.AddEventAsync(
                approval.TraceId ?? Guid.NewGuid().ToString("N"),
                "approval.failed",
                $"Approval {approval.Id} failed: {ex.Message}",
                approval.InstanceId,
                incidentId: approval.Source == "incident" ? approval.SourceId : null,
                cancellationToken: cancellationToken);
            return new AgentApprovalResult(approval, null, ex.Message);
        }
    }

    public async Task<AgentApprovalResult> DenyAsync(string id, string decidedBy, CancellationToken cancellationToken)
    {
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        var approval = await context.AgentApprovals.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (approval is null)
        {
            return new AgentApprovalResult(new AgentApprovalRecord { Id = id }, null, "Approval not found.");
        }

        if (approval.State != "pending")
        {
            return new AgentApprovalResult(approval, null, $"Approval is {approval.State}.");
        }

        approval.State = "denied";
        approval.DecidedBy = decidedBy;
        approval.DecidedAt = time.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        activity.Add("command", "info", $"{approval.Tool} denied by {decidedBy}.", null, null, "agent-host");
        return new AgentApprovalResult(approval, null, null);
    }
}
