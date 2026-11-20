using System.Diagnostics;
using GameWarden.Core.Agents;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>
/// The bounded stop ladder (docs/02 §4.3, ADR-0034): a game that ignores CTRL+C is killed by the Runner, and a stop
/// that does not finish at all ends with the Runner process killed; neither may leave the instance in Stopping.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A game that ignores CTRL+C, with a one-second graceful budget so the hard kill is quick.</summary>
    private static string HangingAdapter => $$"""
        {
          "schemaVersion": 1, "id": "hanggame", "name": "Hanging FakeGame", "version": "1.0.0", "level": "L1",
          "source": { "type": "manual" },
          "launch": { "exe": "{settings.executable}", "args": ["--world", "{paths.data}/world.txt"], "workingDir": "{paths.server}" },
          "ports": [],
          "readiness": { "type": "logRegex", "source": "stdout", "pattern": "FakeGame server ready", "timeoutSec": 30 },
          "stop": { "method": "ctrl-c", "timeoutSec": 1 },
          "backup": { "paths": ["{paths.data}"], "mode": "stop" },
          "resources": { "min": { "cpuCores": 1, "ramMb": 64, "diskMb": 10 } }
        }
        """;

    [Fact]
    public async Task A_hung_game_is_killed_after_the_graceful_timeout_and_the_stop_finishes()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("hanggame", HangingAdapter);
        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest("hang-1", "hanggame", "Hang", new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe }),
            Ct);
        Assert.NotNull(created.Instance);

        var actor = core.Instance("hang-1");
        await actor.StartAsync();
        await core.WaitForAsync("hang-1", snapshot => snapshot.State == ActualState.Running, "Running");
        var runnerPid = actor.Snapshot.RunnerPid;
        Assert.NotNull(runnerPid);

        await actor.SendConsoleAsync("hang");
        await WaitForConsoleAsync(actor, "Hanging", Ct);
        var result = await actor.StopAsync();
        Assert.Equal(CommandOutcome.Accepted, result.Outcome);

        var stopped = await core.WaitForAsync("hang-1", snapshot => snapshot.State == ActualState.Stopped, "Stopped");
        Assert.Contains("did not stop", stopped.LastProblem, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("op_", stopped.OperationId, StringComparison.Ordinal);
        Assert.False(IsAlive(runnerPid.Value));
    }

    [Fact]
    public async Task Force_stop_kills_the_runner_and_finishes_without_waiting_for_the_game()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync("force-stop");
        var actor = core.Instance("force-stop");
        await actor.StartAsync();
        await core.WaitForAsync("force-stop", snapshot => snapshot.State == ActualState.Running, "Running");
        var runnerPid = actor.Snapshot.RunnerPid;
        Assert.NotNull(runnerPid);

        var result = await actor.StopAsync(force: true);
        Assert.Equal(CommandOutcome.Accepted, result.Outcome);

        var stopped = await core.WaitForAsync("force-stop", snapshot => snapshot.State == ActualState.Stopped, "Stopped");
        Assert.Contains("force-stopped", stopped.LastProblem, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("op_", stopped.OperationId, StringComparison.Ordinal);
        Assert.False(IsAlive(runnerPid.Value));

        // The actor answered the next command: a force stop must not leave the mailbox blocked.
        Assert.False(string.IsNullOrWhiteSpace((await actor.GetSettingsAsync()).Settings["executable"]));
    }

    private static async Task WaitForConsoleAsync(InstanceActor actor, string text, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var tail = await actor.TailAsync(50);
            if (tail.Any(line => line.Text.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException($"The console never contained '{text}'.");
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
