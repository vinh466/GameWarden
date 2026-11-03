using GameWarden.Core.Adapters;
using GameWarden.Core.Integrations;

namespace GameWarden.Core.Tests.Unit;

/// <summary>
/// The adapter's console mappings against the lines Valheim writes (docs/17): a connection gives the platform id, the
/// character line gives the name and ZDO, and the close line takes the player offline. The same map is used at startup
/// to rebuild a running instance's roster from its log.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConsolePlayerRegistryTests
{
    private static readonly AdapterConsoleEvent[] Events =
    [
        new("joining", "Got connection SteamID (?<id>\\d+?)\\D*?$"),
        new("joining", "PlayFab socket with remote ID .*? received local Platform ID (?<id>\\w+?_\\d+?)$"),
        new("online", "Got character ZDOID from (?<name>.+?) : (?<zdo>[\\d-]+?)\\D*?:\\d+?\\D*?$"),
        new("offline", "Peer (?<key>\\d+?) has wrong password"),
        new("offline", "Closing socket (?<key>\\d+?)\\D*?$"),
        new("offline", "Destroying abandoned non persistent zdo (?<key>[\\d-]+?):.*$"),
        new("offline", "Disconnect: The client \\((?<key>\\w+?_\\d+?)\\)"),
        new("reset", "Game server connected"),
        new("reset", "Opened Steam server"),
    ];

    [Fact]
    public void A_steam_join_is_paired_with_its_character_and_taken_offline_by_id()
    {
        var registry = new ConsolePlayerRegistry();
        var map = ConsoleEventMap.Build(Events);

        Feed(registry, map, "15:01:02.123 Got connection SteamID 76561198000000001");
        Feed(registry, map, "15:01:02.456 Got character ZDOID from Vinh : -12345:1");

        var player = Assert.Single(registry.Snapshot("valheim-main"));
        Assert.Equal(("Vinh", "76561198000000001", true), (player.Name, player.Id, player.Online));

        Feed(registry, map, "15:10:00.000 Closing socket 76561198000000001");
        Assert.Empty(registry.Snapshot("valheim-main"));
    }

    [Fact]
    public void A_crossplay_join_keeps_the_platform_id_and_the_zdo_closes_it()
    {
        var registry = new ConsolePlayerRegistry();
        var map = ConsoleEventMap.Build(Events);

        Feed(registry, map, "15:01:02.123 PlayFab socket with remote ID 123456 received local Platform ID Xbox_2535416401464117");
        Feed(registry, map, "15:01:02.456 Got character ZDOID from Crossy : -4242:1");

        var player = Assert.Single(registry.Snapshot("valheim-main"));
        Assert.Equal(("Crossy", "Xbox_2535416401464117"), (player.Name, player.Id));

        Feed(registry, map, "15:10:00.000 Destroying abandoned non persistent zdo -4242:1");
        Assert.Empty(registry.Snapshot("valheim-main"));
    }

    [Fact]
    public void A_reset_line_forgets_the_previous_session()
    {
        var registry = new ConsolePlayerRegistry();
        var map = ConsoleEventMap.Build(Events);

        Feed(registry, map, "Got connection SteamID 76561198000000001");
        Feed(registry, map, "Got character ZDOID from Vinh : -12345:1");
        Assert.Single(registry.Snapshot("valheim-main"));

        Feed(registry, map, "Game server connected");
        Assert.Empty(registry.Snapshot("valheim-main"));
    }

    [Fact]
    public void Several_joiners_are_paired_earliest_first()
    {
        var registry = new ConsolePlayerRegistry();
        var map = ConsoleEventMap.Build(Events);

        Feed(registry, map, "Got connection SteamID 76561198000000001");
        Feed(registry, map, "Got connection SteamID 76561198000000002");
        Feed(registry, map, "Got character ZDOID from First : -1:1");
        Feed(registry, map, "Got character ZDOID from Second : -2:1");

        var players = registry.Snapshot("valheim-main");
        Assert.Equal(
            [("First", "76561198000000001"), ("Second", "76561198000000002")],
            players.Select(player => (player.Name, player.Id)).ToArray());
    }

    [Fact]
    public void An_invalid_pattern_is_refused_by_the_adapter_validation()
    {
        var adapter = new AdapterDefinition
        {
            SchemaVersion = 1,
            Id = "demo",
            Name = "Demo",
            Version = "1.0.0",
            Level = "L1",
            Source = new AdapterSource("manual"),
            Launch = new AdapterLaunch("{paths.server}\\demo.exe", []),
            Ports = [],
            Readiness = new AdapterReadiness("logRegex", "stdout", "ready"),
            Stop = new AdapterStop("ctrl-c"),
            Backup = new AdapterBackup(["{paths.data}"], "stop"),
            Resources = new AdapterResources(new ResourceAmounts(1, 512, 100)),
            Integration = new AdapterIntegration(
                "valheim-console",
                Transport: "console",
                Events: [new AdapterConsoleEvent("online", "Got character ZDOID from (?<name>.+?)")]),
            Folder = @"C:\adapters\demo",
        };

        var errors = AdapterCatalog.Validate(adapter);

        Assert.Contains(errors, error => error.Contains("named group 'zdo'", StringComparison.Ordinal));
    }

    private static void Feed(ConsolePlayerRegistry registry, ConsoleEventMap map, string line)
    {
        foreach (var match in map.Match(line))
        {
            registry.Apply("valheim-main", match);
        }
    }
}
