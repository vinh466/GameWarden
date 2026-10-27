using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Jobs;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>Install and update jobs with FakeGame standing in for SteamCMD (03 §5), as the current user.</summary>
[Trait("Category", "Windows")]
public sealed class JobTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A game "installed" by SteamCMD: FakeGame copies itself into the server folder.</summary>
    private static string FakeSteamAdapter(int appId) => $$"""
        {
          "schemaVersion": 1, "id": "fakesteam", "name": "FakeGame via SteamCMD", "version": "1.0.0", "level": "L1",
          "source": { "type": "steamcmd", "appId": {{appId}} },
          "launch": { "exe": "{paths.server}\\FakeGame.exe", "args": ["--world", "{paths.data}\\world.txt"] },
          "ports": [],
          "readiness": { "type": "logRegex", "source": "stdout", "pattern": "FakeGame server ready", "timeoutSec": 60 },
          "stop": { "method": "ctrl-c", "timeoutSec": 20 },
          "backup": { "paths": ["{paths.data}"], "mode": "stop" },
          "resources": { "min": { "cpuCores": 1, "ramMb": 64, "diskMb": 10 } }
        }
        """;

    [Fact]
    public async Task Install_puts_the_game_in_place_and_the_instance_can_start()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("fakesteam", FakeSteamAdapter(896660));
        await core.Supervisor.CreateAsync(new CreateInstanceRequest("steam-1", "fakesteam"), Ct);
        Assert.Equal(ActualState.NotInstalled, core.Instance("steam-1").Snapshot.State);

        var job = await RunJobAsync(core, "steam-1", update: false);

        Assert.True(job.State == JobState.Succeeded, $"{job.State}: {job.Message}\n{string.Join("\n", JobLog.Tail(Path.Combine(core.DataRoot, "logs", "jobs"), job.Id, 100))}");
        Assert.Equal(100, job.Progress);
        Assert.True(File.Exists(Path.Combine(core.Instance("steam-1").ServerFolder, "FakeGame.exe")));
        Assert.Equal(ActualState.Stopped, core.Instance("steam-1").Snapshot.State);
        var log = JobLog.Tail(Path.Combine(core.DataRoot, "logs", "jobs"), job.Id, 500);
        Assert.Contains(log, line => line.Contains("progress: 50.25", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("Success! App '896660' fully installed.", StringComparison.Ordinal));

        await core.Instance("steam-1").StartAsync();
        await core.WaitForAsync("steam-1", s => s.State == ActualState.Running, "Running");
        await core.Instance("steam-1").StopAsync();
        await core.WaitForAsync("steam-1", s => s.State == ActualState.Stopped, "Stopped");
    }

    [Fact]
    public async Task Updating_a_running_game_stops_it_updates_and_starts_it_again()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("fakesteam", FakeSteamAdapter(896660));
        await core.Supervisor.CreateAsync(new CreateInstanceRequest("steam-1", "fakesteam"), Ct);
        await RunJobAsync(core, "steam-1", update: false);
        await core.Instance("steam-1").StartAsync();
        var before = await core.WaitForAsync("steam-1", s => s.State == ActualState.Running, "Running");

        var job = await RunJobAsync(core, "steam-1", update: true);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Contains("ctrl-c", await File.ReadAllTextAsync(core.WorldFile("steam-1"), Ct), StringComparison.Ordinal); // stopped gracefully
        var after = await core.WaitForAsync("steam-1", s => s.State == ActualState.Running, "Running after the update");
        Assert.NotEqual(before.RunnerPid, after.RunnerPid);
        Assert.Null(after.ActiveJobId);
        await core.Instance("steam-1").StopAsync();
        await core.WaitForAsync("steam-1", s => s.State == ActualState.Stopped, "Stopped");
    }

    [Fact]
    public async Task A_failing_install_is_tried_three_times_and_reported()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("fakesteam", FakeSteamAdapter(404));
        await core.Supervisor.CreateAsync(new CreateInstanceRequest("steam-1", "fakesteam"), Ct);

        var job = await RunJobAsync(core, "steam-1", update: false);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("No connection", job.Message, StringComparison.Ordinal);
        var log = JobLog.Tail(Path.Combine(core.DataRoot, "logs", "jobs"), job.Id, 500);
        Assert.Contains(log, line => line.Contains("try 3 of 3", StringComparison.Ordinal));
        var instance = core.Instance("steam-1").Snapshot;
        Assert.Equal(ActualState.NotInstalled, instance.State);
        Assert.Null(instance.ActiveJobId);
        Assert.Contains("No connection", instance.LastProblem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_job_holds_the_instance_lock()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync();
        var actor = core.Instance("fake-1");

        var locked = await actor.BeginJobAsync("job-a");
        var second = await actor.BeginJobAsync("job-b");
        var start = await actor.StartAsync();

        Assert.Equal(CommandOutcome.Accepted, locked.Outcome);
        Assert.Equal(ProblemCodes.InstanceBusy, second.Code);
        Assert.Equal(ProblemCodes.InstanceBusy, start.Code);
        Assert.Equal("job-a", actor.Snapshot.ActiveJobId);

        await actor.EndJobAsync("job-a", installed: false, startIfDesired: false);
        Assert.Null(actor.Snapshot.ActiveJobId);
    }

    private static async Task<JobRecord> RunJobAsync(CoreHarness core, string instanceId, bool update)
    {
        var jobs = core.Get<JobService>();
        var started = await jobs.StartInstallAsync(instanceId, update, force: false, Ct);
        Assert.True(started.Job is not null, started.Error);
        await jobs.WaitAsync(started.Job.Id).WaitAsync(CoreHarness.Timeout, Ct);
        var job = await jobs.FindAsync(started.Job.Id, Ct) ?? throw new InvalidOperationException("Job vanished.");
        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, JobLog.Tail(Path.Combine(core.DataRoot, "logs", "jobs"), job.Id, 200)));
        return job;
    }
}
