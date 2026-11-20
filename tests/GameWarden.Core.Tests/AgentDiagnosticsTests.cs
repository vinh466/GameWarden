using GameWarden.Core.Agents;
using GameWarden.Core.Instances;

namespace GameWarden.Core.Tests;

/// <summary>
/// A live stop is debugged from the logs (docs/08 §5, docs/15): the diagnostics reader must open files the Runner
/// is still writing, the instance log must be the dated console (GW lines), and a bundle must carry the Runner log.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AgentDiagnosticsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_live_log_file_can_be_read_while_the_runner_holds_it_open()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync("diag-1");
        var folder = Path.Combine(InstancePaths.For(core.DataRoot, "diag-1").Logs, "runner");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "runner.log");
        await File.WriteAllTextAsync(path, "2026-11-20 09:44:54.006 +07:00 [op_test] ctrl-c sent\n", Ct);

        await using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            await writer.WriteAsync("2026-11-20 09:45:54.067 +07:00 [op_test] kill requested\n"u8.ToArray(), Ct);
            await writer.FlushAsync(Ct);

            var content = core.Get<AgentFileService>().Read("diag-1", "logs/runner/runner.log");
            Assert.Contains("[op_test] ctrl-c sent", content.Text, StringComparison.Ordinal);
            Assert.Contains("[op_test] kill requested", content.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_instance_log_is_the_dated_console_even_when_the_game_log_is_newer()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync("diag-2");
        var logs = InstancePaths.For(core.DataRoot, "diag-2").Logs;
        Directory.CreateDirectory(logs);
        var console = Path.Combine(logs, "2026-11-20.log");
        var game = Path.Combine(logs, "valheim_server.log");
        await File.WriteAllTextAsync(console, "09:44:54.006 GW  Sent CTRL+C.\n", Ct);
        await File.WriteAllTextAsync(game, "game output\n", Ct);
        File.SetLastWriteTimeUtc(game, DateTime.UtcNow.AddMinutes(1));

        var resolved = core.Get<AgentLogService>().Resolve("instance", "diag-2");
        Assert.Equal(console, resolved);
    }

    [Fact]
    public async Task The_bundle_contains_the_runner_log_and_the_instance_system_log()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync("diag-3");
        var runner = Path.Combine(InstancePaths.For(core.DataRoot, "diag-3").RunnerState, "runner.log");
        Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
        await File.WriteAllTextAsync(runner, "2026-11-20 09:45:54.067 +07:00 [op_test] exit recorded\n", Ct);

        var principal = new AgentPrincipal(
            "agt_test", "test-agent", "tok_test", DateTimeOffset.UtcNow.AddDays(1), true,
            new HashSet<string>(StringComparer.Ordinal), true, DateTimeOffset.UtcNow.AddHours(1), false, null, false);
        var bundle = await core.Get<AgentBundleService>()
            .CreateAsync(new CreateAgentBundleRequest(["diag-3"], 1), principal, Ct);

        Assert.Contains("instances/diag-3/runner.log", bundle.Contents);
        Assert.Contains("instances/diag-3/system.log", bundle.Contents);
    }
}
