using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Activity;

public sealed record ActivityEntry(string Kind, string Level, string Message);

/// <summary>
/// Which events become activity lines (docs/03-domain-model.md §6): the moments an Owner cares about, not every
/// internal step. A server going Stopping → Stopped is "Stopped"; Starting is not news until it is Running.
/// </summary>
public static class ActivityRules
{
    public static ActivityEntry? ForInstance(InstanceSnapshot? before, InstanceSnapshot after)
    {
        if (before is null)
        {
            return new("instance.created", "info", $"Created {after.DisplayName}.");
        }

        if (before.State == after.State)
        {
            return null;
        }

        return after.State switch
        {
            ActualState.Running => new("instance.running", "info", before.State == ActualState.Starting && before.RecentRestarts > 0 ? "Running again after a crash." : "Running."),
            ActualState.Stopped when before.State == ActualState.NotInstalled => new("instance.installed", "info", "Installed."),
            ActualState.Stopped => new("instance.stopped", "info", "Stopped."),
            ActualState.Crashed => new("instance.crashed", "error", $"Crashed: {after.LastProblem ?? "the game exited"}."),
            ActualState.Faulted => new("instance.faulted", "error", $"Not restarted any more: {after.LastProblem ?? "it kept crashing"}."),
            _ => null,
        };
    }

    public static ActivityEntry? ForJob(JobRecord job) => job.State switch
    {
        JobState.Succeeded => new("job.succeeded", "info", $"{Title(job.Type)}: {job.Message ?? "done."}"),
        JobState.Failed => new("job.failed", "error", $"{Title(job.Type)}: {job.Message ?? "failed."}"),
        _ => null,
    };

    private static string Title(string type) => type switch
    {
        "install" => "Install",
        "update" => "Game update",
        "mods.sync" => "Mods",
        _ => type,
    };
}
