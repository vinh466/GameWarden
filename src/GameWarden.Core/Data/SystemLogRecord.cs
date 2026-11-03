namespace GameWarden.Core.Data;

/// <summary>
/// One structured diagnostic record (docs/19): Core's ILogger output and explicit components, queryable by the Owner
/// and the Agent. Messages and exceptions are redacted before insert; the file logs stay the boot/fallback copy.
/// </summary>
public sealed class SystemLogRecord
{
    public long Id { get; set; }

    public DateTime At { get; set; }

    public required string Level { get; set; }

    public required string Component { get; set; }

    public required string Category { get; set; }

    public int? EventId { get; set; }

    public required string Message { get; set; }

    public string? Exception { get; set; }

    public string? InstanceId { get; set; }

    public string? JobId { get; set; }

    public string? RequestId { get; set; }

    public string? AgentClientId { get; set; }

    public required string Source { get; set; }

    public string? Data { get; set; }
}
