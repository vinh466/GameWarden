namespace GameWarden.Core.Data;

/// <summary>One notification event (docs/18). The event is provider-neutral; deliveries point at destinations.</summary>
public sealed class NotificationEventRecord
{
    public long Id { get; set; }

    public DateTime At { get; set; }

    public required string Topic { get; set; }

    public required string Severity { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public string? FieldsJson { get; set; }

    public string? InstanceId { get; set; }

    public string? JobId { get; set; }

    public string? Url { get; set; }

    public string? DedupeKey { get; set; }

    public required string Source { get; set; }
}

public sealed class NotificationDeliveryRecord
{
    public long Id { get; set; }

    public long EventId { get; set; }

    public required string DestinationId { get; set; }

    public required string Mode { get; set; }

    public required string State { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public string? MessageId { get; set; }

    public DateTime? SentAt { get; set; }
}
