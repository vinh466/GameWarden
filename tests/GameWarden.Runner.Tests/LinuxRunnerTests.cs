using System.Diagnostics;
using System.Globalization;
using GameWarden.Contracts.Runner;

namespace GameWarden.Runner.Tests;

/// <summary>
/// The Runner on the Linux development platform (ADR-0016): <c>SIGINT</c> instead of CTRL+C, with an inherited
/// ignored <c>SIGINT</c> cleared; its own session; the pipe's peer from <c>SO_PEERCRED</c>. Skipped elsewhere.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LinuxRunnerTests
{
    private const int SigInt = 2;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [LinuxFact]
    public async Task Test_process_hands_down_an_ignored_sigint()
    {
        var info = RunnerHarness.WithInheritedInterruptIgnored("/bin/cat", "/proc/self/status");
        info.RedirectStandardOutput = true;
        using var cat = Process.Start(info) ?? throw new InvalidOperationException("cat did not start.");
        var status = await cat.StandardOutput.ReadToEndAsync(Ct);
        await cat.WaitForExitAsync(Ct);

        Assert.True(IgnoresSigInt(status), $"The tests could not hand down an ignored SIGINT to Runners.\n{status}");
    }

    [LinuxFact]
    public async Task Sigint_stops_the_game_gracefully_and_saves_the_world()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);
        var gamePid = (await client.GetStatusAsync(Ct)).GamePid ?? throw new InvalidOperationException("No game pid.");
        Assert.False(IgnoresSigInt(await File.ReadAllTextAsync($"/proc/{gamePid}/status", Ct)), "The game inherited an ignored SIGINT.");

        await client.SendCtrlCAsync(Ct);
        await client.WaitForLineAsync("Sent SIGINT.", Ct);
        var exited = await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);

        Assert.True(exited.ExitCode == 0, $"exit code {exited.ExitCode}\n{harness.ReadDiagnostics()}");
        Assert.True(File.Exists(harness.WorldPath), "The world was not saved.");
        Assert.Contains("ctrl-c", await File.ReadAllTextAsync(harness.WorldPath, Ct), StringComparison.Ordinal);
        Assert.Contains("Cleared the inherited SIGINT-ignore flag.", harness.ReadDiagnostics(), StringComparison.Ordinal);
    }

    [LinuxFact]
    public async Task Runner_and_game_leave_the_session_of_the_process_that_started_them()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);
        var gamePid = (await client.GetStatusAsync(Ct)).GamePid ?? throw new InvalidOperationException("No game pid.");

        Assert.Equal(harness.Runner.Id, await SessionOfAsync(harness.Runner.Id));
        Assert.Equal(harness.Runner.Id, await SessionOfAsync(gamePid));
        Assert.NotEqual(await SessionOfAsync(Environment.ProcessId), await SessionOfAsync(harness.Runner.Id));
    }

    [LinuxFact]
    public async Task Peer_credentials_name_the_runner_as_the_pipe_server()
    {
        await using var harness = await RunnerHarness.StartAsync();

        await using var client = await RunnerClient.ConnectAsync(harness.InstanceId, expectedRunnerPid: null, RunnerHarness.Timeout, Ct);

        Assert.Equal(harness.Runner.Id, client.ServerProcessId);
    }

    private static bool IgnoresSigInt(string procStatus)
    {
        var line = procStatus.Split('\n').Single(l => l.StartsWith("SigIgn:", StringComparison.Ordinal));
        var mask = ulong.Parse(line["SigIgn:".Length..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (mask & (1UL << (SigInt - 1))) != 0;
    }

    /// <summary>Field 6 of <c>/proc/&lt;pid&gt;/stat</c>; the fields after the command name, which may hold spaces, start at 3.</summary>
    private static async Task<int> SessionOfAsync(int pid)
    {
        var stat = await File.ReadAllTextAsync($"/proc/{pid}/stat", Ct);
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return int.Parse(fields[3], CultureInfo.InvariantCulture);
    }
}
