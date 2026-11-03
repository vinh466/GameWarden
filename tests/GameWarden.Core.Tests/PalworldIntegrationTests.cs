using System.Net;
using System.Net.Sockets;
using GameWarden.Core.Instances;
using GameWarden.Core.Integrations;
using GameWarden.Core.Secrets;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Tests;

/// <summary>
/// The generic game API path against a real (fake) REST server (docs/16): an adapter names the Palworld provider, the
/// service renders the instance's secret, the tab reads info/metrics/players and the actions reach the game.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PalworldIntegrationTests
{
    private const string Adapter = """
        {
          "schemaVersion": 1,
          "id": "pwapi",
          "name": "Palworld API (fake)",
          "version": "0.1.0",
          "level": "L1",
          "source": { "type": "manual" },
          "launch": {
            "exe": "{settings.executable}",
            "args": [
              "--world", "{paths.data}/world.txt",
              "--rest-port", "{ports.rest}",
              "--rest-password", "{secrets.AdminPassword}",
              "--rest-log", "{paths.data}/rest.log",
              "--ready-after-ms", "50"
            ]
          },
          "ports": [
            { "name": "rest", "protocol": "tcp", "default": 18222, "internal": true }
          ],
          "integration": {
            "provider": "palworld-rest",
            "port": "rest",
            "auth": { "username": "admin", "password": "{secrets.AdminPassword}" },
            "timeoutSec": 10,
            "label": { "en": "Palworld API", "vi": "Palworld API" }
          },
          "readiness": { "type": "logRegex", "source": "stdout", "pattern": "FakeGame server ready", "timeoutSec": 30 },
          "stop": { "method": "ctrl-c", "timeoutSec": 20 },
          "backup": { "paths": ["{paths.data}"], "mode": "stop" },
          "resources": { "min": { "cpuCores": 1, "ramMb": 64, "diskMb": 10 } }
        }
        """;

    [Fact]
    public async Task The_tab_reads_the_game_and_its_actions_reach_it()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.AddOwnerAdapterAsync("pwapi", Adapter);
        var rest = FreeTcp();
        var created = await core.Supervisor.CreateAsync(
            new CreateInstanceRequest(
                "pw-api",
                "pwapi",
                "Palworld API",
                new Dictionary<string, string> { ["executable"] = CoreHarness.FakeGameExe },
                new Dictionary<string, int> { ["rest"] = rest }),
            TestContext.Current.CancellationToken);
        Assert.NotNull(created.Instance);
        var paths = InstancePaths.For(core.DataRoot, "pw-api");
        Directory.CreateDirectory(paths.Server);
        var secrets = core.Get<ISecretStore>();
        secrets.Set(SecretScopes.Instance("pw-api"), "AdminPassword", "test-secret");

        Assert.Equal(CommandOutcome.Accepted, (await core.Instance("pw-api").StartAsync()).Outcome);
        await core.WaitForAsync("pw-api", snapshot => snapshot.State == ActualState.Running, "Running");

        var integrations = core.Get<GameIntegrationService>();
        var read = await integrations.GetAsync("pw-api", TestContext.Current.CancellationToken);
        var view = read.Value ?? throw new InvalidOperationException(read.Error);
        Assert.True(view.Online);
        Assert.Equal("palworld-rest", view.Provider);
        Assert.Equal("Palworld API", view.Label!["en"]);
        Assert.Contains("players", view.Capabilities);
        Assert.Equal(("fake", "FakeGame", "guid-1"),
            (view.Info!.Version, view.Info.ServerName, view.Info.WorldGuid));
        Assert.Equal((60, 1, 32, 2, 12),
            (view.Metrics!.ServerFps, view.Metrics.CurrentPlayerNum, view.Metrics.MaxPlayerNum,
                view.Metrics.BaseCampNum, view.Metrics.Days));
        var player = Assert.Single(view.Players);
        Assert.Equal(("Vinh", "steam_1", "127.0.0.1", 20, 3),
            (player.Name, player.UserId, player.Ip, player.Level, player.BuildingCount));

        // A wrong secret is the only thing that changed: the provider reports the auth failure, not a generic 502.
        secrets.Set(SecretScopes.Instance("pw-api"), "AdminPassword", "wrong");
        var refused = await integrations.GetAsync("pw-api", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, (HttpStatusCode)refused.StatusCode);
        Assert.Equal("integration_auth_failed", refused.Code);
        secrets.Set(SecretScopes.Instance("pw-api"), "AdminPassword", "test-secret");

        Assert.True((await integrations.ActAsync("pw-api", "announce",
            new GameIntegrationActionRequest(Message: "hello"), TestContext.Current.CancellationToken)).Value!.Ok);
        Assert.True((await integrations.ActAsync("pw-api", "save",
            new GameIntegrationActionRequest(), TestContext.Current.CancellationToken)).Value!.Ok);
        Assert.True((await integrations.ActAsync("pw-api", "kick",
            new GameIntegrationActionRequest(Message: "bye", UserId: "steam_1"), TestContext.Current.CancellationToken)).Value!.Ok);
        Assert.True((await integrations.ActAsync("pw-api", "ban",
            new GameIntegrationActionRequest(Message: "bye", UserId: "steam_1"), TestContext.Current.CancellationToken)).Value!.Ok);
        Assert.True((await integrations.ActAsync("pw-api", "unban",
            new GameIntegrationActionRequest(UserId: "steam_1"), TestContext.Current.CancellationToken)).Value!.Ok);

        var log = await File.ReadAllTextAsync(Path.Combine(paths.Data, "rest.log"), TestContext.Current.CancellationToken);
        Assert.Contains("rest-announce {\"message\":\"hello\"}", log, StringComparison.Ordinal);
        Assert.Contains("rest-kick {\"userid\":\"steam_1\",\"message\":\"bye\"}", log, StringComparison.Ordinal);
        Assert.Contains("rest-ban {\"userid\":\"steam_1\",\"message\":\"bye\"}", log, StringComparison.Ordinal);
        Assert.Contains("rest-unban {\"userid\":\"steam_1\"}", log, StringComparison.Ordinal);
        Assert.Contains("rest-save", await File.ReadAllTextAsync(core.WorldFile("pw-api"), TestContext.Current.CancellationToken), StringComparison.Ordinal);

        await core.Instance("pw-api").StopAsync();
        await core.WaitForAsync("pw-api", snapshot => snapshot.State == ActualState.Stopped, "Stopped");
        var offline = await integrations.GetAsync("pw-api", TestContext.Current.CancellationToken);
        Assert.False(offline.Value!.Online);
        Assert.Empty(offline.Value.Players);
    }

    [Fact]
    public async Task An_instance_without_an_integration_says_so()
    {
        await using var core = await CoreHarness.StartAsync();
        await core.CreateFakeGameAsync("no-api");

        var result = await core.Get<GameIntegrationService>().GetAsync("no-api", TestContext.Current.CancellationToken);

        Assert.Null(result.Value);
        Assert.Equal(HttpStatusCode.Conflict, (HttpStatusCode)result.StatusCode);
        Assert.Equal("integration_not_supported", result.Code);
    }

    private static int FreeTcp()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
