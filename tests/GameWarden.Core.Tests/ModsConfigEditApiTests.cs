using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Instances;
using GameWarden.Core.Mods;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Tests;

/// <summary>
/// Phase 3 (docs/13 §4.7, ADR-0021): config files of the active set edited through the API and the CLI, with revisions;
/// rollback keeps the configs.
/// </summary>
public sealed partial class ModsApiTests
{
    private const string EpicLootConfig = "randyknapp.mods.epicloot.cfg";

    [Fact]
    public async Task An_edit_writes_the_active_sets_file_keeps_what_was_on_disk_and_revisions_survive_a_new_set()
    {
        using var client = await ClientWithInstanceAsync("mods-30", BepInExAdapter);
        await using var stops = StopsAtTheEnd(client, "mods-30");
        var paths = InstancePaths.For(factory.DataRoot, "mods-30");
        await SyncAsync(client, "mods-30", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var file = await ActiveConfigFileAsync(client, "mods-30", paths, EpicLootConfig);
        var original = await File.ReadAllBytesAsync(file, Ct);
        Assert.True(original.AsSpan().StartsWith(Encoding.UTF8.Preamble)); // the fake packages' files have a byte order mark

        // The first save stores what was on disk as a server revision, then the edit; the file keeps its byte order mark.
        var loaded = await ConfigAsync(client, "mods-30", EpicLootConfig);
        Assert.Equal(Sha256(original), loaded.GetProperty("sha256").GetString());
        var saved = await SaveConfigAsync(client, "mods-30", EpicLootConfig, "[General]\nEnabled = false", loaded.GetProperty("sha256").GetString()!);
        Assert.False(saved.GetProperty("running").GetBoolean());
        byte[] withBom = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("[General]\nEnabled = false")];
        Assert.Equal(withBom, await File.ReadAllBytesAsync(file, Ct));
        Assert.Equal(Sha256(await File.ReadAllBytesAsync(file, Ct)), saved.GetProperty("sha256").GetString());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.gamewarden-tmp", SearchOption.AllDirectories));
        Assert.Equal(["edit", "server"], await RevisionKindsAsync(client, "mods-30", EpicLootConfig));
        var revisions = await RevisionsAsync(client, "mods-30", EpicLootConfig);
        Assert.Equal(saved.GetProperty("revisionId").GetInt64(), revisions[0].GetProperty("id").GetInt64());
        var serverRevision = revisions[1].GetProperty("id").GetInt64();
        var first = await client.GetFromJsonAsync<JsonElement>($"/api/v1/instances/mods-30/mods/configs/revisions/{serverRevision}", Ct);
        Assert.Equal("[General]", first.GetProperty("text").GetString());
        Assert.Equal(EpicLootConfig, first.GetProperty("path").GetString());
        Assert.Equal(Sha256(original), first.GetProperty("sha256").GetString());
        await WaitForActivityAsync(client, "mods-30", $"Config {EpicLootConfig} edited.");

        // A second save while nothing else wrote the file: no server revision.
        saved = await SaveConfigAsync(client, "mods-30", EpicLootConfig, "[General]\nEnabled = true", saved.GetProperty("sha256").GetString()!);
        Assert.Equal(["edit", "edit", "server"], await RevisionKindsAsync(client, "mods-30", EpicLootConfig));

        // The game writes the file (with CRLF line ends): a save from the old text is refused and writes nothing.
        await File.WriteAllTextAsync(file, "[General]\r\nEnabled = true\r\nWritten = by the game\r\n", Ct);
        var stale = await PutConfigAsync(client, "mods-30", EpicLootConfig, "[General]\nmine", saved.GetProperty("sha256").GetString()!);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, ModsService.ConfigChanged);
        Assert.Equal("[General]\r\nEnabled = true\r\nWritten = by the game\r\n", await File.ReadAllTextAsync(file, Ct));
        Assert.Equal(3, (await RevisionsAsync(client, "mods-30", EpicLootConfig)).Length);

        // Saved from the game's version: the game's text becomes a server revision, and the file keeps CRLF.
        loaded = await ConfigAsync(client, "mods-30", EpicLootConfig);
        await SaveConfigAsync(client, "mods-30", EpicLootConfig, "[General]\nEnabled = true\nWritten = by the Owner\n", loaded.GetProperty("sha256").GetString()!);
        Assert.Equal("[General]\r\nEnabled = true\r\nWritten = by the Owner\r\n", await File.ReadAllTextAsync(file, Ct));
        Assert.Equal(["edit", "server", "edit", "edit", "server"], await RevisionKindsAsync(client, "mods-30", EpicLootConfig));

        // A new set gets the edited file; the revisions belong to the path, so they stay.
        await SyncAsync(client, "mods-30", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" }, new { package = "ValheimModding-JsonDotNET" });
        var newFile = await ActiveConfigFileAsync(client, "mods-30", paths, EpicLootConfig);
        Assert.NotEqual(file, newFile);
        Assert.Equal("[General]\r\nEnabled = true\r\nWritten = by the Owner\r\n", await File.ReadAllTextAsync(newFile, Ct));
        Assert.Equal(5, (await RevisionsAsync(client, "mods-30", EpicLootConfig)).Length);

        // Restore the first revision: its bytes as they were, as a new revision; a stale base or an unknown id is refused.
        loaded = await ConfigAsync(client, "mods-30", EpicLootConfig);
        await AssertProblemAsync(await RestoreAsync(client, "mods-30", serverRevision, saved.GetProperty("sha256").GetString()!), HttpStatusCode.Conflict, ModsService.ConfigChanged);
        await AssertProblemAsync(await RestoreAsync(client, "mods-30", 999_999, loaded.GetProperty("sha256").GetString()!), HttpStatusCode.NotFound, "not_found");
        var restored = await RestoreAsync(client, "mods-30", serverRevision, loaded.GetProperty("sha256").GetString()!);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(newFile, Ct));
        Assert.Equal(["restore", "edit", "server", "edit", "edit", "server"], await RevisionKindsAsync(client, "mods-30", EpicLootConfig));
        await WaitForActivityAsync(client, "mods-30", $"Config {EpicLootConfig} restored to revision {serverRevision}.");

        // While the server runs the active set with Mods on, the answer says so.
        await SetModsAsync(client, "mods-30", true);
        await StartAsync(client, "mods-30");
        loaded = await ConfigAsync(client, "mods-30", EpicLootConfig);
        saved = await SaveConfigAsync(client, "mods-30", EpicLootConfig, "[General]\nlive", loaded.GetProperty("sha256").GetString()!);
        Assert.True(saved.GetProperty("running").GetBoolean());
        await StopAsync(client, "mods-30");
    }

    [Fact]
    public async Task Edits_are_refused_for_bad_paths_types_sizes_and_json_and_while_a_mods_job_runs()
    {
        using var client = await ClientWithInstanceAsync("mods-31", BepInExAdapter);
        var anySha = new string('0', 64);
        await AssertProblemAsync(await PutConfigAsync(client, "mods-31", EpicLootConfig, "x", anySha), HttpStatusCode.NotFound, "not_found");

        await SyncAsync(client, "mods-31", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var paths = InstancePaths.For(factory.DataRoot, "mods-31");
        var folder = Path.GetDirectoryName(await ActiveConfigFileAsync(client, "mods-31", paths, EpicLootConfig))!;
        await File.WriteAllTextAsync(Path.Combine(folder, "patch.json"), "{\"a\":1}", Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.dat"), "binary", Ct);
        await File.WriteAllBytesAsync(Path.Combine(folder, "huge.cfg"), new byte[ModsService.ConfigViewMaxBytes + 1], Ct);
        var sha = (await ConfigAsync(client, "mods-31", EpicLootConfig)).GetProperty("sha256").GetString()!;

        foreach (var bad in new[] { "../../../mods.json", "/etc/passwd", "C:/Windows/win.ini", "EpicLoot/", "" })
        {
            await AssertProblemAsync(await PutConfigAsync(client, "mods-31", bad, "x", sha), HttpStatusCode.BadRequest, "validation_failed");
        }

        await AssertProblemAsync(await PutConfigAsync(client, "mods-31", "missing.cfg", "x", sha), HttpStatusCode.NotFound, "not_found");
        var dat = await ConfigAsync(client, "mods-31", "notes.dat");
        Assert.False(dat.GetProperty("editable").GetBoolean());
        await AssertProblemAsync(await PutConfigAsync(client, "mods-31", "notes.dat", "x", dat.GetProperty("sha256").GetString()!), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await PutConfigAsync(client, "mods-31", "huge.cfg", "x", anySha), HttpStatusCode.Conflict, "invalid_state");
        await AssertProblemAsync(
            await PutConfigAsync(client, "mods-31", EpicLootConfig, new string('x', (int)ModsService.ConfigViewMaxBytes + 1), sha),
            HttpStatusCode.BadRequest,
            "validation_failed");
        await AssertProblemAsync(await PutConfigAsync(client, "mods-31", EpicLootConfig, "x", "not a hash"), HttpStatusCode.BadRequest, "validation_failed");
        var bodyless = await client.PutAsJsonAsync($"/api/v1/instances/mods-31/mods/configs/content?path={EpicLootConfig}", new { baseSha256 = sha }, Ct);
        await AssertProblemAsync(bodyless, HttpStatusCode.BadRequest, "validation_failed");

        // JSON is checked before it is written; comments and trailing commas, which mods' JSON readers take, pass.
        var jsonSha = (await ConfigAsync(client, "mods-31", "patch.json")).GetProperty("sha256").GetString()!;
        var badJson = await AssertProblemAsync(await PutConfigAsync(client, "mods-31", "patch.json", "{\"a\":", jsonSha), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Contains("is not valid JSON", badJson.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal("{\"a\":1}", await File.ReadAllTextAsync(Path.Combine(folder, "patch.json"), Ct));
        await SaveConfigAsync(client, "mods-31", "patch.json", "{\n  // a comment\n  \"a\": 2,\n}", jsonSha);

        // Nothing was stored for the refused edits.
        Assert.Empty(await RevisionsAsync(client, "mods-31", EpicLootConfig));
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-31/mods/configs/revisions?path=" + Uri.EscapeDataString("../x.cfg"), Ct), HttpStatusCode.BadRequest, "validation_failed");

        // While a mods job builds a set, an edit could race its copy of the configs: mods_busy.
        factory.Thunderstore.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string jobId;
        try
        {
            var started = await client.PutAsJsonAsync("/api/v1/instances/mods-31/mods", new { entries = new[] { new { package = "ValheimModding-JsonDotNET" } } }, Ct);
            jobId = (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("jobId").GetString()!;
            await WaitForStageAsync(client, jobId, "download");
            await AssertProblemAsync(await PutConfigAsync(client, "mods-31", EpicLootConfig, "x", sha), HttpStatusCode.Conflict, "mods_busy");
        }
        finally
        {
            factory.Thunderstore.HoldDownloads.TrySetResult();
            factory.Thunderstore.HoldDownloads = null;
        }

        Assert.Equal("succeeded", (await WaitForJobAsync(client, jobId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Only_the_newest_50_revisions_of_a_file_are_kept()
    {
        using var client = await ClientWithInstanceAsync("mods-32", BepInExAdapter);
        await SyncAsync(client, "mods-32", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var paths = InstancePaths.For(factory.DataRoot, "mods-32");
        var other = Path.Combine(Path.GetDirectoryName(await ActiveConfigFileAsync(client, "mods-32", paths, EpicLootConfig))!, "other.cfg");
        await File.WriteAllTextAsync(other, "[Other]", Ct);
        await SaveConfigAsync(client, "mods-32", "other.cfg", "[Other]\nx = 1", (await ConfigAsync(client, "mods-32", "other.cfg")).GetProperty("sha256").GetString()!);

        var sha = (await ConfigAsync(client, "mods-32", EpicLootConfig)).GetProperty("sha256").GetString()!;
        long last = 0;
        for (var edit = 1; edit <= ModsService.ConfigRevisionsKept + 2; edit++)
        {
            var saved = await SaveConfigAsync(client, "mods-32", EpicLootConfig, $"[General]\nedit = {edit}", sha);
            sha = saved.GetProperty("sha256").GetString()!;
            last = saved.GetProperty("revisionId").GetInt64();
        }

        // A server revision and 52 edits: the server one and the two oldest edits are gone; the other file keeps its own.
        var revisions = await RevisionsAsync(client, "mods-32", EpicLootConfig);
        Assert.Equal(ModsService.ConfigRevisionsKept, revisions.Length);
        Assert.Equal(last, revisions[0].GetProperty("id").GetInt64());
        Assert.All(revisions, revision => Assert.Equal("edit", revision.GetProperty("kind").GetString()));
        var oldest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/instances/mods-32/mods/configs/revisions/{revisions[^1].GetProperty("id").GetInt64()}", Ct);
        Assert.Equal("[General]\nedit = 3", oldest.GetProperty("text").GetString());
        Assert.Equal(["edit", "server"], await RevisionKindsAsync(client, "mods-32", "other.cfg"));

        // The revisions go with the instance.
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/v1/instances/mods-32?mode=purge&confirm=mods-32", Ct)).StatusCode);
        await using var db = await Db();
        Assert.False(await db.ModConfigRevisions.AnyAsync(row => row.InstanceId == "mods-32", Ct));
    }

    [Fact]
    public async Task Rollback_copies_the_active_sets_configs_into_the_previous_set()
    {
        using var client = await ClientWithInstanceAsync("mods-33", BepInExAdapter);
        var paths = InstancePaths.For(factory.DataRoot, "mods-33");
        await SyncAsync(client, "mods-33", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var previousFile = await ActiveConfigFileAsync(client, "mods-33", paths, EpicLootConfig);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(previousFile)!, "only-previous.cfg"), "kept", Ct);
        await SyncAsync(client, "mods-33", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" }, new { package = "ValheimModding-JsonDotNET" });
        var activeFile = await ActiveConfigFileAsync(client, "mods-33", paths, EpicLootConfig);
        await File.WriteAllTextAsync(activeFile, "[General]\nedited in the newer set", Ct);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(activeFile)!, "EpicLoot"));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(activeFile)!, "EpicLoot", "new.json"), "{}", Ct);

        var rollback = await client.PostAsync("/api/v1/instances/mods-33/mods/rollback", null, Ct);

        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(previousFile, await ActiveConfigFileAsync(client, "mods-33", paths, EpicLootConfig));
        Assert.Equal("[General]\nedited in the newer set", await File.ReadAllTextAsync(previousFile, Ct));
        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(previousFile)!, "EpicLoot", "new.json"), Ct));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(previousFile)!, "only-previous.cfg"), Ct));
    }

    [Fact]
    public async Task The_cli_lists_a_files_revisions_and_restores_one()
    {
        using var client = await ClientWithInstanceAsync("mods-34", BepInExAdapter);
        await SyncAsync(client, "mods-34", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var paths = InstancePaths.For(factory.DataRoot, "mods-34");
        var file = await ActiveConfigFileAsync(client, "mods-34", paths, EpicLootConfig);
        var original = await File.ReadAllBytesAsync(file, Ct);

        var (exit, output, _) = await CliAsync(client, "configs", "mods-34", EpicLootConfig, "--history");
        Assert.Equal(0, exit);
        Assert.Contains("has no revisions yet", output, StringComparison.Ordinal);

        var saved = await SaveConfigAsync(client, "mods-34", EpicLootConfig, "[General]\ncli", (await ConfigAsync(client, "mods-34", EpicLootConfig)).GetProperty("sha256").GetString()!);
        var serverRevision = (await RevisionsAsync(client, "mods-34", EpicLootConfig))[1].GetProperty("id").GetInt64();
        (exit, output, _) = await CliAsync(client, "configs", "mods-34", EpicLootConfig, "--history");
        Assert.Equal(0, exit);
        Assert.Matches($@"\s{saved.GetProperty("revisionId").GetInt64()}\s+\S+ \S+\s+edit\s", output);
        Assert.Matches($@"\s{serverRevision}\s+\S+ \S+\s+server\s", output);

        (exit, output, _) = await CliAsync(client, "configs", "mods-34", EpicLootConfig, "--restore", serverRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, exit);
        Assert.Contains($"{EpicLootConfig} restored to revision {serverRevision}", output, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllBytesAsync(file, Ct));

        Assert.Equal(2, (await CliAsync(client, "configs", "mods-34", EpicLootConfig, "--restore", "abc")).Exit);
        Assert.Equal(2, (await CliAsync(client, "configs", "mods-34", "--history")).Exit);
        Assert.Equal(2, (await CliAsync(client, "configs", "mods-34", EpicLootConfig, "--restore")).Exit);
        Assert.Equal(2, (await CliAsync(client, "configs", "mods-34", EpicLootConfig, "--history", "--restore", "1")).Exit);
        Assert.Equal(1, (await CliAsync(client, "configs", "mods-34", "other.cfg", "--restore", serverRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))).Exit);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The full path of a config file in the active set's <c>BepInEx/config</c>.</summary>
    private static async Task<string> ActiveConfigFileAsync(HttpClient client, string id, InstancePaths paths, string path)
    {
        var setId = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/instances/{id}/mods", Ct)).GetProperty("active").GetProperty("id").GetInt64();
        return Path.Combine(paths.Mods, "sets", setId.ToString(System.Globalization.CultureInfo.InvariantCulture), "BepInEx", "config", path);
    }

    private static async Task<JsonElement> ConfigAsync(HttpClient client, string id, string path) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/instances/{id}/mods/configs/content?path={Uri.EscapeDataString(path)}", Ct);

    private static Task<HttpResponseMessage> PutConfigAsync(HttpClient client, string id, string path, string content, string baseSha256) =>
        client.PutAsJsonAsync($"/api/v1/instances/{id}/mods/configs/content?path={Uri.EscapeDataString(path)}", new { content, baseSha256 }, Ct);

    private static async Task<JsonElement> SaveConfigAsync(HttpClient client, string id, string path, string content, string baseSha256)
    {
        var response = await PutConfigAsync(client, id, path, content, baseSha256);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, string id, long revisionId, string baseSha256) =>
        client.PostAsJsonAsync($"/api/v1/instances/{id}/mods/configs/revisions/{revisionId}/restore", new { baseSha256 }, Ct);

    private static async Task<JsonElement[]> RevisionsAsync(HttpClient client, string id, string path) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"/api/v1/instances/{id}/mods/configs/revisions?path={Uri.EscapeDataString(path)}", Ct)).GetProperty("revisions").EnumerateArray()];

    private static async Task<string[]> RevisionKindsAsync(HttpClient client, string id, string path) =>
        [.. (await RevisionsAsync(client, id, path)).Select(revision => revision.GetProperty("kind").GetString()!)];

    /// <summary>Activity lines are written by a background queue.</summary>
    private static async Task WaitForActivityAsync(HttpClient client, string id, string message)
    {
        var deadline = DateTime.UtcNow + CoreHarness.Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var items = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/activity?instanceId={id}", Ct);
            if (items!.Any(item => item.GetProperty("message").GetString() == message))
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"No activity '{message}' for {id}.");
    }
}
