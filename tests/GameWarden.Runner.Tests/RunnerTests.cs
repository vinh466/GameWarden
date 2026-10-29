using GameWarden.Contracts.Runner;

namespace GameWarden.Runner.Tests;

/// <summary>
/// The Runner's portable behaviour with FakeGame as a real process (ADR-0016, tier <c>Integration</c>): the same
/// tests pass on Windows with the real adapters and on Linux with the development platform.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_that_connects_at_once_is_welcomed_with_the_game_pid()
    {
        // Core connects as soon as the pipe exists, which can be before the Runner has started the game; Core keeps
        // the game's PID from the welcome (metrics, the shutdown check), so the welcome must wait for the start.
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);

        Assert.NotNull(client.Welcome.Status.GamePid);
        Assert.True(client.Welcome.Status.GameRunning);
    }

    [Fact]
    public async Task Stdin_command_stops_the_game()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);

        await client.WriteStdinAsync("exit", Ct);
        var exited = await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);

        Assert.Equal(0, exited.ExitCode);
        Assert.Contains("exit", await File.ReadAllTextAsync(harness.WorldPath, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kill_ends_a_game_that_ignores_ctrl_c()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);
        await client.WriteStdinAsync("hang", Ct);
        await client.WaitForLineAsync("Hanging", Ct);

        await client.SendCtrlCAsync(Ct);
        await client.WaitForLineAsync("CTRL+C ignored", Ct);
        Assert.True((await client.GetStatusAsync(Ct)).GameRunning);

        await client.KillAsync(Ct);
        var exited = await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);

        Assert.NotEqual(0, exited.ExitCode);
        Assert.False(File.Exists(harness.WorldPath));
    }

    [Fact]
    public async Task Crash_is_reported_with_its_exit_code_and_recorded_on_disk()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);

        await client.WriteStdinAsync("crash", Ct);
        var exited = await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);

        Assert.Equal(3, exited.ExitCode);
        var record = await File.ReadAllTextAsync(Path.Combine(harness.StateDirectory, RunnerExitRecord.FileName), Ct);
        Assert.Contains("\"exitCode\":3", record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_quits_once_core_acknowledges_the_exit()
    {
        await using var harness = await RunnerHarness.StartAsync(lingerSeconds: 600);
        await using (var client = await harness.ConnectAsync(Ct))
        {
            await client.WaitForLineAsync("FakeGame server ready", Ct);
            await client.WriteStdinAsync("exit", Ct);
            await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);
            await client.AckExitAsync(Ct);
        }

        await harness.Runner.WaitForExitAsync(new CancellationTokenSource(RunnerHarness.Timeout).Token);
        Assert.Equal(0, harness.Runner.ExitCode);
    }

    [Fact]
    public async Task Reconnecting_client_gets_status_and_tail()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using (var first = await harness.ConnectAsync(Ct))
        {
            await first.WaitForLineAsync("FakeGame server ready", Ct);
            await first.WriteStdinAsync("echo marker-before-reconnect", Ct);
            await first.WaitForLineAsync("marker-before-reconnect", Ct);
        }

        await using var second = await harness.ConnectAsync(Ct);

        Assert.True(second.Welcome.Status.GameRunning);
        Assert.Equal(harness.Runner.Id, second.Welcome.Status.RunnerPid);
        var tail = await second.GetTailAsync(50, Ct);
        Assert.Contains(tail, line => line.Text == "marker-before-reconnect");
        await second.WriteStdinAsync("exit", Ct);
        await second.WaitForExitAsync(RunnerHarness.Timeout, Ct);
    }

    [Fact]
    public async Task Client_refuses_a_pipe_served_by_another_process()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using (var client = await harness.ConnectAsync(Ct))
        {
            await client.WaitForLineAsync("FakeGame server ready", Ct);
        }

        var error = await Assert.ThrowsAsync<RunnerIdentityException>(
            () => RunnerClient.ConnectAsync(harness.InstanceId, expectedRunnerPid: harness.Runner.Id + 1, RunnerHarness.Timeout, Ct));
        Assert.Equal(harness.Runner.Id, error.ActualPid);
    }

    [Fact]
    public async Task A_console_written_to_a_file_is_relayed_as_stdout()
    {
        await using var harness = await RunnerHarness.StartAsync(consoleToFile: true);
        await using var client = await harness.ConnectAsync(Ct);

        await client.WaitForLineAsync("FakeGame server ready", Ct);
        await client.WriteStdinAsync("echo from-the-log-file", Ct);
        await client.WaitForLineAsync("from-the-log-file", Ct);

        await client.SendCtrlCAsync(Ct);
        await client.WaitForLineAsync("World saved (ctrl-c)", Ct); // written just before the exit, still relayed
        var tail = await client.GetTailAsync(100, Ct);
        Assert.Contains(tail, line => line.Text == "FakeGame server ready" && line.Stream == OutputStream.Stdout);
    }

    [Fact]
    public async Task Output_is_written_to_the_daily_log()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);
        await client.WriteStdinAsync("echo log-marker", Ct);
        await client.WaitForLineAsync("log-marker", Ct);

        var logFile = Assert.Single(Directory.GetFiles(harness.LogDirectory, "*.log"));
        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        Assert.Contains("log-marker", await reader.ReadToEndAsync(Ct), StringComparison.Ordinal);
    }
}
