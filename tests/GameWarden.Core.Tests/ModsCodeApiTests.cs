extern alias cli;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using cli::GameWarden.Cli;
using GameWarden.Core.Instances;
using GameWarden.Core.Mods;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Tests;

/// <summary>
/// Phase 1c through the API and the CLI (docs/13 §4.4, §4.6–§4.8, §4.11): r2modman codes both ways, configs, update
/// checks. Thunderstore is <see cref="FakeThunderstore"/>; codes are built as r2modman exports them
/// (<see cref="R2ModmanFixture"/>).
/// </summary>
public sealed partial class ModsApiTests
{
    [Fact]
    public async Task A_code_preview_shows_the_mods_as_the_code_has_them_with_their_state_and_changes_nothing()
    {
        factory.Thunderstore.Add("Tester-Old", "1.0.0", [], ("Old.dll", "old"));
        factory.Thunderstore.Deprecated["Tester-Old"] = true;
        using var client = await ClientWithInstanceAsync("mods-16");
        var code = NewCode(R2ModmanFixture.ExportYaml(
            "Friends",
            ("ValheimModding-Jotunn", "2.30.2", true),
            ("ValheimModding-JsonDotNET", "13.0.4", false),
            ("Tester-Old", "1.0.0", null),
            ("Nobody-Missing", "1.0.0", true),
            ("NotAPackage", "1.0.0", true)),
            ("config/randyknapp.mods.epicloot.cfg", "[General]\nfrom the code"),
            ("config/EpicLoot/patches/code.json", "{}"));

        var response = await client.PostAsJsonAsync("/api/v1/instances/mods-16/mods/code-preview", new { code = $" {code.ToUpperInvariant()} " }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, preview.GetProperty("code").GetString());
        Assert.Equal("Friends", preview.GetProperty("profileName").GetString());
        Assert.Matches("^[0-9a-f]{32}$", preview.GetProperty("previewId").GetString());
        var expires = preview.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
        Assert.Equal(
            [
                "ValheimModding-Jotunn 2.30.2 True ok",
                "ValheimModding-JsonDotNET 13.0.4 False ok",
                "Tester-Old 1.0.0 True deprecated",
                "Nobody-Missing 1.0.0 True notFound",
            ],
            preview.GetProperty("entries").EnumerateArray().Select(entry =>
                $"{entry.GetProperty("package").GetString()} {entry.GetProperty("version").GetString()} {entry.GetProperty("enabled").GetBoolean()} {entry.GetProperty("state").GetString()}"));
        Assert.Equal(["EpicLoot/patches/code.json", "randyknapp.mods.epicloot.cfg"], Strings(preview.GetProperty("configFiles")));
        var warnings = Strings(preview.GetProperty("warnings"));
        Assert.Contains("'NotAPackage' is not a Thunderstore package (Namespace-Name); it is left out.", warnings);
        Assert.Contains(warnings, warning => warning.StartsWith("Not on Thunderstore: Nobody-Missing.", StringComparison.Ordinal));

        // Nothing changed: no set, no job, no folder.
        var mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-16/mods", Ct);
        Assert.Equal(JsonValueKind.Null, mods.GetProperty("active").ValueKind);
        Assert.Equal(JsonValueKind.Null, mods.GetProperty("pending").ValueKind);
        Assert.False(Directory.Exists(Path.Combine(InstancePaths.For(factory.DataRoot, "mods-16").Mods, "sets")));
        Assert.Equal(1, factory.Thunderstore.Count($"/legacyprofile/get/{code}/"));
    }

    [Fact]
    public async Task Malformed_unknown_unsafe_and_rate_limited_codes_and_bad_previews_are_refused()
    {
        using var client = await ClientWithInstanceAsync("mods-17");
        await CreateInstanceAsync(client, "mods-17b");
        async Task<JsonElement> PreviewAsync(string code, HttpStatusCode status, string problem) =>
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/instances/mods-17/mods/code-preview", new { code }, Ct), status, problem);

        var requests = factory.Thunderstore.Requests.Count;
        await PreviewAsync("not-a-code", HttpStatusCode.BadRequest, "validation_failed");
        await PreviewAsync("../../package/RandyKnapp/EpicLoot", HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(requests, factory.Thunderstore.Requests.Count); // never sent

        var unknown = await PreviewAsync(Guid.CreateVersion7().ToString(), HttpStatusCode.NotFound, "not_found");
        Assert.Contains("is unknown or expired", unknown.GetProperty("title").GetString(), StringComparison.Ordinal);

        var slippery = NewCode(R2ModmanFixture.ExportYaml("Slippery", ("ValheimModding-Jotunn", "2.30.2", true)), ("config/../../../escaped.cfg", "x"));
        var refused = await PreviewAsync(slippery, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Contains("refused entry 'config/../../../escaped.cfg'", refused.GetProperty("title").GetString(), StringComparison.Ordinal);

        var limited = NewCode(R2ModmanFixture.ExportYaml("Limited", ("ValheimModding-Jotunn", "2.30.2", true)));
        factory.Thunderstore.Intercept = request => request.RequestUri!.AbsolutePath.Contains("/legacyprofile/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : null;
        try
        {
            var problem = await PreviewAsync(limited, HttpStatusCode.BadGateway, ModsService.UpstreamUnavailable);
            Assert.Contains("wait a minute and try again", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            factory.Thunderstore.Intercept = null;
        }

        // A preview applies to its own instance only, and an unknown one is a clear 404.
        var preview = await (await client.PostAsJsonAsync("/api/v1/instances/mods-17/mods/code-preview", new { code = limited }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var previewId = preview.GetProperty("previewId").GetString();
        var other = await client.PutAsJsonAsync("/api/v1/instances/mods-17b/mods", new { previewId }, Ct);
        Assert.Contains("is unknown or expired", (await AssertProblemAsync(other, HttpStatusCode.NotFound, "not_found")).GetProperty("title").GetString(), StringComparison.Ordinal);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/v1/instances/mods-17/mods", new { previewId = "0123" }, Ct), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(
            await client.PutAsJsonAsync("/api/v1/instances/mods-17/mods", new { previewId, entries = Array.Empty<object>() }, Ct),
            HttpStatusCode.BadRequest,
            "validation_failed");
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/v1/instances/mods-17/mods", new { }, Ct), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Applying_a_preview_builds_the_codes_set_as_it_is_with_its_configs()
    {
        using var client = await ClientWithInstanceAsync("mods-18");
        var code = NewCode(
            R2ModmanFixture.ExportYaml(
                "Friends",
                (Loader, "5.4.2333", true),
                ("ValheimModding-Jotunn", "2.30.2", true),
                ("RandyKnapp-EpicLoot", "0.15.4", true),
                ("ValheimModding-JsonDotNET", "13.0.4", false)),
            ("config/randyknapp.mods.epicloot.cfg", "[General]\nfrom the code"),
            ("config/EpicLoot/patches/code.json", "{\"from\":\"code\"}"));
        var preview = await (await client.PostAsJsonAsync("/api/v1/instances/mods-18/mods/code-preview", new { code }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var started = await client.PutAsJsonAsync("/api/v1/instances/mods-18/mods", new { previewId = preview.GetProperty("previewId").GetString() }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var job = await WaitForJobAsync(client, (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("jobId").GetString()!);
        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        var active = (await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-18/mods", Ct)).GetProperty("active");
        Assert.Equal("code", active.GetProperty("origin").GetString());

        // Versions as the code has them (the pack stays 5.4.2333 although Epic Loot names 5.4.2351), nothing added, the
        // disabled mod kept disabled and not installed.
        Assert.Equal(
            [
                $"{Loader} 5.4.2333 chosen",
                "RandyKnapp-EpicLoot 0.15.4 chosen",
                "ValheimModding-Jotunn 2.30.2 chosen",
                "ValheimModding-JsonDotNET 13.0.4 chosen",
            ],
            Entries(active));
        Assert.False(active.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("package").GetString() == "ValheimModding-JsonDotNET").GetProperty("enabled").GetBoolean());
        var paths = InstancePaths.For(factory.DataRoot, "mods-18");
        var set = Path.Combine(paths.Mods, "sets", active.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("preloader 5.4.2333", await File.ReadAllTextAsync(Path.Combine(set, "loader", "BepInExPack_Valheim", "BepInEx", "core", "BepInEx.Preloader.dll"), Ct));
        Assert.False(Directory.Exists(Path.Combine(set, "plugins", "ValheimModding-JsonDotNET")));
        Assert.False(File.Exists(Path.Combine(paths.Mods, "cache", "ValheimModding-JsonDotNET-13.0.4.zip")));
        Assert.Equal("[General]\nfrom the code", await File.ReadAllTextAsync(Path.Combine(set, "config", "randyknapp.mods.epicloot.cfg"), Ct));
        Assert.Equal("{\"from\":\"code\"}", await File.ReadAllTextAsync(Path.Combine(set, "config", "EpicLoot", "patches", "code.json"), Ct));
        var log = await LogAsync(client, job);
        Assert.Contains(log, line => line.Contains($"Warning: {Loader} 5.4.2333 is older than the 5.4.2351 that RandyKnapp-EpicLoot-0.15.4 names.", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("Warning: ValheimModding-JsonDotNET is disabled, but RandyKnapp-EpicLoot-0.15.4 needs it.", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("and 2 config file(s) of the code over them.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Configs_of_a_new_set_are_the_defaults_then_the_active_sets_then_the_codes()
    {
        using var client = await ClientWithInstanceAsync("mods-19", BepInExAdapter);
        var paths = InstancePaths.For(factory.DataRoot, "mods-19");
        string Config(long id, params string[] parts) =>
            Path.Combine([paths.Mods, "sets", id.ToString(System.Globalization.CultureInfo.InvariantCulture), "BepInEx", "config", .. parts]);
        async Task<long> ActiveAsync() =>
            (await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-19/mods", Ct)).GetProperty("active").GetProperty("id").GetInt64();

        await SyncAsync(client, "mods-19", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var first = await ActiveAsync();
        await File.WriteAllTextAsync(Config(first, "randyknapp.mods.epicloot.cfg"), "[General]\nchanged by the game", Ct);
        Directory.CreateDirectory(Config(first, "EpicLoot", "patches"));
        await File.WriteAllTextAsync(Config(first, "EpicLoot", "patches", "extra.json"), "previous", Ct);
        await File.WriteAllTextAsync(Config(first, "EpicLoot", "patches", "code.json"), "previous", Ct);

        var code = NewCode(
            R2ModmanFixture.ExportYaml("Friends", ("RandyKnapp-EpicLoot", "0.15.4", true)),
            ("config/randyknapp.mods.epicloot.cfg", "[General]\nfrom the code"),
            ("config/EpicLoot/patches/code.json", "from the code"));
        var preview = await (await client.PostAsJsonAsync("/api/v1/instances/mods-19/mods/code-preview", new { code }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var started = await client.PutAsJsonAsync("/api/v1/instances/mods-19/mods", new { previewId = preview.GetProperty("previewId").GetString() }, Ct);
        var job = await WaitForJobAsync(client, (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("jobId").GetString()!);
        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        var second = await ActiveAsync();

        // Built, not started: the mods' defaults, then the active set's files, then the code's (the code wins).
        Assert.Equal("[Logging]", await File.ReadAllTextAsync(Config(second, "BepInEx.cfg"), Ct)); // default
        Assert.Equal("previous", await File.ReadAllTextAsync(Config(second, "EpicLoot", "patches", "extra.json"), Ct)); // the active set
        Assert.Equal("[General]\nfrom the code", await File.ReadAllTextAsync(Config(second, "randyknapp.mods.epicloot.cfg"), Ct)); // the code
        Assert.Equal("from the code", await File.ReadAllTextAsync(Config(second, "EpicLoot", "patches", "code.json"), Ct));
        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "sets", second.ToString(System.Globalization.CultureInfo.InvariantCulture), "code-config")));
        Assert.Contains(await LogAsync(client, job), line => line.Contains($"4 config file(s) of the active set {first}, and 2 config file(s) of the code over them.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_active_sets_config_files_are_listed_and_shown_with_their_paths_checked()
    {
        using var client = await ClientWithInstanceAsync("mods-22", BepInExAdapter);
        var none = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-22/mods/configs", Ct);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("setId").ValueKind);
        Assert.Empty(none.GetProperty("files").EnumerateArray());
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-22/mods/configs/content?path=BepInEx.cfg", Ct), HttpStatusCode.NotFound, "not_found");

        await SyncAsync(client, "mods-22", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var setId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-22/mods", Ct)).GetProperty("active").GetProperty("id").GetInt64();
        var config = Path.Combine(InstancePaths.For(factory.DataRoot, "mods-22").Mods, "sets", setId.ToString(System.Globalization.CultureInfo.InvariantCulture), "BepInEx", "config");
        Directory.CreateDirectory(Path.Combine(config, "EpicLoot", "patches"));
        await File.WriteAllTextAsync(Path.Combine(config, "EpicLoot", "patches", "extra.json"), "{\"a\":1}", Ct);
        await File.WriteAllBytesAsync(Path.Combine(config, "huge.cfg"), new byte[ModsService.ConfigViewMaxBytes + 1], Ct);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-22/mods/configs", Ct);

        Assert.Equal(setId, list.GetProperty("setId").GetInt64());
        Assert.Equal(
            // The fake packages' files start with a UTF-8 byte order mark (3 bytes), which the text leaves out.
            ["BepInEx.cfg 12", "EpicLoot/patches/extra.json 7", $"huge.cfg {ModsService.ConfigViewMaxBytes + 1}", "randyknapp.mods.epicloot.cfg 12"],
            list.GetProperty("files").EnumerateArray().Select(file => $"{file.GetProperty("path").GetString()} {file.GetProperty("size").GetInt64()}"));
        var content = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-22/mods/configs/content?path=randyknapp.mods.epicloot.cfg", Ct);
        Assert.Equal("[General]", content.GetProperty("text").GetString());
        Assert.Equal(setId, content.GetProperty("setId").GetInt64());
        Assert.True(content.GetProperty("editable").GetBoolean());
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(config, "randyknapp.mods.epicloot.cfg"), Ct))),
            content.GetProperty("sha256").GetString());
        var nested = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-22/mods/configs/content?path=" + Uri.EscapeDataString("EpicLoot\\patches\\extra.json"), Ct);
        Assert.Equal("EpicLoot/patches/extra.json", nested.GetProperty("path").GetString());
        Assert.Equal("{\"a\":1}", nested.GetProperty("text").GetString());

        foreach (var bad in new[] { "../../../mods.json", "/etc/passwd", "C:/Windows/win.ini", "EpicLoot/" })
        {
            await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-22/mods/configs/content?path=" + Uri.EscapeDataString(bad), Ct), HttpStatusCode.BadRequest, "validation_failed");
        }

        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-22/mods/configs/content", Ct), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-22/mods/configs/content?path=missing.cfg", Ct), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-22/mods/configs/content?path=huge.cfg", Ct), HttpStatusCode.Conflict, "invalid_state");
    }

    [Fact]
    public async Task The_cli_prints_a_codes_preview_and_applies_it_only_with_yes()
    {
        using var client = await ClientWithInstanceAsync("mods-23");
        var code = NewCode(R2ModmanFixture.ExportYaml("Friends", ("ValheimModding-Jotunn", "2.30.2", true), ("ValheimModding-JsonDotNET", "13.0.4", false)), ("config/a.cfg", "a"));

        var (exit, output, _) = await CliAsync(client, "import", "mods-23", code);

        Assert.Equal(0, exit);
        Assert.Contains($"Code {code}: profile \"Friends\", 2 mod(s), 1 config file(s).", output, StringComparison.Ordinal);
        Assert.Matches(@"ValheimModding-JsonDotNET\s+13\.0\.4\s+no\s+ok", output);
        Assert.Contains($"Nothing changed. Apply it with: gamewarden mods import mods-23 {code} --yes", output, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-23/mods", Ct)).GetProperty("active").ValueKind);

        (exit, output, _) = await CliAsync(client, "import", "mods-23", code, "--yes");

        Assert.Equal(0, exit);
        Assert.Contains("succeeded", output, StringComparison.Ordinal);
        Assert.Contains("Restart to apply: gamewarden restart mods-23", output, StringComparison.Ordinal);
        var active = (await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-23/mods", Ct)).GetProperty("active");
        Assert.Equal("code", active.GetProperty("origin").GetString());
        Assert.Equal(["ValheimModding-Jotunn 2.30.2 chosen", "ValheimModding-JsonDotNET 13.0.4 chosen", $"{Loader} 5.4.2333 dependency"], Entries(active));

        (exit, _, var error) = await CliAsync(client, "import", "mods-23", "not-a-code");
        Assert.Equal(1, exit);
        Assert.StartsWith("400 validation_failed: 'not-a-code' is not an r2modman profile code", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cli_adds_updates_removes_and_shares_mods_from_the_active_sets_chosen_ones()
    {
        factory.Thunderstore.Add("Tester-Cli", "1.0.0", [], ("Cli.dll", "1.0.0"));
        using var client = await ClientWithInstanceAsync("mods-24");

        var (exit, output, _) = await CliAsync(client, "add", "mods-24", "Tester-Cli@1.0.0");
        Assert.Equal(0, exit);
        Assert.Contains("Restart to apply: gamewarden restart mods-24", output, StringComparison.Ordinal);
        Assert.Equal(0, (await CliAsync(client, "add", "mods-24", "ValheimModding-JsonDotNET")).Exit);
        Assert.Equal(["Tester-Cli 1.0.0 chosen", "ValheimModding-JsonDotNET 13.0.4 chosen", $"{Loader} 5.4.2360 dependency"], await ActiveEntriesAsync());
        (exit, output, _) = await CliAsync(client, "add", "mods-24", "Tester-Cli");
        Assert.Equal(0, exit);
        Assert.Contains("Tester-Cli 1.0.0 is already in the set; nothing changed.", output, StringComparison.Ordinal);

        // A newer version: the check prints it, and only a named mod or --all is updated.
        factory.Thunderstore.Add("Tester-Cli", "1.1.0", [], ("Cli.dll", "1.1.0"));
        (exit, output, _) = await CliAsync(client, "update", "mods-24");
        Assert.Equal(0, exit);
        Assert.Matches(@"Tester-Cli\s+1\.0\.0\s+1\.1\.0\s+chosen", output);
        Assert.Contains("Nothing changed. Update them with: gamewarden mods update mods-24 --all", output, StringComparison.Ordinal);
        Assert.Equal(0, (await CliAsync(client, "update", "mods-24", "Tester-Cli")).Exit);
        Assert.Equal(["Tester-Cli 1.1.0 chosen", "ValheimModding-JsonDotNET 13.0.4 chosen", $"{Loader} 5.4.2360 dependency"], await ActiveEntriesAsync());
        (exit, output, _) = await CliAsync(client, "update", "mods-24", "--all");
        Assert.Equal(0, exit);
        Assert.Contains("Every mod is at its latest version.", output, StringComparison.Ordinal);

        // Remove: a chosen mod goes; a dependency cannot be removed by itself.
        Assert.Equal(0, (await CliAsync(client, "remove", "mods-24", "ValheimModding-JsonDotNET")).Exit);
        Assert.Equal(["Tester-Cli 1.1.0 chosen", $"{Loader} 5.4.2360 dependency"], await ActiveEntriesAsync());
        var (removeExit, _, removeError) = await CliAsync(client, "remove", "mods-24", Loader);
        Assert.Equal(1, removeExit);
        Assert.Contains("is a dependency of other mods in the set", removeError, StringComparison.Ordinal);

        // Share, then the overview shows the code.
        (exit, output, _) = await CliAsync(client, "share", "mods-24");
        Assert.Equal(0, exit);
        var code = Assert.Single(output.Split('\n'), line => line.StartsWith("Share code for friends: ", StringComparison.Ordinal))["Share code for friends: ".Length..].Trim();
        Assert.True(Guid.TryParseExact(code, "D", out _));
        (exit, output, _) = await CliAsync(client, "mods-24");
        Assert.Equal(0, exit);
        Assert.Contains("mods-24: Mods off (loader fake)", output, StringComparison.Ordinal);
        Assert.Contains($"Share code for friends: {code}", output, StringComparison.Ordinal);
        Assert.Matches(@"Tester-Cli\s+1\.1\.0\s+yes\s+chosen", output);
        Assert.Contains("Previous set", output, StringComparison.Ordinal);

        Assert.Equal(2, (await CliAsync(client, "frobnicate", "mods-24")).Exit);
        Assert.Equal(2, (await CliAsync(client, "update", "mods-24", "Tester-Cli", "--all")).Exit);
        Assert.Equal(("Tester-Cli", (string?)"1.0.0"), ModsCommands.ParsePackage("Tester-Cli@1.0.0"));
        Assert.Equal(("Tester-Cli", (string?)null), ModsCommands.ParsePackage("Tester-Cli"));

        async Task<string[]> ActiveEntriesAsync() =>
            Entries((await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-24/mods", Ct)).GetProperty("active"));
    }

    /// <summary>A code Thunderstore knows, with this <c>export.r2x</c> and these other zip entries.</summary>
    private string NewCode(string exportYaml, params (string Name, string Content)[] files)
    {
        var code = Guid.CreateVersion7().ToString("D");
        factory.Thunderstore.Profiles[code] = R2ModmanFixture.Text(exportYaml, files);
        return code;
    }

    /// <summary>Runs <c>gamewarden mods …</c> with <c>--wait</c> against the test Core.</summary>
    private static async Task<(int Exit, string Output, string Error)> CliAsync(HttpClient client, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await new ModsCommands(client, output, error) { PollInterval = TimeSpan.FromMilliseconds(50) }.RunAsync([.. arguments], wait: true);
        return (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];
}
