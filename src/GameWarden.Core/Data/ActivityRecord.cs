namespace GameWarden.Core.Data;

/// <summary>
/// One line of the activity feed (docs/03-domain-model.md §6, docs/11-web-panel.md §13): what happened to a server,
/// a job, an update or Core, and who asked for it. Table <c>events</c>.
/// </summary>
public sealed class ActivityRecord
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary><c>instance.running</c>, <c>instance.crashed</c>, <c>job.failed</c>, <c>command</c>, <c>core.started</c>, …</summary>
    public required string Kind { get; set; }

    /// <summary><c>info</c>, <c>warning</c> or <c>error</c>.</summary>
    public required string Level { get; set; }

    public string? InstanceId { get; set; }

    public string? JobId { get; set; }

    /// <summary>Who asked: the panel user's name or <c>cli</c>; null for what Core did on its own.</summary>
    public string? User { get; set; }

    public required string Message { get; set; }
}
