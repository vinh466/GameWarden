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
}
