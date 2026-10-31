namespace GameWarden.Core.Data;

/// <summary>One Agent client (docs/15): a named, permissioned caller with its own tokens and audit trail.</summary>
public sealed class AgentClientRecord
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool Enabled { get; set; }

    /// <summary>When true, every permission is allowed, including permissions added after this row was written.</summary>
    public bool TrustedFull { get; set; }

    /// <summary>JSON array of permission names; ignored while <see cref="TrustedFull"/> is true.</summary>
    public string PermissionsJson { get; set; } = "[]";

    /// <summary>JSON array of CIDR ranges this client may call from; empty means any address the panel accepts.</summary>
    public string AllowedNetworksJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public string? LastSeenIp { get; set; }

    public string? LastSeenVersion { get; set; }
}

/// <summary>A credential of one Agent client. Only the hash is stored; the plaintext is shown once.</summary>
public sealed class AgentTokenRecord
{
    public required string Id { get; set; }

    public required string ClientId { get; set; }

    public required string Name { get; set; }

    public required string SecretHash { get; set; }

    public required string Prefix { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public string? LastUsedIp { get; set; }
}

/// <summary>Global Agent feature settings; one row.</summary>
public sealed class AgentSettingsRecord
{
    public int Id { get; set; } = 1;

    public bool Enabled { get; set; }

    public bool DiagnosticsEnabled { get; set; }

    public DateTimeOffset? DiagnosticsExpiresAt { get; set; }

    public string? DiagnosticsReason { get; set; }

    public bool AdminEnabled { get; set; }

    public DateTimeOffset? AdminExpiresAt { get; set; }

    public string? AdminReason { get; set; }

    /// <summary>Destructive endpoints need both the permission and a live admin window; the Owner may turn this off.</summary>
    public bool RequireAdminMode { get; set; } = true;

    /// <summary>JSON array of CIDR ranges allowed for every Agent call; empty means the per-client ranges decide.</summary>
    public string AllowedNetworksJson { get; set; } = "[]";

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One audit row per Agent request, including denied and failed requests.</summary>
public sealed class AgentAuditRecord
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public string? ClientId { get; set; }

    public string? ClientName { get; set; }

    public string? TokenId { get; set; }

    public required string Action { get; set; }

    public string? Target { get; set; }

    public required string Outcome { get; set; }

    public int StatusCode { get; set; }

    public string? SourceIp { get; set; }

    public required string RequestId { get; set; }

    public long DurationMs { get; set; }

    /// <summary>Small, redacted JSON with query/route details; never a body or a secret.</summary>
    public string? Details { get; set; }
}
