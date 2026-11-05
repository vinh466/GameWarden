namespace GameWarden.Core.Data;

/// <summary>One chat channel's conversation (docs/20). The id is the Discord channel id.</summary>
public sealed class AgentConversationRecord
{
    public string Id { get; set; } = string.Empty;

    public string? InstanceId { get; set; }

    public long? OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class AgentMessageRecord
{
    public long Id { get; set; }

    public string ConversationId { get; set; } = string.Empty;

    /// <summary><c>user</c> or <c>assistant</c>; tool output stays in <see cref="AgentToolCallRecord"/>.</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public sealed class AgentRunRecord
{
    public long Id { get; set; }

    public string ConversationId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string State { get; set; } = "running";

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public string? Error { get; set; }

    public string? TraceId { get; set; }

    public string? TurnId { get; set; }

    public string? PromptId { get; set; }

    public int? DurationMs { get; set; }
}

public sealed class AgentToolCallRecord
{
    public long Id { get; set; }

    public long RunId { get; set; }

    public string Tool { get; set; } = string.Empty;

    public string Arguments { get; set; } = "{}";

    public string? Result { get; set; }

    public string Risk { get; set; } = "read";

    public string State { get; set; } = "ok";

    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? TraceId { get; set; }

    public string? PolicyRule { get; set; }

    public string? ApprovalId { get; set; }

    public string? InstanceId { get; set; }

    public string? ErrorCode { get; set; }
}

public sealed class AgentApprovalRecord
{
    public string Id { get; set; } = string.Empty;

    public long RunId { get; set; }

    public string ConversationId { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public string Arguments { get; set; } = "{}";

    public string State { get; set; } = "pending";

    public string? DecidedBy { get; set; }

    public string? MessageId { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? TraceId { get; set; }

    public string? PolicyRule { get; set; }

    public string? Impact { get; set; }

    public string? Scope { get; set; }

    public string? Source { get; set; } // chat, incident, runbook

    public string? SourceId { get; set; }

    public string? InstanceId { get; set; }

    public string ExecutionState { get; set; } = "notStarted";

    public DateTime? ExecutedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? Verification { get; set; }
}

/// <summary>One incident opened by an alert, a user report or a runbook (docs/21).</summary>
public sealed class AgentIncidentRecord
{
    public string Id { get; set; } = string.Empty;

    public string? InstanceId { get; set; }

    public string Severity { get; set; } = "warning";

    public string State { get; set; } = "open";

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string Trigger { get; set; } = "activity";

    public string? TriggerId { get; set; }

    public string? ChannelId { get; set; }

    public string? ThreadId { get; set; }

    public string? MessageId { get; set; }

    public string? TraceId { get; set; }

    public DateTime OpenedAt { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Append-only incident timeline (docs/21 §2.7).</summary>
public sealed class AgentIncidentEventRecord
{
    public long Id { get; set; }

    public string IncidentId { get; set; } = string.Empty;

    public DateTime At { get; set; }

    public string Type { get; set; } = "note";

    public string Text { get; set; } = string.Empty;

    public string? Data { get; set; }

    public string? TraceId { get; set; }
}

/// <summary>One run of a declarative runbook (docs/21 §2.8).</summary>
public sealed class AgentRunbookRunRecord
{
    public string Id { get; set; } = string.Empty;

    public string RunbookId { get; set; } = string.Empty;

    public int RunbookVersion { get; set; }

    public string? IncidentId { get; set; }

    public string? InstanceId { get; set; }

    public string State { get; set; } = "running";

    public int CurrentStep { get; set; }

    public string? Checkpoint { get; set; }

    public string? Output { get; set; }

    public string? Error { get; set; }

    public string? TraceId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}

/// <summary>A policy decision, kept for "why was this allowed?" (docs/21 §2.6).</summary>
public sealed class AgentPolicyDecisionRecord
{
    public long Id { get; set; }

    public DateTime At { get; set; }

    public string? TraceId { get; set; }

    public string? TurnId { get; set; }

    public string Capability { get; set; } = string.Empty;

    public string? Action { get; set; }

    public string? InstanceId { get; set; }

    public string Rule { get; set; } = string.Empty;

    public string Decision { get; set; } = "allow";

    public string? Reason { get; set; }

    public string? InputHash { get; set; }
}

/// <summary>A redacted event in the Agent trace timeline (docs/21 §2.9).</summary>
public sealed class AgentEventRecord
{
    public long Id { get; set; }

    public DateTime At { get; set; }

    public string? TraceId { get; set; }

    public string Type { get; set; } = "event";

    public string Text { get; set; } = string.Empty;

    public string? Data { get; set; }

    public string? InstanceId { get; set; }

    public long? RunId { get; set; }

    public string? IncidentId { get; set; }
}
