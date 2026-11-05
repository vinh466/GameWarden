using GameWarden.Core.Data;

namespace GameWarden.Core.AgentHost;

/// <summary>An opaque message reference a surface can later update or thread from (docs/21 §2.1).</summary>
public sealed record AgentMessageRef(ulong ChannelId, ulong MessageId, ulong? ThreadId = null);

/// <summary>
/// What the kernel needs from a chat surface. Discord is the first implementation; the panel or MCP can implement
/// the same contract without the kernel knowing their wire format.
/// </summary>
public interface IAgentMessenger
{
    bool Connected { get; }

    Task<AgentMessageRef?> PostIncidentAsync(AgentIncidentRecord incident, string? displayName, CancellationToken cancellationToken);

    Task PostIncidentNoteAsync(AgentIncidentRecord incident, string text, CancellationToken cancellationToken);

    Task<AgentMessageRef?> PostApprovalAsync(AgentApprovalRecord approval, string? displayName, CancellationToken cancellationToken);

    Task PostApprovalResultAsync(AgentApprovalRecord approval, string text, CancellationToken cancellationToken);
}
