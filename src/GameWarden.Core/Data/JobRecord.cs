namespace GameWarden.Core.Data;

public enum JobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>A long operation with stages and progress (docs/03-domain-model.md §5); its log is <c>logs\jobs\&lt;id&gt;.log</c>.</summary>
public sealed class JobRecord
{
    public required string Id { get; set; }

    /// <summary><c>install</c> or <c>update</c> so far; backup, restore and others arrive with their milestones.</summary>
    public required string Type { get; set; }

    public string? InstanceId { get; set; }

    public JobState State { get; set; }

    public string? Stage { get; set; }

    /// <summary>Percent of the current stage, when the tool reports it (SteamCMD does).</summary>
    public double? Progress { get; set; }

    /// <summary>Outcome, or the reason it failed.</summary>
    public string? Message { get; set; }

    /// <summary>Runner of the tool this job runs, so a Core restart can end an orphan.</summary>
    public int? RunnerPid { get; set; }

    public DateTimeOffset? RunnerStartedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
