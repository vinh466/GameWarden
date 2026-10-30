using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameWarden.Core.Data;
using GameWarden.Core.Instances;
using GameWarden.Core.Jobs;
using GameWarden.Core.Mods;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameWarden.Core.Tests;

/// <summary>A Core with the fake loader and recorded Thunderstore answers (docs/13 §4.12).</summary>
public sealed class ModsApiFactory : CoreApiFactory
{
    internal FakeThunderstore Thunderstore { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<IModLoader, FakeModLoader>();
        services.AddSingleton(new ThunderstorePolicy([TimeSpan.Zero, TimeSpan.Zero], TimeSpan.Zero, TimeSpan.FromSeconds(30)));
        services.AddHttpClient(ThunderstoreClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Thunderstore);
    }
}

/// <summary>
/// The <c>mods.sync</c> job, activation and rollback through the API (docs/13 §4.5, §4.11), on the development
/// platform: as on Windows without isolation, <c>IInstanceFiles</c> runs its work directly.
/// </summary>
[Trait("Category", "Api")]
[Collection(CoreApiFactory.Collection)]
public sealed class ModsApiTests(ModsApiFactory factory) : IClassFixture<ModsApiFactory>
{
    private const string Loader = FakeThunderstore.Loader;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sync_resolves_downloads_builds_and_activates_a_set()
    {
        using var client = await ClientWithInstanceAsync("mods-1");
        var empty = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-1/mods", Ct);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("active").ValueKind);
        Assert.Equal(FakeModLoader.LoaderId, empty.GetProperty("loader").GetString());
        Assert.False(empty.GetProperty("enabled").GetBoolean());

        var job = await SyncAsync(client, "mods-1", new { package = "RandyKnapp-EpicLoot" });

        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        var mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-1/mods", Ct);
        var active = mods.GetProperty("active");
        Assert.Equal("ready", active.GetProperty("state").GetString());
        Assert.Equal("edit", active.GetProperty("origin").GetString());
        Assert.Equal(job.GetProperty("id").GetString(), active.GetProperty("jobId").GetString());
        Assert.Equal(
            [
                "RandyKnapp-EpicLoot 0.15.4 chosen",
                $"{Loader} 5.4.2351 dependency",
                "ValheimModding-Jotunn 2.30.2 dependency",
                "ValheimModding-JsonDotNET 13.0.4 dependency",
            ],
            Entries(active));
        Assert.Equal(JsonValueKind.Null, mods.GetProperty("previous").ValueKind);
        Assert.Equal(JsonValueKind.Null, mods.GetProperty("pending").ValueKind);

        // The set folder, laid out by the loader; Thunderstore's own files are left out.
        var paths = InstancePaths.For(factory.DataRoot, "mods-1");
        var set = Path.Combine(paths.Mods, "sets", active.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("preloader 5.4.2351", await File.ReadAllTextAsync(Path.Combine(set, "loader", "BepInEx", "core", "BepInEx.Preloader.dll"), Ct));
        Assert.Equal("epic loot", await File.ReadAllTextAsync(Path.Combine(set, "plugins", "RandyKnapp-EpicLoot", "EpicLoot.dll"), Ct));
        Assert.True(File.Exists(Path.Combine(set, "plugins", "ValheimModding-Jotunn", "plugins", "Jotunn.dll")));
        Assert.False(File.Exists(Path.Combine(set, "plugins", "RandyKnapp-EpicLoot", "manifest.json")));
        Assert.Equal(4, Directory.GetFiles(Path.Combine(paths.Mods, "cache"), "*.zip").Length);

        // mods.json is a copy of the active set; the downloads are gone; the hashes are trusted from now on.
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(paths.Mods, ModsService.MetadataFile), Ct));
        Assert.Equal(active.GetProperty("id").GetInt64(), metadata.RootElement.GetProperty("setId").GetInt64());
        Assert.Equal(4, metadata.RootElement.GetProperty("mods").GetArrayLength());
        Assert.False(Directory.Exists(Path.Combine(factory.DataRoot, "tmp", "mods", job.GetProperty("id").GetString()!)));
        await using (var db = await Db())
        {
            var trusted = await db.PackageVersions.AsNoTracking().ToListAsync(Ct);
            foreach (var (package, version) in new[] { ("RandyKnapp-EpicLoot", "0.15.4"), (Loader, "5.4.2351"), ("ValheimModding-JsonDotNET", "13.0.4"), ("ValheimModding-Jotunn", "2.30.2") })
            {
                var row = Assert.Single(trusted, row => row.Package == package && row.Version == version);
                Assert.Equal(64, row.Sha256.Length);
                Assert.True(row.Size > 0);
            }
        }

        var log = await LogAsync(client, job);
        Assert.Contains(log, line => line.EndsWith("Stage: resolve", StringComparison.Ordinal));
        Assert.Contains(log, line => line.EndsWith("Stage: activate", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("RandyKnapp-EpicLoot: the latest version is 0.15.4.", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("RandyKnapp-EpicLoot-0.15.4: downloaded", StringComparison.Ordinal));
        Assert.All(factory.Thunderstore.Requests, request => Assert.StartsWith("GameWarden/", request.UserAgent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_new_set_keeps_the_last_one_as_previous_and_rollback_swaps_them()
    {
        using var client = await ClientWithInstanceAsync("mods-2");
        var noPrevious = await client.PostAsync("/api/v1/instances/mods-2/mods/rollback", null, Ct);
        await AssertProblemAsync(noPrevious, HttpStatusCode.Conflict, "invalid_state");

        var first = await SyncAsync(client, "mods-2", new { package = "RandyKnapp-EpicLoot", version = "0.15.4" });
        var jotunnDownloads = factory.Thunderstore.Count("/download/ValheimModding/Jotunn/");
        var second = await SyncAsync(client, "mods-2", new { package = "ValheimModding-Jotunn", version = "2.30.2" });

        Assert.Equal("succeeded", second.GetProperty("state").GetString());
        Assert.Equal(jotunnDownloads, factory.Thunderstore.Count("/download/ValheimModding/Jotunn/")); // from the cache
        Assert.Contains(await LogAsync(client, second), line => line.Contains("ValheimModding-Jotunn-2.30.2: in the cache.", StringComparison.Ordinal));
        var mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-2/mods", Ct);
        var firstSet = mods.GetProperty("previous").GetProperty("id").GetInt64();
        var secondSet = mods.GetProperty("active").GetProperty("id").GetInt64();
        Assert.Equal(first.GetProperty("id").GetString(), mods.GetProperty("previous").GetProperty("jobId").GetString());
        Assert.Equal(["ValheimModding-Jotunn 2.30.2 chosen", $"{Loader} 5.4.2333 dependency"], Entries(mods.GetProperty("active")));

        // A third set: the first goes, with the zips only it used.
        await SyncAsync(client, "mods-2", new { package = "ValheimModding-JsonDotNET" });
        var paths = InstancePaths.For(factory.DataRoot, "mods-2");
        mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-2/mods", Ct);
        var thirdSet = mods.GetProperty("active").GetProperty("id").GetInt64();
        Assert.Equal(secondSet, mods.GetProperty("previous").GetProperty("id").GetInt64());
        Assert.Equal(
            [.. new[] { secondSet, thirdSet }.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)],
            Directory.GetDirectories(Path.Combine(paths.Mods, "sets")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "sets", firstSet.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.False(File.Exists(Path.Combine(paths.Mods, "cache", "RandyKnapp-EpicLoot-0.15.4.zip")));
        Assert.True(File.Exists(Path.Combine(paths.Mods, "cache", "ValheimModding-Jotunn-2.30.2.zip")));
        await using (var db = await Db())
        {
            Assert.False(await db.ModSets.AnyAsync(set => set.Id == firstSet, Ct));
            Assert.False(await db.ModEntries.AnyAsync(entry => entry.SetId == firstSet, Ct));
        }

        // Back one step, and forward again: only the pointers move.
        var rollback = await client.PostAsync("/api/v1/instances/mods-2/mods/rollback", null, Ct);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        var rolledBack = await rollback.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(secondSet, rolledBack.GetProperty("active").GetProperty("id").GetInt64());
        Assert.Equal(thirdSet, rolledBack.GetProperty("previous").GetProperty("id").GetInt64());
        using (var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(paths.Mods, ModsService.MetadataFile), Ct)))
        {
            Assert.Equal(secondSet, metadata.RootElement.GetProperty("setId").GetInt64());
        }

        var again = await (await client.PostAsync("/api/v1/instances/mods-2/mods/rollback", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(thirdSet, again.GetProperty("active").GetProperty("id").GetInt64());
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(paths.Mods, "sets")).Length);
    }

    [Fact]
    public async Task While_a_mods_job_runs_another_a_rollback_and_a_purge_are_refused()
    {
        using var client = await ClientWithInstanceAsync("mods-3");
        factory.Thunderstore.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string jobId;
        try
        {
            var started = await client.PutAsJsonAsync("/api/v1/instances/mods-3/mods", new { entries = new[] { new { package = "ValheimModding-JsonDotNET" } } }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
            jobId = (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("jobId").GetString()!;
            await WaitForStageAsync(client, jobId, "download");
            var building = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-3/mods", Ct);
            Assert.Equal("building", building.GetProperty("pending").GetProperty("state").GetString());
            Assert.Equal(jobId, building.GetProperty("pending").GetProperty("jobId").GetString());

            var second = await client.PutAsJsonAsync("/api/v1/instances/mods-3/mods", new { entries = Array.Empty<object>() }, Ct);
            var problem = await AssertProblemAsync(second, HttpStatusCode.Conflict, "mods_busy");
            Assert.Contains(jobId, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
            await AssertProblemAsync(await client.PostAsync("/api/v1/instances/mods-3/mods/rollback", null, Ct), HttpStatusCode.Conflict, "mods_busy");
            await AssertProblemAsync(await client.DeleteAsync("/api/v1/instances/mods-3?mode=purge&confirm=mods-3", Ct), HttpStatusCode.Conflict, "mods_busy");
        }
        finally
        {
            factory.Thunderstore.HoldDownloads.TrySetResult();
            factory.Thunderstore.HoldDownloads = null;
        }

        Assert.Equal("succeeded", (await WaitForJobAsync(client, jobId)).GetProperty("state").GetString());
        var purged = await client.DeleteAsync("/api/v1/instances/mods-3?mode=purge&confirm=mods-3", Ct);
        Assert.Equal(HttpStatusCode.OK, purged.StatusCode);
        await using var db = await Db();
        Assert.False(await db.InstanceMods.AnyAsync(row => row.InstanceId == "mods-3", Ct));
        Assert.False(await db.ModSets.AnyAsync(set => set.InstanceId == "mods-3", Ct));
    }

    [Fact]
    public async Task A_download_must_match_the_hash_recorded_at_its_first_download()
    {
        factory.Thunderstore.Add("Tester-Tampered", "1.0.0", [], ("Tampered.dll", "original"));
        using var client = await ClientWithInstanceAsync("mods-4");
        var first = await SyncAsync(client, "mods-4", new { package = "Tester-Tampered" });
        Assert.Contains(await LogAsync(client, first), line => line.Contains("Tester-Tampered-1.0.0: first download", StringComparison.Ordinal));

        // Another instance downloads the same bytes: they match the trusted hash.
        await CreateInstanceAsync(client, "mods-5");
        var matching = await SyncAsync(client, "mods-5", new { package = "Tester-Tampered" });
        Assert.Equal("succeeded", matching.GetProperty("state").GetString());
        Assert.DoesNotContain(await LogAsync(client, matching), line => line.Contains("Tester-Tampered-1.0.0: first download", StringComparison.Ordinal));

        // Thunderstore now serves other bytes for that version: refused, and the active set stays.
        await CreateInstanceAsync(client, "mods-6");
        await SyncAsync(client, "mods-6", new { package = "ValheimModding-JsonDotNET" });
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-6/mods", Ct);
        factory.Thunderstore.ReplaceZip("Tester-Tampered", "1.0.0", FakeThunderstore.Zip(("manifest.json", "{}"), ("Tampered.dll", "replaced")));

        var refused = await SyncAsync(client, "mods-6", new { package = "Tester-Tampered" });

        Assert.Equal("failed", refused.GetProperty("state").GetString());
        Assert.Contains("Tester-Tampered-1.0.0: the download's SHA-256", refused.GetProperty("message").GetString(), StringComparison.Ordinal);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-6/mods", Ct);
        Assert.Equal(before.GetProperty("active").GetProperty("id").GetInt64(), after.GetProperty("active").GetProperty("id").GetInt64());
        var pending = after.GetProperty("pending");
        Assert.Equal("failed", pending.GetProperty("state").GetString());
        Assert.Equal(refused.GetProperty("id").GetString(), pending.GetProperty("jobId").GetString());
        var paths = InstancePaths.For(factory.DataRoot, "mods-6");
        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "sets", pending.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.False(File.Exists(Path.Combine(paths.Mods, "cache", "Tester-Tampered-1.0.0.zip")));
    }

    [Fact]
    public async Task A_zip_entry_that_leaves_the_set_folder_fails_the_build()
    {
        factory.Thunderstore.Add("Tester-Slippery", "1.0.0", [], ("Slippery.dll", "fine"), ("../../../escaped.txt", "not fine"));
        using var client = await ClientWithInstanceAsync("mods-7");

        var job = await SyncAsync(client, "mods-7", new { package = "Tester-Slippery" });

        Assert.Equal("failed", job.GetProperty("state").GetString());
        Assert.Contains("Tester-Slippery-1.0.0: refused the zip entry '../../../escaped.txt'", job.GetProperty("message").GetString(), StringComparison.Ordinal);
        var paths = InstancePaths.For(factory.DataRoot, "mods-7");
        Assert.Empty(Directory.GetDirectories(Path.Combine(paths.Mods, "sets")));
        Assert.False(File.Exists(Path.Combine(paths.Mods, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(paths.Root, "escaped.txt")));
    }

    [Fact]
    public async Task An_unknown_version_fails_the_job_with_its_name()
    {
        using var client = await ClientWithInstanceAsync("mods-8");

        var job = await SyncAsync(client, "mods-8", new { package = "RandyKnapp-EpicLoot", version = "0.0.1" });

        Assert.Equal("failed", job.GetProperty("state").GetString());
        Assert.Equal("mods.sync failed: RandyKnapp-EpicLoot 0.0.1 was not found on Thunderstore.", job.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Bad_requests_unknown_instances_and_adapters_without_mods_are_refused()
    {
        using var client = await ClientWithInstanceAsync("mods-9");
        var created = await client.PostAsJsonAsync("/api/v1/instances", new { id = "mods-vanilla", adapterId = "fakegame" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var badName = await client.PutAsJsonAsync("/api/v1/instances/mods-9/mods", new { entries = new[] { new { package = "../EpicLoot" } } }, Ct);
        await AssertProblemAsync(badName, HttpStatusCode.BadRequest, "validation_failed");
        var twice = await client.PutAsJsonAsync(
            "/api/v1/instances/mods-9/mods",
            new { entries = new[] { new { package = "RandyKnapp-EpicLoot" }, new { package = "RandyKnapp-EpicLoot" } } },
            Ct);
        await AssertProblemAsync(twice, HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/nope/mods", Ct), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await client.GetAsync("/api/v1/instances/mods-vanilla/mods", Ct), HttpStatusCode.Conflict, "invalid_state");
        await AssertProblemAsync(
            await client.PutAsJsonAsync("/api/v1/instances/mods-vanilla/mods", new { entries = Array.Empty<object>() }, Ct),
            HttpStatusCode.Conflict,
            "invalid_state");

        var adapters = await client.GetFromJsonAsync<JsonElement>("/api/v1/adapters", Ct);
        var modded = adapters.GetProperty("adapters").EnumerateArray().Single(adapter => adapter.GetProperty("id").GetString() == "fakemods");
        Assert.Equal(FakeModLoader.LoaderId, modded.GetProperty("modLoader").GetString());
        Assert.Equal("L3", modded.GetProperty("level").GetString());
        var vanilla = adapters.GetProperty("adapters").EnumerateArray().Single(adapter => adapter.GetProperty("id").GetString() == "fakegame");
        Assert.Equal(JsonValueKind.Null, vanilla.GetProperty("modLoader").ValueKind);
    }

    [Fact]
    public async Task A_mods_job_left_running_by_a_previous_core_fails_with_its_set()
    {
        using var client = await ClientWithInstanceAsync("mods-10");
        var paths = InstancePaths.For(factory.DataRoot, "mods-10");
        long setId;
        await using (var db = await Db())
        {
            db.Jobs.Add(new JobRecord { Id = "interrupted-1", Type = ModsService.JobType, InstanceId = "mods-10", State = JobState.Running, Stage = "build", CreatedAt = DateTimeOffset.UtcNow });
            var set = new ModSetRecord { InstanceId = "mods-10", Origin = ModsService.EditOrigin, State = ModSetState.Building, JobId = "interrupted-1", CreatedAt = DateTimeOffset.UtcNow };
            db.ModSets.Add(set);
            await db.SaveChangesAsync(Ct);
            setId = set.Id;
        }

        var setFolder = Path.Combine(paths.Mods, "sets", setId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(setFolder, "plugins"));
        var downloads = Path.Combine(factory.DataRoot, "tmp", "mods", "interrupted-1");
        Directory.CreateDirectory(downloads);

        await factory.Services.GetRequiredService<ModsService>().FailInterruptedAsync(Ct);

        var job = await client.GetFromJsonAsync<JsonElement>("/api/v1/jobs/interrupted-1", Ct);
        Assert.Equal("failed", job.GetProperty("state").GetString());
        Assert.Contains("Core restarted", job.GetProperty("message").GetString(), StringComparison.Ordinal);
        var mods = await client.GetFromJsonAsync<JsonElement>("/api/v1/instances/mods-10/mods", Ct);
        Assert.Equal("failed", mods.GetProperty("pending").GetProperty("state").GetString());
        Assert.False(Directory.Exists(setFolder));
        Assert.False(Directory.Exists(downloads));
    }

    private async Task<HttpClient> ClientWithInstanceAsync(string id)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CliToken());
        await AddModdedAdapterAsync(client);
        await CreateInstanceAsync(client, id);
        return client;
    }

    private static async Task CreateInstanceAsync(HttpClient client, string id)
    {
        var created = await client.PostAsJsonAsync("/api/v1/instances", new { id, adapterId = "fakemods" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    /// <summary>An L3 Owner adapter whose mods use the fake loader and Thunderstore's Valheim community.</summary>
    private async Task AddModdedAdapterAsync(HttpClient client)
    {
        var folder = Path.Combine(factory.DataRoot, "adapters", "fakemods");
        if (Directory.Exists(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "adapter.json"), $$"""
            {
              "schemaVersion": 1, "id": "fakemods", "name": "FakeGame with mods", "version": "1.0.0", "level": "L3",
              "source": { "type": "manual" },
              "launch": { "exe": "{{CoreHarness.FakeGameExe.Replace("\\", "\\\\", StringComparison.Ordinal)}}", "args": ["--world", "{paths.data}/world.txt"] },
              "ports": [],
              "readiness": { "type": "logRegex", "source": "stdout", "pattern": "FakeGame server ready", "timeoutSec": 60 },
              "stop": { "method": "ctrl-c", "timeoutSec": 20 },
              "backup": { "paths": ["{paths.data}"], "mode": "stop" },
              "resources": { "min": { "cpuCores": 1, "ramMb": 1, "diskMb": 1 } },
              "mods": {
                "loader": "{{FakeModLoader.LoaderId}}",
                "loaderPackage": "{{Loader}}",
                "sources": [{ "type": "thunderstore", "community": "valheim" }]
              }
            }
            """, Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "settings.schema.json"), """
            { "groups": [{ "id": "server", "label": "Server" }], "settings": [{ "key": "name", "type": "string", "group": "server", "default": "Fake" }] }
            """, Ct);
        var reload = await client.PostAsync("/api/v1/adapters/reload", null, Ct);
        var result = await reload.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(result.GetProperty("problems").GetArrayLength() == 0, result.ToString());
    }

    /// <summary>Starts a <c>mods.sync</c> with these chosen mods and waits for it to finish.</summary>
    private static async Task<JsonElement> SyncAsync(HttpClient client, string id, params object[] entries)
    {
        var started = await client.PutAsJsonAsync($"/api/v1/instances/{id}/mods", new { entries }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var body = await started.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(ModsService.JobType, body.GetProperty("job").GetProperty("type").GetString());
        Assert.Equal($"/api/v1/jobs/{body.GetProperty("jobId").GetString()}", started.Headers.Location?.ToString());
        return await WaitForJobAsync(client, body.GetProperty("jobId").GetString()!);
    }

    private static async Task<JsonElement> WaitForJobAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow + CoreHarness.Timeout;
        JsonElement job;
        do
        {
            job = await client.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{jobId}", Ct);
            if (job.GetProperty("state").GetString() is "succeeded" or "failed")
            {
                return job;
            }

            await Task.Delay(50, Ct);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Job {jobId} did not finish: {job}");
    }

    private static async Task WaitForStageAsync(HttpClient client, string jobId, string stage)
    {
        var deadline = DateTime.UtcNow + CoreHarness.Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var job = await client.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{jobId}", Ct);
            if (job.GetProperty("stage").GetString() == stage)
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"Job {jobId} never reached {stage}.");
    }

    private static async Task<string[]> LogAsync(HttpClient client, JsonElement job)
    {
        var log = await client.GetFromJsonAsync<JsonElement>($"/api/v1/jobs/{job.GetProperty("id").GetString()}/log", Ct);
        return [.. log.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!)];
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {problem}");
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static string[] Entries(JsonElement set) =>
    [
        .. set.GetProperty("entries").EnumerateArray().Select(entry =>
            $"{entry.GetProperty("package").GetString()} {entry.GetProperty("version").GetString()} {entry.GetProperty("origin").GetString()}"),
    ];

    private Task<GameWardenDb> Db() => factory.Services.GetRequiredService<IDbContextFactory<GameWardenDb>>().CreateDbContextAsync(Ct);
}
