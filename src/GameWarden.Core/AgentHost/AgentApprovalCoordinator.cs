using GameWarden.Core.Data;

namespace GameWarden.Core.AgentHost;

public sealed record AgentApprovalOutcome(AgentApprovalRecord Approval, string? Result, string Text, string? Error);

/// <summary>
/// Runs a decided approval and closes the loop (docs/21 §2.6): execute, verify, resume the conversation, update the
/// incident. The Discord surface only renders the returned text; it does not own approval semantics.
/// </summary>
public sealed class AgentApprovalCoordinator(
    AgentApprovalService approvals,
    AgentRuntime runtime,
    AgentIncidentService incidents,
    ILogger<AgentApprovalCoordinator> logger)
{
    public async Task<AgentApprovalOutcome> ApproveAsync(string id, string user, CancellationToken cancellationToken)
    {
        var result = await approvals.ApproveAsync(id, user, cancellationToken);
        if (result.Error is not null)
        {
            return new AgentApprovalOutcome(result.Approval, null, $"Không chạy được `{result.Approval.Tool}`: {result.Error}", result.Error);
        }

        if (result.Approval.Source == "incident")
        {
            await incidents.OnApprovalExecutedAsync(result.Approval, result.Result, cancellationToken);
            return new AgentApprovalOutcome(
                result.Approval,
                result.Result,
                $"Đã duyệt `{result.Approval.Tool}`.\n```\n{Truncate(result.Result ?? "Xong.")}\n```",
                null);
        }

        try
        {
            var reply = await runtime.ContinueAfterApprovalAsync(result.Approval, result.Result, cancellationToken);
            return new AgentApprovalOutcome(result.Approval, result.Result, reply.Text, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The Agent could not verify approval {Approval}.", id);
            return new AgentApprovalOutcome(
                result.Approval,
                result.Result,
                $"Đã chạy `{result.Approval.Tool}` nhưng chưa xác minh được: {ex.Message}",
                ex.Message);
        }
    }

    public async Task<AgentApprovalOutcome> DenyAsync(string id, string user, CancellationToken cancellationToken)
    {
        var result = await approvals.DenyAsync(id, user, cancellationToken);
        if (result.Error is not null)
        {
            return new AgentApprovalOutcome(result.Approval, null, $"Không từ chối được: {result.Error}", result.Error);
        }

        if (result.Approval.Source == "incident")
        {
            await incidents.OnApprovalDeniedAsync(result.Approval, cancellationToken);
        }

        return new AgentApprovalOutcome(result.Approval, null, "Đã từ chối thao tác.", null);
    }

    private static string Truncate(string text) => text.Length <= 1700 ? text : text[..1700] + "…";
}
