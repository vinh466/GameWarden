using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GameWarden.Core.Hosting;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Mods;

/// <summary>A package of a community's list as search keeps it (docs/13 §4.4 item 3): its latest version only.</summary>
/// <param name="Package"><c>Namespace-Name</c>.</param>
/// <param name="Url">Its page on Thunderstore, in the community.</param>
public sealed record ModSearchEntry(
    string Package,
    string Name,
    string Owner,
    string Version,
    string Description,
    IReadOnlyList<string> Categories,
    bool Deprecated,
    bool Nsfw,
    long Downloads,
    int Rating,
    string Url);

/// <summary>The compact index of a community's package list, as downloaded at <see cref="FetchedAt"/>.</summary>
public sealed record ThunderstoreIndex(string Community, DateTimeOffset FetchedAt, IReadOnlyList<ModSearchEntry> Packages);

/// <summary>
/// Thunderstore search (docs/13 §4.4 item 3, §2 answer 8). A search downloads the community's package list
/// (<see cref="ThunderstoreClient.GetPackageListAsync{T}"/>) when its cache is missing or older than <see cref="MaxAge"/>,
/// parses it as it arrives into a <see cref="ModSearchEntry"/> per package, keeps that in memory and saves it to
/// <c>&lt;data root&gt;\cache\thunderstore\&lt;community&gt;.json</c>, a Core-only folder. One download runs per
/// community: a search with an old cache answers from it while the new list downloads; one without waits for that
/// download. A failed download keeps the old cache, logs a warning and is not tried again for
/// <see cref="RetryAfterFailure"/>; adding a mod by its exact name never needs the list.
/// </summary>
public sealed class ThunderstoreSearch(ThunderstoreClient client, IOptions<GameWardenOptions> options, TimeProvider time, ILogger<ThunderstoreSearch> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions ListJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, Community> _communities = new(StringComparer.Ordinal);

    /// <summary>The cache file of a community.</summary>
    public string CacheFile(string community) => Path.Combine(options.Value.CachePath, "thunderstore", community + ".json");

    /// <summary>
    /// The community's index: from memory or the cache file while it is younger than <see cref="MaxAge"/>; otherwise as
    /// described on the class. Throws <see cref="ThunderstoreException"/> when there is no cache and the download failed.
    /// </summary>
    public async Task<ThunderstoreIndex> IndexAsync(string community, CancellationToken cancellationToken)
    {
        if (!ThunderstoreClient.IsCommunity(community))
        {
            throw new ThunderstoreException($"'{community}' is not a Thunderstore community id.");
        }

        var state = _communities.GetOrAdd(community, name => new Community(new Lazy<Task<ThunderstoreIndex?>>(() => Task.Run(() => LoadAsync(name)))));
        var fromDisk = await state.Disk.Value.WaitAsync(cancellationToken);
        Task<ThunderstoreIndex?> download;
        lock (state)
        {
            state.Index ??= fromDisk;
            var now = time.GetUtcNow();
            if (state.Index is { } fresh && now - fresh.FetchedAt < MaxAge)
            {
                return fresh;
            }

            if (state.Download is null)
            {
                if (state.FailedAt is { } failedAt && now - failedAt < RetryAfterFailure)
                {
                    return state.Index ?? throw new ThunderstoreException(state.Failure ?? "The package list could not be downloaded.");
                }

                state.Download = Task.Run(() => DownloadAsync(community, state));
            }

            if (state.Index is { } old)
            {
                return old;
            }

            download = state.Download;
        }

        return await download.WaitAsync(cancellationToken)
            ?? throw new ThunderstoreException(state.Failure ?? "The package list could not be downloaded.");
    }

    /// <summary>Waits for a download of the community's list that is running now, if any (tests).</summary>
    internal Task WaitForDownloadAsync(string community)
    {
        if (!_communities.TryGetValue(community, out var state))
        {
            return Task.CompletedTask;
        }

        lock (state)
        {
            return state.Download ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Search (docs/13 §4.4 item 3): a case-insensitive match on the name, then the owner, then the description. An exact
    /// name (or <c>Namespace-Name</c>) comes first, then names that start with the text, names that contain it, owners
    /// and descriptions; within each, packages that are not deprecated first, then by downloads. Names are compared
    /// without spaces, dashes and underscores, so "epic loot" finds <c>EpicLoot</c>. NSFW packages are left out.
    /// </summary>
    public static IReadOnlyList<ModSearchEntry> Search(IReadOnlyList<ModSearchEntry> packages, string query, int limit)
    {
        var text = query.Trim();
        var compact = Compact(text);
        if (text.Length == 0)
        {
            return [];
        }

        return
        [
            .. packages
                .Where(package => !package.Nsfw)
                .Select(package => (Package: package, Rank: Rank(package, text, compact)))
                .Where(match => match.Rank >= 0)
                .OrderBy(match => match.Rank)
                .ThenBy(match => match.Package.Deprecated)
                .ThenByDescending(match => match.Package.Downloads)
                .ThenBy(match => match.Package.Package, StringComparer.Ordinal)
                .Take(limit)
                .Select(match => match.Package),
        ];
    }

    /// <summary>
    /// Reads Thunderstore's v1 package list as a stream of packages, never the whole document at once: per package its
    /// <c>full_name</c>, <c>owner</c>, <c>name</c>, <c>package_url</c>, flags, categories and rating, and from its versions
    /// (newest first) the first one's number and description and the sum of their downloads. Packages whose name or
    /// version Core cannot read are skipped.
    /// </summary>
    public static async Task<List<ModSearchEntry>> ReadListAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<ModSearchEntry> entries = [];
        await foreach (var package in JsonSerializer.DeserializeAsyncEnumerable<ListedPackage>(stream, ListJson, cancellationToken))
        {
            if (package is not { FullName: { } fullName, Versions: [{ VersionNumber: { } version } latest, ..] versions }
                || !Packages.IsPackage(fullName)
                || !Packages.IsVersion(version))
            {
                continue;
            }

            var dash = fullName.IndexOf('-', StringComparison.Ordinal);
            var owner = package.Owner ?? fullName[..dash];
            var name = package.Name ?? fullName[(dash + 1)..];
            entries.Add(new ModSearchEntry(
                fullName,
                name,
                owner,
                version,
                latest.Description ?? string.Empty,
                package.Categories ?? [],
                package.IsDeprecated,
                package.HasNsfwContent,
                versions.Sum(entry => Math.Max(0, entry.Downloads)),
                package.RatingScore,
                package.PackageUrl ?? $"https://thunderstore.io/package/{owner}/{name}/"));
        }

        return entries;
    }

    private static int Rank(ModSearchEntry package, string text, string compact)
    {
        if (compact.Length > 0)
        {
            var name = Compact(package.Name);
            if (name == compact || string.Equals(package.Package, text, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.StartsWith(compact, StringComparison.Ordinal))
            {
                return 1;
            }

            if (name.Contains(compact, StringComparison.Ordinal))
            {
                return 2;
            }
        }

        return package.Owner.Contains(text, StringComparison.OrdinalIgnoreCase) ? 3
            : package.Description.Contains(text, StringComparison.OrdinalIgnoreCase) ? 4
            : -1;
    }

    private static string Compact(string text) =>
        string.Concat(text.Where(c => c is not (' ' or '_' or '-')).Select(char.ToLowerInvariant));

    private async Task<ThunderstoreIndex?> DownloadAsync(string community, Community state)
    {
        var watch = Stopwatch.StartNew();
        ThunderstoreIndex? index = null;
        string? failure = null;
        try
        {
            var packages = await client.GetPackageListAsync(community, ReadListAsync, CancellationToken.None);
            index = new ThunderstoreIndex(community, time.GetUtcNow(), packages);
            logger.LogInformation("Thunderstore package list of {Community}: {Count} package(s) in {Seconds:0.0} s.", community, packages.Count, watch.Elapsed.TotalSeconds);
        }
        catch (ThunderstoreException ex)
        {
            failure = ex.Message;
            logger.LogWarning("Downloading Thunderstore's package list of {Community} failed: {Message} Search keeps the cache it has.", community, ex.Message);
        }

        if (index is not null)
        {
            try
            {
                await SaveAsync(index);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not save the package list of {Community}; it is kept in memory.", community);
            }
        }

        lock (state)
        {
            if (index is not null)
            {
                state.Index = index;
                state.FailedAt = null;
                state.Failure = null;
            }
            else
            {
                state.FailedAt = time.GetUtcNow();
                state.Failure = failure;
            }

            state.Download = null;
        }

        return index;
    }

    private async Task SaveAsync(ThunderstoreIndex index)
    {
        var path = CacheFile(index.Community);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(file, index, CacheJson);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private async Task<ThunderstoreIndex?> LoadAsync(string community)
    {
        var path = CacheFile(community);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var index = await JsonSerializer.DeserializeAsync<ThunderstoreIndex>(file, CacheJson);
            return index is { Packages: not null } && index.Community == community ? index : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "The cached package list {Path} cannot be read; it is downloaded again.", LogText.Clean(path));
            return null;
        }
    }

    /// <summary>One community's cache; its fields change under its own lock.</summary>
    private sealed class Community(Lazy<Task<ThunderstoreIndex?>> disk)
    {
        public Lazy<Task<ThunderstoreIndex?>> Disk { get; } = disk;

        public ThunderstoreIndex? Index { get; set; }

        public Task<ThunderstoreIndex?>? Download { get; set; }

        public DateTimeOffset? FailedAt { get; set; }

        public string? Failure { get; set; }
    }

    private sealed record ListedPackage(
        string? Name,
        string? FullName,
        string? Owner,
        string? PackageUrl,
        int RatingScore,
        bool IsDeprecated,
        bool HasNsfwContent,
        List<string>? Categories,
        List<ListedVersion>? Versions);

    private sealed record ListedVersion(string? VersionNumber, string? Description, long Downloads);
}
