using System.Net;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Events;
using GameWarden.Core.Instances;
using GameWarden.Core.Integrations;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>
/// The console transport without a port or HTTP (docs/17): the registry supplies the player event, the provider adds
/// the admin list from <c>data/adminlist.txt</c>, and the action writes it. No Valheim or mod is needed for the path.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ValheimConsoleIntegrationTests
{
    private const string Adapter = """
        {
          "schemaVersion": 1,
          "id": "valheimconsole",
          "name": "Valheim console (fake)",
          "version": "0.1.0",
          "level": "L1",
          "source": { "type": "manual" },
          "launch": { "exe": "{settings.executable}", "args": ["--world", "{paths.data}/world.txt"] },
          "ports": [],
          "integration": {
            "provider": "valheim-console",
            "transport": "console",
            "label": { "en": "Valheim players" },
            "events": [
              { "event": "joining", "pattern": "Got connection SteamID (?<id>\\d+?)\\D*?$" },
              { "event": "online", "pattern": "Got character ZDOID from (?<name>.+?) : (?<zdo>[\\d-]+?)\\D*?:\\d+?\\D*?$" },
              { "event": "offline", "pattern": "Closing socket (?<key>\\d+?)\\D*?$" },
              { "event": "reset", "pattern": "Game server connected" }
            ]
          },
          "readiness": { "type": "logRegex", "source": "stdout", "pattern": "FakeGame server ready", "timeoutSec": 30 },
          "stop": { "method": "ctrl-c", "timeoutSec": 20 },
          "backup": { "paths": ["{paths.data}"], "mode": "stop" },
          "resources": { "min": { "cpuCores": 1, "ramMb": 64, "diskMb": 10 } }
        }
        """;

    [Fact]
    public async Task A_console_player_can_be_made_an_admin_and_removed_again()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("valheimconsole", Adapter);
        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "valheim-console-1",
                "valheimconsole",
                "Valheim console",
                new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe, ["world"] = "Midgard" }),
            TestContext.Current.CancellationToken);
        Assert.NotNull(created.Instance);
        Assert.Equal(CommandOutcome.Accepted, (await core.Instance("valheim-console-1").StartAsync()).Outcome);
        await core.WaitForAsync("valheim-console-1", snapshot => snapshot.State == ActualState.Running, "Running");
        await core.Get<ConsolePlayerMonitor>().StartAsync(TestContext.Current.CancellationToken);
        core.Get<CoreEvents>().RaiseConsoleLine(
            "valheim-console-1",
            new OutputLine(DateTimeOffset.UtcNow, OutputStream.Stdout, "15:01:02.123 Got connection SteamID 76561198000000001"));
        core.Get<CoreEvents>().RaiseConsoleLine(
            "valheim-console-1",
            new OutputLine(DateTimeOffset.UtcNow, OutputStream.Stdout, "15:01:02.456 Got character ZDOID from Vinh : -12345:1"));

        var integrations = core.Get<GameIntegrationService>();
        var read = await integrations.GetAsync("valheim-console-1", TestContext.Current.CancellationToken);
        var view = read.Value ?? throw new InvalidOperationException(read.Error);
        Assert.True(view.Online);
        Assert.Equal("valheim-console", view.Provider);
        Assert.Equal("Midgard", view.Info!.WorldName);
        Assert.Equal(1, view.Metrics!.CurrentPlayerNum);
        var player = Assert.Single(view.Players);
        Assert.Equal(("Vinh", "76561198000000001", true), (player.Name, player.UserId, player.Online));
        Assert.False(player.IsAdmin);
        var file = Path.Combine(InstancePaths.For(core.DataRoot, "valheim-console-1").Data, "adminlist.txt");
        await File.WriteAllTextAsync(file, "// keep this comment\n", TestContext.Current.CancellationToken);

        var admin = await integrations.ActAsync(
            "valheim-console-1",
            "admin",
            new GameIntegrationActionRequest(UserId: "76561198000000001"),
            TestContext.Current.CancellationToken);
        Assert.True(admin.Value!.Ok);
        Assert.Contains("restart", admin.Value.Message, StringComparison.OrdinalIgnoreCase);
        var written = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.Contains("// keep this comment", written, StringComparison.Ordinal);
        Assert.Contains("76561198000000001", written, StringComparison.Ordinal);

        var after = await integrations.GetAsync("valheim-console-1", TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(after.Value!.Players).IsAdmin);

        var removed = await integrations.ActAsync(
            "valheim-console-1",
            "unadmin",
            new GameIntegrationActionRequest(UserId: "76561198000000001"),
            TestContext.Current.CancellationToken);
        Assert.True(removed.Value!.Ok);
        Assert.DoesNotContain("76561198000000001", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));

        var invalid = await integrations.ActAsync(
            "valheim-console-1",
            "admin",
            new GameIntegrationActionRequest(UserId: "not an id"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)invalid.StatusCode);
        Assert.Equal("validation_failed", invalid.Code);
    }

    [Fact]
    public async Task A_running_instance_rebuilds_its_roster_from_the_log_after_a_core_restart()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("valheimconsole", Adapter);
        await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "valheim-console-seed",
                "valheimconsole",
                "Valheim console",
                new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe }),
            TestContext.Current.CancellationToken);
        await core.Instance("valheim-console-seed").StartAsync();
        await core.WaitForAsync("valheim-console-seed", snapshot => snapshot.State == ActualState.Running, "Running");
        var paths = InstancePaths.For(core.DataRoot, "valheim-console-seed");
        // A test-only name that sorts after the Runner's daily logs, so it does not race the live log writer.
        await File.WriteAllTextAsync(
            Path.Combine(paths.Logs, "9999-12-31.log"),
            "11:59:59.000 Game server connected\n12:00:00.000 Got connection SteamID 76561198000000009\n12:00:00.100 Got character ZDOID from SeedPlayer : -99:1\n",
            TestContext.Current.CancellationToken);

        await core.StopCoreAsync();
        await core.StartCoreAsync();
        await core.WaitForAsync("valheim-console-seed", snapshot => snapshot.State == ActualState.Running, "Running again");
        Assert.True(File.Exists(Path.Combine(paths.Logs, "9999-12-31.log")), "test log survived restart");
        // CoreHarness starts the Supervisor itself, not the web host, so start the hosted monitor explicitly.
        await core.Get<ConsolePlayerMonitor>().StartAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(Directory.GetFiles(paths.Logs, "*.log"));

        var read = await core.Get<GameIntegrationService>().GetAsync(
            "valheim-console-seed",
            TestContext.Current.CancellationToken);
        var player = Assert.Single(read.Value!.Players);
        Assert.Equal(("SeedPlayer", "76561198000000009"), (player.Name, player.UserId));
    }

    [Fact]
    public async Task A_console_instance_without_an_id_cannot_grant_admin()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("valheimconsole", Adapter);
        await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "valheim-console-2",
                "valheimconsole",
                "Valheim console",
                new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe }),
            TestContext.Current.CancellationToken);
        await core.Instance("valheim-console-2").StartAsync();
        await core.WaitForAsync("valheim-console-2", snapshot => snapshot.State == ActualState.Running, "Running");
        core.Get<ConsolePlayerRegistry>().Apply(
            "valheim-console-2",
            new ConsoleEventMatch(ConsoleEventKind.Online, Name: "NoId", Zdo: "-7"));

        var result = await core.Get<GameIntegrationService>().ActAsync(
            "valheim-console-2",
            "admin",
            new GameIntegrationActionRequest(UserId: ""),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)result.StatusCode);
        Assert.Equal("validation_failed", result.Code);
    }
}
