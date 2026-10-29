using GameWarden.Core.Activity;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ActivityRulesTests
{
    private static InstanceSnapshot Server(ActualState state, int restarts = 0, string? problem = null) =>
        new("valheim-main", "Vikings", "valheim", DesiredState.Running, state, null, null, null, restarts, problem, new Dictionary<string, int>());

    [Fact]
    public void Creating_and_the_moments_the_owner_cares_about_become_lines()
    {
        Assert.Equal("instance.created", ActivityRules.ForInstance(null, Server(ActualState.NotInstalled))?.Kind);
        Assert.Equal("instance.installed", ActivityRules.ForInstance(Server(ActualState.NotInstalled), Server(ActualState.Stopped))?.Kind);
        Assert.Equal("instance.running", ActivityRules.ForInstance(Server(ActualState.Starting), Server(ActualState.Running))?.Kind);
        Assert.Equal("instance.stopped", ActivityRules.ForInstance(Server(ActualState.Stopping), Server(ActualState.Stopped))?.Kind);

        var crashed = ActivityRules.ForInstance(Server(ActualState.Running), Server(ActualState.Crashed, problem: "exited with code 3"));
        Assert.Equal(("instance.crashed", "error"), (crashed?.Kind, crashed?.Level));
        Assert.Contains("exited with code 3", crashed!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Steps_in_between_are_not_news()
    {
        Assert.Null(ActivityRules.ForInstance(Server(ActualState.Stopped), Server(ActualState.Starting)));
        Assert.Null(ActivityRules.ForInstance(Server(ActualState.Running), Server(ActualState.Stopping)));
        Assert.Null(ActivityRules.ForInstance(Server(ActualState.Running), Server(ActualState.Running, problem: "changed")));
    }

    [Fact]
    public void Running_again_after_a_crash_says_so()
    {
        var entry = ActivityRules.ForInstance(Server(ActualState.Starting, restarts: 1), Server(ActualState.Running, restarts: 1));

        Assert.Equal("Running again after a crash.", entry?.Message);
    }

    [Fact]
    public void Only_finished_jobs_count()
    {
        var job = new JobRecord { Id = "j1", Type = "update", InstanceId = "valheim-main", State = JobState.Running };

        Assert.Null(ActivityRules.ForJob(job));
        job.State = JobState.Failed;
        job.Message = "update failed: no connection";
        Assert.Equal(("job.failed", "error", "Game update: update failed: no connection"), (ActivityRules.ForJob(job)?.Kind, ActivityRules.ForJob(job)?.Level, ActivityRules.ForJob(job)?.Message));
    }
}
