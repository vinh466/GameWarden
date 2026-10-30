using System.Net;
using GameWarden.Core.Mods;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GameWarden.Core.Tests.Unit;

/// <summary>
/// Thunderstore search (docs/13 §4.4 item 3): the package list streamed into a compact index, the ranking, the 24 h
/// cache in memory and on disk, one download at a time, and a failed download that keeps the old cache.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ThunderstoreSearchTests : IDisposable
{
    private static readonly ThunderstorePolicy NoDelays = new([TimeSpan.Zero, TimeSpan.Zero], TimeSpan.Zero, TimeSpan.FromSeconds(30));

    private const string ListUrl = "/c/valheim/api/v1/package/";

    private readonly FakeThunderstore _thunderstore = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "search-" + Guid.NewGuid().ToString("N")[..10]);

    public ThunderstoreSearchTests()
    {
        _thunderstore.PackageLists["valheim"] =
        [
            FakeThunderstore.ListedPackage("RandyKnapp-EpicLoot", "Loot drops with magic effects, like Diablo.", [16952, 120000, 300000], categories: ["Gear", "Server-side"]),
            FakeThunderstore.ListedPackage("ValheimModding-Jotunn", "Jötunn, the Valheim Library.", [5000, 900000]),
            FakeThunderstore.ListedPackage("Someone-EpicLootAddon", "Adds sets to Epic Loot.", [700]),
            FakeThunderstore.ListedPackage("Other-MoreEpicLoot", "More of it.", [90000]),
            FakeThunderstore.ListedPackage("Old-EpicLootFork", "A fork of Epic Loot.", [2000000], deprecated: true),
            FakeThunderstore.ListedPackage("Lewd-EpicLootSkins", "Epic Loot skins.", [9000000], nsfw: true),
            FakeThunderstore.ListedPackage("RandyKnapp-AugmentedLoot", "Made by the author of loot mods.", [100]),
        ];
        Directory.CreateDirectory(_dataRoot);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ThunderstoreSearch Search() => new(
        new ThunderstoreClient(new FakeHttpClientFactory(_thunderstore), NoDelays, _time, NullLogger<ThunderstoreClient>.Instance),
        Options.Create(new GameWardenOptions { DataRoot = _dataRoot }),
        _time,
        NullLogger<ThunderstoreSearch>.Instance);

    [Fact]
    public async Task The_gzipped_list_becomes_a_compact_index_saved_in_cores_cache_folder()
    {
        var search = Search();

        var index = await search.IndexAsync("valheim", Ct);

        Assert.Equal(_time.GetUtcNow(), index.FetchedAt);
        Assert.Equal(7, index.Packages.Count);
        var epicLoot = Assert.Single(index.Packages, package => package.Package == "RandyKnapp-EpicLoot");
        Assert.Equal(
            new ModSearchEntry(
                "RandyKnapp-EpicLoot",
                "EpicLoot",
                "RandyKnapp",
                "1.0.2",
                "Loot drops with magic effects, like Diablo.",
                epicLoot.Categories,
                Deprecated: false,
                Nsfw: false,
                Downloads: 436952,
                Rating: 12,
                "https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/"),
            epicLoot);
        Assert.Equal(["Gear", "Server-side"], epicLoot.Categories);
        Assert.Equal(Path.Combine(_dataRoot, "cache", "thunderstore", "valheim.json"), search.CacheFile("valheim"));
        Assert.True(File.Exists(search.CacheFile("valheim")));
        Assert.False(File.Exists(search.CacheFile("valheim") + ".tmp"));
        Assert.Equal(1, _thunderstore.Count(ListUrl));
    }

    [Fact]
    public async Task Packages_core_cannot_read_are_skipped()
    {
        var broken = FakeThunderstore.ListedPackage("Broken-NoVersions", "none", []);
        var badName = FakeThunderstore.ListedPackage("Fine-Name", "bad", [1]);
        badName["full_name"] = "not a package";
        _thunderstore.PackageLists["valheim"] = [broken, badName, FakeThunderstore.ListedPackage("Good-One", "fine", [3])];

        var index = await Search().IndexAsync("valheim", Ct);

        Assert.Equal(["Good-One"], index.Packages.Select(package => package.Package));
    }

    [Fact]
    public async Task Search_ranks_exact_names_then_prefixes_then_names_owners_and_descriptions_and_leaves_nsfw_out()
    {
        var index = await Search().IndexAsync("valheim", Ct);

        // Exact name, then name prefixes (the deprecated one last, despite its downloads), then names that contain it.
        Assert.Equal(
            ["RandyKnapp-EpicLoot", "Someone-EpicLootAddon", "Old-EpicLootFork", "Other-MoreEpicLoot"],
            Names(ThunderstoreSearch.Search(index.Packages, "epic loot", 10)));
        Assert.Equal(["RandyKnapp-EpicLoot"], Names(ThunderstoreSearch.Search(index.Packages, "  RandyKnapp-EpicLoot ", 1)));
        Assert.Equal(["RandyKnapp-EpicLoot", "RandyKnapp-AugmentedLoot"], Names(ThunderstoreSearch.Search(index.Packages, "randyknapp", 10)));
        Assert.Equal(["ValheimModding-Jotunn"], Names(ThunderstoreSearch.Search(index.Packages, "VALHEIM LIBRARY", 10)));
        Assert.Empty(ThunderstoreSearch.Search(index.Packages, "skins", 10));
        Assert.Empty(ThunderstoreSearch.Search(index.Packages, "   ", 10));
        Assert.Equal(2, ThunderstoreSearch.Search(index.Packages, "loot", 2).Count);

        static string[] Names(IEnumerable<ModSearchEntry> entries) => [.. entries.Select(entry => entry.Package)];
    }

    [Fact]
    public async Task The_list_is_downloaded_again_after_a_day_while_searches_use_the_old_one()
    {
        var search = Search();
        var first = await search.IndexAsync("valheim", Ct);

        _time.Advance(TimeSpan.FromHours(23));
        Assert.Same(first, await search.IndexAsync("valheim", Ct));
        Assert.Equal(1, _thunderstore.Count(ListUrl));

        // A day old: the search answers from the old list at once and a new one downloads behind it.
        _time.Advance(TimeSpan.FromHours(2));
        _thunderstore.PackageLists["valheim"] = [FakeThunderstore.ListedPackage("New-Mod", "new", [1])];
        _thunderstore.HoldPackageLists = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Same(first, await search.IndexAsync("valheim", Ct));
        Assert.Same(first, await search.IndexAsync("valheim", Ct));
        _thunderstore.HoldPackageLists.SetResult();
        await search.WaitForDownloadAsync("valheim");

        var second = await search.IndexAsync("valheim", Ct);
        Assert.Equal(["New-Mod"], second.Packages.Select(package => package.Package));
        Assert.Equal(_time.GetUtcNow(), second.FetchedAt);
        Assert.Equal(2, _thunderstore.Count(ListUrl));
    }

    [Fact]
    public async Task The_cache_file_serves_a_new_core_for_a_day()
    {
        await Search().IndexAsync("valheim", Ct);
        _time.Advance(TimeSpan.FromHours(3));

        var afterRestart = await Search().IndexAsync("valheim", Ct);

        Assert.Equal(7, afterRestart.Packages.Count);
        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromHours(3), afterRestart.FetchedAt);
        Assert.Equal(1, _thunderstore.Count(ListUrl));
    }

    [Fact]
    public async Task Concurrent_searches_share_one_download()
    {
        var search = Search();
        _thunderstore.HoldPackageLists = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var searches = Enumerable.Range(0, 8).Select(_ => search.IndexAsync("valheim", Ct)).ToList();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (_thunderstore.Count(ListUrl) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        Assert.All(searches, task => Assert.False(task.IsCompleted));
        _thunderstore.HoldPackageLists.SetResult();
        var indexes = await Task.WhenAll(searches);

        Assert.All(indexes, index => Assert.Same(indexes[0], index));
        Assert.Equal(1, _thunderstore.Count(ListUrl));
    }

    [Fact]
    public async Task A_failed_download_keeps_the_old_cache_and_waits_before_trying_again()
    {
        var search = Search();
        var first = await search.IndexAsync("valheim", Ct);
        _time.Advance(TimeSpan.FromDays(2));
        _thunderstore.Intercept = request => request.RequestUri!.AbsolutePath == ListUrl ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;

        Assert.Same(first, await search.IndexAsync("valheim", Ct));
        await search.WaitForDownloadAsync("valheim");
        var tries = _thunderstore.Count(ListUrl);
        Assert.Equal(1 + NoDelays.Attempts, tries);

        // Within the pause after a failure, searches use the old list without asking Thunderstore.
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Same(first, await search.IndexAsync("valheim", Ct));
        Assert.Equal(tries, _thunderstore.Count(ListUrl));

        // After it, the next search tries again; Thunderstore answers.
        _time.Advance(TimeSpan.FromMinutes(2));
        _thunderstore.Intercept = null;
        Assert.Same(first, await search.IndexAsync("valheim", Ct));
        await search.WaitForDownloadAsync("valheim");
        Assert.NotSame(first, await search.IndexAsync("valheim", Ct));
    }

    [Fact]
    public async Task Without_a_cache_a_failed_download_is_an_error_that_names_thunderstore()
    {
        _thunderstore.Intercept = request => request.RequestUri!.AbsolutePath == ListUrl ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null;
        var search = Search();

        var error = await Assert.ThrowsAsync<ThunderstoreException>(() => search.IndexAsync("valheim", Ct));

        Assert.Contains("Thunderstore answered 502 for the package list of valheim", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(search.CacheFile("valheim")));
        await Assert.ThrowsAsync<ThunderstoreException>(() => search.IndexAsync("../etc", Ct));
    }
}
