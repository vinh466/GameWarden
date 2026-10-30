using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameWarden.Core.Instances;
using GameWarden.Core.Mods;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Tests;

/// <summary>
/// What the panel's Mods tab needs (docs/13 §6, phase 2): Thunderstore search, the restart flag of <c>GET /mods</c>,
/// the loader's log and Thunderstore's state of each package after an update check.
/// </summary>
public sealed partial class ModsApiTests
{
    [Fact]
    public async Task Search_answers_from_the_communitys_cached_list_and_checks_its_input()
    {
        factory.Thunderstore.PackageLists["valheim"] =
        [
            FakeThunderstore.ListedPackage("RandyKnapp-EpicLoot", "Loot drops with magic effects.", [1000, 5000]),
            FakeThunderstore.ListedPackage("Someone-EpicLootAddon", "Adds sets to Epic Loot.", [700]),
            FakeThunderstore.ListedPackage("Old-EpicLootFork", "A fork.", [9000], deprecated: true),
            FakeThunderstore.ListedPackage("Lewd-EpicLootSkins", "Skins.", [90000], nsfw: true),
        ];
        using var client = await ClientWithInstanceAsync("mods-25");

        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-25/mods/search?q=epic%20loot", Ct);

        Assert.Equal("valheim", result.GetProperty("community").GetString());
        Assert.True(result.GetProperty("fetchedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        var hits = result.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(["RandyKnapp-EpicLoot", "Someone-EpicLootAddon", "Old-EpicLootFork"], hits.Select(hit => hit.GetProperty("package").GetString()));
        var epicLoot = hits[0];
        Assert.Equal("1.0.1", epicLoot.GetProperty("version").GetString());
        Assert.Equal("Loot drops with magic effects.", epicLoot.GetProperty("description").GetString());
        Assert.Equal(6000, epicLoot.GetProperty("downloads").GetInt64());
        Assert.Equal("https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/", epicLoot.GetProperty("url").GetString());
        Assert.True(hits[2].GetProperty("deprecated").GetBoolean());
        var limited = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-25/mods/search?q=loot&limit=1", Ct);
        Assert.Single(limited.GetProperty("results").EnumerateArray());
        Assert.Equal(1, factory.Thunderstore.Count("/c/valheim/api/v1/package/")); // the second search used the cache

        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-25/mods/search?q=%20", Ct), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-25/mods/search?q=loot&limit=51", Ct), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await client.GetAsync($"/api/v1/instances/mods-25/mods/search?q={new string('a', 101)}", Ct), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/nope/mods/search?q=loot", Ct), HttpStatusCode.NotFound, "not_found");
        var created = await client.PostAsJsonAsync("/api/v1/instances", new { id = "mods-vanilla-3", adapterId = "fakegame" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-vanilla-3/mods/search?q=loot", Ct), HttpStatusCode.Conflict, "invalid_state");
        var anonymous = await factory.CreateClient().GetAsync("/api/v1/instances/mods-25/mods/search?q=loot", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Restart_is_required_while_the_server_runs_something_other_than_its_next_start_would_load()
    {
        using var client = await ClientWithInstanceAsync("mods-26", BepInExAdapter);
        await using var stops = StopsAtTheEnd(client, "mods-26");
        async Task<JsonElement> ModsAsync() => await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-26/mods", Ct);

        await SyncAsync(client, "mods-26", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var first = (await ModsAsync()).GetProperty("active").GetProperty("id").GetInt64();
        Assert.Equal("valheim", (await ModsAsync()).GetProperty("community").GetString());
        await SetModsAsync(client, "mods-26", true);
        Assert.False((await ModsAsync()).GetProperty("restartRequired").GetBoolean()); // stopped: the next start loads it anyway

        await StartAsync(client, "mods-26");
        var mods = await ModsAsync();
        Assert.Equal(first, mods.GetProperty("runningSetId").GetInt64());
        Assert.False(mods.GetProperty("restartRequired").GetBoolean());

        // A new active set while it runs, then back to the running one.
        await SyncAsync(client, "mods-26", new { package = "ValheimModding-Jotunn", version = "2.30.2" });
        Assert.True((await ModsAsync()).GetProperty("restartRequired").GetBoolean());
        await client.PostAsync("/api/v1/instances/mods-26/mods/rollback", null, Ct);
        Assert.False((await ModsAsync()).GetProperty("restartRequired").GetBoolean());

        // Mods off while it runs modded; a vanilla start; Mods on again while it runs vanilla.
        Assert.True((await SetModsAsync(client, "mods-26", false)).GetProperty("restartRequired").GetBoolean());
        await StopAsync(client, "mods-26");
        Assert.False((await ModsAsync()).GetProperty("restartRequired").GetBoolean());
        await StartAsync(client, "mods-26");
        Assert.False((await ModsAsync()).GetProperty("restartRequired").GetBoolean());
        await using (var db = await Db())
        {
            var row = await db.InstanceMods.AsNoTracking().SingleAsync(row => row.InstanceId == "mods-26", Ct);
            Assert.False(row.LastStartModsOn);
            Assert.Equal(first, row.RunningSetId);
        }

        Assert.True((await SetModsAsync(client, "mods-26", true)).GetProperty("restartRequired").GetBoolean());
        await StopAsync(client, "mods-26");
        Assert.False((await ModsAsync()).GetProperty("restartRequired").GetBoolean());
    }

    [Fact]
    public async Task The_loader_log_is_the_tail_of_the_running_or_else_the_active_sets_log()
    {
        using var client = await ClientWithInstanceAsync("mods-27");
        var none = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-27/mods/log", Ct);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("setId").ValueKind);
        Assert.Empty(none.GetProperty("lines").EnumerateArray());

        await SyncAsync(client, "mods-27", new { package = "ValheimModding-JsonDotNET" });
        await SyncAsync(client, "mods-27", new { package = "ValheimModding-Jotunn" });
        var mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-27/mods", Ct);
        var active = mods.GetProperty("active").GetProperty("id").GetInt64();
        var previous = mods.GetProperty("previous").GetProperty("id").GetInt64();
        var noLog = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-27/mods/log", Ct);
        Assert.Equal(active, noLog.GetProperty("setId").GetInt64());
        Assert.Empty(noLog.GetProperty("lines").EnumerateArray());

        var paths = InstancePaths.For(factory.DataRoot, "mods-27");
        string Log(long set) => Path.Combine(paths.Mods, "sets", set.ToString(CultureInfo.InvariantCulture), "logs", "loader.log");
        Directory.CreateDirectory(Path.GetDirectoryName(Log(active))!);
        await File.WriteAllLinesAsync(Log(active), Enumerable.Range(1, 300).Select(line => $"[Info   :   BepInEx] line {line}"), Ct);
        var tail = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-27/mods/log", Ct);
        var lines = tail.GetProperty("lines").EnumerateArray().Select(line => line.GetString()).ToList();
        Assert.Equal(ModsService.LogDefaultTail, lines.Count);
        Assert.Equal("[Info   :   BepInEx] line 300", lines[^1]);
        var five = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-27/mods/log?tail=5", Ct);
        Assert.Equal("[Info   :   BepInEx] line 296", five.GetProperty("lines")[0].GetString());

        // The set that ran last wins over the active one.
        await using (var db = await Db())
        {
            await db.InstanceMods.Where(row => row.InstanceId == "mods-27").ExecuteUpdateAsync(row => row.SetProperty(mods => mods.RunningSetId, previous), Ct);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Log(previous))!);
        await File.WriteAllTextAsync(Log(previous), "from the running set\n", Ct);
        var running = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-27/mods/log", Ct);
        Assert.Equal(previous, running.GetProperty("setId").GetInt64());
        Assert.Equal(["from the running set"], running.GetProperty("lines").EnumerateArray().Select(line => line.GetString()));

        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-27/mods/log?tail=0", Ct), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-27/mods/log?tail=2001", Ct), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public void The_log_tail_reads_only_the_end_of_a_large_file_and_drops_its_cut_line()
    {
        var file = Path.Combine(factory.DataRoot, "large-" + Guid.NewGuid().ToString("N")[..8] + ".log");
        File.WriteAllLines(file, Enumerable.Range(1, 120_000).Select(line => $"{line:D6} {new string('x', 40)}"));
        Assert.True(new FileInfo(file).Length > 4 * 1024 * 1024);

        var lines = ModsService.TailLines(file, ModsService.LogMaxTail);

        Assert.Equal(ModsService.LogMaxTail, lines.Count);
        Assert.StartsWith("120000 ", lines[^1], StringComparison.Ordinal);
        Assert.StartsWith("118001 ", lines[0], StringComparison.Ordinal);
        Assert.Empty(ModsService.TailLines(file + ".missing", 10));
    }

    [Fact]
    public async Task An_update_check_gives_thunderstores_state_of_each_package()
    {
        factory.Thunderstore.Add("Tester-Abandoned", "1.0.0", [$"{Loader}-5.4.2351"], ("Abandoned.dll", "x"));
        using var client = await ClientWithInstanceAsync("mods-28");
        await SyncAsync(client, "mods-28", new { package = "Tester-Abandoned", version = "1.0.0" });
        factory.Thunderstore.Deprecated["Tester-Abandoned"] = true;

        var check = await (await client.PostAsync("/api/v1/instances/mods-28/mods/check-updates", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(
            ["Tester-Abandoned deprecated", $"{Loader} ok"],
            check.GetProperty("states").EnumerateArray().Select(state => $"{state.GetProperty("package").GetString()} {state.GetProperty("state").GetString()}"));
        var mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-28/mods", Ct);
        Assert.Equal(2, mods.GetProperty("newerVersions").GetProperty("states").GetArrayLength());
        Assert.False(mods.GetProperty("restartRequired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, mods.GetProperty("runningSetId").ValueKind);
    }
}
