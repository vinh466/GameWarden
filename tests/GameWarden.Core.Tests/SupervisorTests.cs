using System.Diagnostics;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>Supervisor + real Runner + FakeGame, as the current user (no admin). 02 §4–5, 03 §3–4.</summary>
[Trait("Category", "Integration")]
public sealed class SupervisorTests
{
    [Fact]
    public async Task Start_runs_the_game_until_it_is_ready()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();

        var result = await core.Instance("fake-1").StartAsync();
        var running = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");

        Assert.Equal(CommandOutcome.Accepted, result.Outcome);
        Assert.Equal(DesiredState.Running, running.DesiredState);
        Assert.NotNull(running.RunnerPid);
        Assert.NotNull(running.GamePid);
    }

    [Fact]
    public async Task Stop_saves_the_world_with_ctrl_c()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();
        await core.Instance("fake-1").StartAsync();
        await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");

        await core.Instance("fake-1").StopAsync();
        var stopped = await core.WaitForAsync("fake-1", s => s.State == ActualState.Stopped, "Stopped");

        Assert.Null(stopped.RunnerPid);
        Assert.Contains("ctrl-c", await File.ReadAllTextAsync(core.WorldFile("fake-1"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crash_restarts_the_game_after_the_back_off()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();
        await core.Instance("fake-1").StartAsync();
        var first = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");

        await core.Instance("fake-1").SendConsoleAsync("crash");
        var again = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running && s.RunnerPid != first.RunnerPid, "Running again");

        Assert.Equal(1, again.RecentRestarts);
    }

    [Fact]
    public async Task Too_many_crashes_fault_the_instance_and_it_stays_faulted_after_a_core_restart()
    {
        var policy = RestartPolicy.Default with { Backoff = [TimeSpan.FromMilliseconds(200)], MaxRestarts = 1 };
        await using var core = await CoreHarness.StartAsync(policy);
        await core.CreateFakeGameAsync();
        await core.Instance("fake-1").StartAsync();
        var first = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");
        await core.Instance("fake-1").SendConsoleAsync("crash");
        await core.WaitForAsync("fake-1", s => s.State == ActualState.Running && s.RunnerPid != first.RunnerPid, "Running again");

        await core.Instance("fake-1").SendConsoleAsync("crash");
        await core.WaitForAsync("fake-1", s => s.State == ActualState.Faulted, "Faulted");

        await core.StopCoreAsync();
        await core.StartCoreAsync();
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        var afterRestart = core.Instance("fake-1").Snapshot;
        Assert.Equal(ActualState.Faulted, afterRestart.State);
        Assert.Null(afterRestart.RunnerPid);
    }

    [Fact]
    public async Task Windows_shutdown_saves_the_world_and_the_game_comes_back_after_boot()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();
        await core.Instance("fake-1").StartAsync();
        var before = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");

        core.Get<Hosting.ShutdownSignal>().OnSystemShutdown(); // what PRESHUTDOWN does (ADR-0014)
        await core.StopCoreAsync();

        Assert.True(HasExited(before.GamePid!.Value), "The game must stop when Windows shuts down.");
        Assert.Contains("ctrl-c", await File.ReadAllTextAsync(core.WorldFile("fake-1"), TestContext.Current.CancellationToken), StringComparison.Ordinal);

        await core.StartCoreAsync(); // "after boot"
        var after = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running after boot");
        Assert.Equal(DesiredState.Running, after.DesiredState);
        Assert.NotEqual(before.RunnerPid, after.RunnerPid);
    }

    [Fact]
    public async Task Core_restart_reattaches_to_the_running_game()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();
        await core.Instance("fake-1").StartAsync();
        var before = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");

        await core.StopCoreAsync();
        Assert.False(Process.GetProcessById(before.RunnerPid!.Value).HasExited, "The game must survive a Core stop.");
        await core.StartCoreAsync();

        var after = core.Instance("fake-1").Snapshot;
        Assert.Equal(ActualState.Running, after.State);
        Assert.Equal(before.RunnerPid, after.RunnerPid);

        await core.Instance("fake-1").StopAsync();
        await core.WaitForAsync("fake-1", s => s.State == ActualState.Stopped, "Stopped");
        Assert.True(File.Exists(core.WorldFile("fake-1")));
    }

    [Fact]
    public async Task A_game_that_died_while_core_was_away_is_restarted()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();
        await core.Instance("fake-1").StartAsync();
        var before = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running, "Running");

        await core.StopCoreAsync();
        using (var runner = Process.GetProcessById(before.RunnerPid!.Value))
        {
            // On Windows its Job Object takes the game down with it; Linux has no Job Object (ADR-0016), so take the tree.
            runner.Kill(entireProcessTree: !OperatingSystem.IsWindows());
            await runner.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        await core.StartCoreAsync();
        var after = await core.WaitForAsync("fake-1", s => s.State == ActualState.Running && s.RunnerPid != before.RunnerPid, "Running again");
        Assert.Equal(1, after.RecentRestarts);
    }

    [Fact]
    public async Task A_game_that_never_gets_ready_is_killed_and_faulted()
    {
        var policy = RestartPolicy.Default with { MaxRestarts = 0 };
        await using var core = await CoreHarness.StartAsync(policy);
        var adapterFolder = Path.Combine(core.DataRoot, "adapters", "slowgame");
        Directory.CreateDirectory(adapterFolder);
        var adapterJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "adapters-builtin", "fakegame", "adapter.json"), TestContext.Current.CancellationToken);
        adapterJson = adapterJson
            .Replace("\"id\": \"fakegame\"", "\"id\": \"slowgame\"", StringComparison.Ordinal)
            .Replace("\"FakeGame server ready\", \"timeoutSec\": 60", "\"never printed\", \"timeoutSec\": 2", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(adapterFolder, "adapter.json"), adapterJson, TestContext.Current.CancellationToken);
        await core.StopCoreAsync();
        await core.StartCoreAsync(); // loads the Owner's adapter

        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest("slow-1", "slowgame", Settings: new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe }),
            TestContext.Current.CancellationToken);
        Assert.Null(created.Error);
        await core.Instance("slow-1").StartAsync();

        var faulted = await core.WaitForAsync("slow-1", s => s.State == ActualState.Faulted, "Faulted");
        Assert.NotNull(faulted.LastProblem);
        Assert.Null(faulted.RunnerPid);
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
    }
}
