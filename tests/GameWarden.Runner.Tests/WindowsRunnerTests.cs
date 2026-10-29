using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using GameWarden.Contracts.Runner;

namespace GameWarden.Runner.Tests;

/// <summary>
/// The Runner's Windows adapter (ADR-0016, tier <c>Windows</c>): CTRL+C with the inherited ignore flag cleared
/// (spike S1, 02 §4.3), the kill-on-close Job Object (02 §4.2) and the pipe ACL.
/// </summary>
[Trait("Category", "Windows")]
public sealed class WindowsRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [WindowsFact]
    public void Test_process_hands_down_the_ignore_flag()
    {
        Assert.True(RunnerHarness.InheritedIgnoreFlagSet, "The tests could not set the CTRL+C-ignore flag they hand down to Runners.");
    }

    [WindowsFact]
    public async Task Ctrl_c_stops_the_game_gracefully_and_saves_the_world()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);

        await client.SendCtrlCAsync(Ct);
        var exited = await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);

        Assert.True(exited.ExitCode == 0, $"exit code {exited.ExitCode}\n{harness.ReadDiagnostics()}");
        Assert.True(File.Exists(harness.WorldPath), "The world was not saved.");
        Assert.Contains("ctrl-c", await File.ReadAllTextAsync(harness.WorldPath, Ct), StringComparison.Ordinal);
    }

    [WindowsFact]
    public async Task Killing_the_runner_kills_the_game()
    {
        await using var harness = await RunnerHarness.StartAsync();
        int gamePid;
        await using (var client = await harness.ConnectAsync(Ct))
        {
            await client.WaitForLineAsync("FakeGame server ready", Ct);
            gamePid = (await client.GetStatusAsync(Ct)).GamePid ?? throw new InvalidOperationException("No game pid.");
        }

        using var game = Process.GetProcessById(gamePid);
        harness.Runner.Kill(entireProcessTree: false);

        await game.WaitForExitAsync(new CancellationTokenSource(RunnerHarness.Timeout).Token);
        Assert.True(game.HasExited);
    }

    [WindowsFact]
    public async Task A_launcher_counts_as_running_until_its_child_exits()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);

        await client.WriteStdinAsync("spawn-child", Ct);
        await client.WaitForLineAsync("FakeGame child running", Ct);
        await client.WaitForLineAsync("waiting for the processes it started", Ct);
        Assert.True((await client.GetStatusAsync(Ct)).GameRunning, "The child still runs, so the game does.");

        await client.SendCtrlCAsync(Ct); // reaches the child: it shares the Runner's console
        var exited = await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);
        Assert.Equal(0, exited.ExitCode);
    }

    [WindowsFact]
    public async Task Kill_also_ends_children_whose_parent_already_exited()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using var client = await harness.ConnectAsync(Ct);
        await client.WaitForLineAsync("FakeGame server ready", Ct);
        await client.WriteStdinAsync("spawn-child", Ct);
        await client.WaitForLineAsync("waiting for the processes it started", Ct);

        await client.KillAsync(Ct);

        await client.WaitForExitAsync(RunnerHarness.Timeout, Ct);
        Assert.False((await client.GetStatusAsync(Ct)).GameRunning);
    }

    [WindowsFact]
    public async Task Pipe_admits_only_system_administrators_and_the_runners_user()
    {
        await using var harness = await RunnerHarness.StartAsync();
        await using (var client = await harness.ConnectAsync(Ct))
        {
            await client.WaitForLineAsync("FakeGame server ready", Ct);
        }

        // A bare client only reads the ACL; the Runner ends a session that never says hello.
        await using var pipe = new NamedPipeClientStream(".", RunnerPipe.NameFor(harness.InstanceId), PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(RunnerHarness.Timeout, Ct);
        var rules = pipe.GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

        SecurityIdentifier[] expected =
        [
            WindowsIdentity.GetCurrent().User!,
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        ];
        Assert.Equal(
            expected.Select(sid => sid.Value).Order(StringComparer.Ordinal),
            rules.Select(rule => rule.IdentityReference.Value).Distinct().Order(StringComparer.Ordinal));
        Assert.All(rules, rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
    }
}
