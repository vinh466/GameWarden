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
    AgentHostSettingsService settings,
    ActivityLog activity,
    TimeProvider time,
    ILogger<AgentApprovalService> logger)
{
    public async Task<AgentApprovalRecord> CreateAsync(
        long runId,
        string conversationId,
        AgentTool tool,
        string arguments,
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
        };
        await using var context = await db.CreateDbContextAsync(cancellationToken);
        context.AgentApprovals.Add(approval);
        await context.SaveChangesAsync(cancellationToken);
        return approval;
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
        await context.SaveChangesAsync(cancellationToken);

        var started = time.GetUtcNow();
        try
        {
            var args = JsonDocument.Parse(approval.Arguments).RootElement.Clone();
            var result = await tools.ExecuteAsync(tool, args, cancellationToken);
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
            });
            await context.SaveChangesAsync(cancellationToken);
            activity.Add("command", "info", $"{tool.Name} approved by {decidedBy} and sent.", null, null, "agent-host");
            return new AgentApprovalResult(approval, result, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            approval.State = "failed";
            await context.SaveChangesAsync(cancellationToken);
            logger.LogWarning(ex, "Approved Agent tool {Tool} failed.", tool.Name);
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
