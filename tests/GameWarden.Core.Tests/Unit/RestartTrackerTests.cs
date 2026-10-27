using GameWarden.Core.Instances;

namespace GameWarden.Core.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class RestartTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Back_off_follows_the_policy_then_faults()
    {
        var tracker = new RestartTracker(RestartPolicy.Default);
        var delays = Enumerable.Range(0, 5).Select(i => tracker.OnCrash(T0.AddSeconds(i), runningSince: null)).ToArray();

        Assert.Equal([10, 30, 60, 120, 300], delays.Select(delay => (int)delay!.Value.TotalSeconds));
        Assert.Null(tracker.OnCrash(T0.AddSeconds(10), runningSince: null));
    }

    [Fact]
    public void Crashes_outside_the_window_no_longer_count()
    {
        var policy = RestartPolicy.Default with { MaxRestarts = 2 };
        var tracker = new RestartTracker(policy);
        tracker.OnCrash(T0, null);
        tracker.OnCrash(T0.AddMinutes(1), null);

        Assert.Null(tracker.OnCrash(T0.AddMinutes(2), null));
        Assert.NotNull(tracker.OnCrash(T0.AddMinutes(32), null));
    }

    [Fact]
    public void Running_long_enough_resets_the_counter()
    {
        var tracker = new RestartTracker(RestartPolicy.Default with { MaxRestarts = 1 });
        tracker.OnCrash(T0, null);

        var delay = tracker.OnCrash(T0.AddMinutes(31), runningSince: T0.AddMinutes(1));

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }
}
