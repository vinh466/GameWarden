using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameWarden.Core.Adapters;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Isolation;
using GameWarden.Core.Jobs;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Mods;

/// <summary>The answer to a read or a rollback: the instance's mods, or why not.</summary>
public sealed record ModsResult(ModsView? Mods, string? Error, string? Code, int StatusCode);

/// <summary>A step of a mods job failed in a way the message explains, naming the package.</summary>
public sealed class ModSyncException(string message) : Exception(message);

/// <summary>
/// An instance's mod sets (docs/13 §4.5, ADR-0020). The <c>mods.sync</c> job resolves the chosen mods, downloads and
/// verifies their zips, builds <c>{paths.mods}\sets\&lt;id&gt;\</c> and makes it the active set. It never stops or
/// starts the server and does not take the instance lock: the set takes effect at the next start. One mods job runs per
/// instance (<see cref="ModJobGate"/>). Instance files are touched only through <see cref="IInstanceFiles"/>,
/// with synchronous bodies (ADR-0009 §3); downloads land first in Core's own <c>tmp\mods\&lt;job id&gt;\</c>.
/// </summary>
public sealed class ModsService(
    SupervisorContext context,
    Supervisor supervisor,
    JobService jobs,
    ThunderstoreClient thunderstore,
    IEnumerable<IModLoader> loaders,
    ModJobGate gate,
    ILogger<ModsService> logger)
{
    public const string JobType = "mods.sync";

    /// <summary>Origin of a set made from <c>PUT /mods</c>.</summary>
    public const string EditOrigin = "edit";

    /// <summary><c>{paths.mods}\mods.json</c>: a copy of the active set for people and manual backups (docs/13 §4.2).</summary>
    public const string MetadataFile = "mods.json";

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);

    /// <summary>True while any mods job runs.</summary>
    public bool AnyRunning => !_running.IsEmpty;

    /// <summary>Waits for a mods job started by this Core (tests).</summary>
    public Task WaitAsync(string jobId) => _running.GetValueOrDefault(jobId) ?? Task.CompletedTask;

    /// <summary>
    /// Core start-up: a mods job a previous Core left running failed, and its <c>building</c> set with it; the set's
    /// folder and the job's downloads are removed where possible.
    /// </summary>
    public async Task FailInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var sets = await db.ModSets.Where(set => set.State == ModSetState.Building).ToListAsync(cancellationToken);
        var interrupted = await db.Jobs
            .Where(job => job.Type == JobType && (job.State == JobState.Queued || job.State == JobState.Running))
            .ToListAsync(cancellationToken);
        foreach (var set in sets)
        {
            set.State = ModSetState.Failed;
        }

        foreach (var job in interrupted)
        {
            job.State = JobState.Failed;
            job.Message = "Core restarted while this job was running; run it again.";
            job.FinishedAt = context.Time.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            context.Events.RaiseJobChanged(job);
        }

        foreach (var set in sets)
        {
            logger.LogWarning("Mod set {Set} of {Instance} was interrupted by a Core restart.", set.Id, set.InstanceId);
            TryDeleteSetFolder(SetFolder(InstancePaths.For(context.Options.DataRoot, set.InstanceId), set.Id), null);
        }

        TryDeleteDownloads(Path.Combine(context.Options.TempPath, "mods"));
    }

    public async Task<ModsResult> GetAsync(string instanceId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return problem!;
        }

        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        return new ModsResult(await ViewAsync(db, target, cancellationToken), null, null, StatusCodes.Status200OK);
    }

    /// <summary><c>PUT /mods</c>: a new set from the whole list of chosen mods, built by a <c>mods.sync</c> job.</summary>
    public async Task<JobStartResult> StartSyncAsync(string instanceId, IReadOnlyList<ModEntryRequest>? entries, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return new JobStartResult(null, problem!.Error, problem.Code, problem.StatusCode);
        }

        if (Invalid(entries) is { } invalid)
        {
            return new JobStartResult(null, invalid, ProblemCodes.ValidationFailed, StatusCodes.Status400BadRequest);
        }

        var job = new JobRecord
        {
            Id = jobs.NewJobId(),
            Type = JobType,
            InstanceId = instanceId,
            State = JobState.Queued,
            CreatedAt = context.Time.GetUtcNow(),
        };
        if (!gate.TryEnter(instanceId, job.Id))
        {
            return new JobStartResult(null, gate.BusyMessage(instanceId), ProblemCodes.ModsBusy, StatusCodes.Status409Conflict);
        }

        ModSetRecord set;
        try
        {
            if (supervisor.Find(instanceId) is null)
            {
                gate.Exit(instanceId);
                return new JobStartResult(null, $"No instance '{instanceId}'.", "not_found", StatusCodes.Status404NotFound);
            }

            await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
            await RowAsync(db, instanceId, cancellationToken);

            set = new ModSetRecord
            {
                InstanceId = instanceId,
                CreatedAt = job.CreatedAt,
                Origin = EditOrigin,
                State = ModSetState.Building,
                JobId = job.Id,
            };
            db.Jobs.Add(job);
            db.ModSets.Add(set);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            gate.Exit(instanceId);
            throw;
        }

        context.Events.RaiseJobChanged(job);
        _running[job.Id] = Task.Run(() => RunSyncAsync(job, set.Id, target, entries!));
        logger.LogInformation("Job {Job}: {Type} of {Instance} queued (set {Set}).", job.Id, job.Type, LogText.Clean(instanceId), set.Id);
        return new JobStartResult(job, null, null, StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// <c>PUT /mods/enabled</c>: <i>Mods on</i> or <i>off</i>, applied at the next start (13 §4.3). Turning mods on
    /// without a ready active set is allowed; the start is then refused until a set is ready.
    /// </summary>
    public async Task<ModsResult> SetEnabledAsync(string instanceId, bool enabled, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return problem!;
        }

        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var mods = await RowAsync(db, instanceId, cancellationToken);
        mods.Enabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Mods of {Instance} turned {State}; it applies at the next start.", LogText.Clean(instanceId), enabled ? "on" : "off");
        return new ModsResult(await ViewAsync(db, target, cancellationToken), null, null, StatusCodes.Status200OK);
    }

    /// <summary>Back one step: the active and previous sets swap in one write; nothing is rebuilt.</summary>
    public async Task<ModsResult> RollbackAsync(string instanceId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return problem!;
        }

        if (!gate.TryEnter(instanceId, "rollback"))
        {
            return new ModsResult(null, gate.BusyMessage(instanceId), ProblemCodes.ModsBusy, StatusCodes.Status409Conflict);
        }

        try
        {
            await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
            var mods = await db.InstanceMods.FirstOrDefaultAsync(row => row.InstanceId == instanceId, cancellationToken);
            if (mods?.PreviousSetId is not { } previous)
            {
                return new ModsResult(null, $"'{instanceId}' has no previous mod set to go back to.", ProblemCodes.InvalidState, StatusCodes.Status409Conflict);
            }

            (mods.ActiveSetId, mods.PreviousSetId) = (previous, mods.ActiveSetId);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Mods of {Instance}: back to set {Set}.", LogText.Clean(instanceId), previous);
            try
            {
                await WriteMetadataAsync(db, target, mods);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not write {File} of {Instance}.", MetadataFile, LogText.Clean(instanceId));
            }

            return new ModsResult(await ViewAsync(db, target, cancellationToken), null, null, StatusCodes.Status200OK);
        }
        finally
        {
            gate.Exit(instanceId);
        }
    }

    private async Task RunSyncAsync(JobRecord job, long setId, ModsTarget target, IReadOnlyList<ModEntryRequest> entries)
    {
        try
        {
            using var log = new JobLog(context.Options.JobLogsPath, job.Id);
            var downloads = Path.Combine(context.Options.TempPath, "mods", job.Id);
            var setFolder = SetFolder(target.Paths, setId);
            string? problem = null;
            try
            {
                log.Write($"{job.Type} of {job.InstanceId}: {entries.Count} chosen mod(s), loader {target.Mods.Loader}, set {setId}.");
                await SetStageAsync(job, "resolve", log);
                var resolved = await ResolveAsync(target, entries, log);
                await SaveEntriesAsync(setId, resolved);

                // Disabled mods stay in the set but are neither downloaded nor installed (13 §4.4).
                List<PackageRef> install = [.. resolved.Where(mod => mod.Enabled).Select(mod => new PackageRef(mod.Package, mod.Version))];
                await SetStageAsync(job, "download", log);
                var fetched = await DownloadAsync(job, target, install, downloads, log);

                await SetStageAsync(job, "verify", log);
                var trusted = await VerifyAsync(target, install, fetched, log);

                await SetStageAsync(job, "build", log);
                var maxBytes = context.Options.ModPackageMaxBytes;
                var files = context.Files.Run(() => Build(setFolder, target, install, trusted, maxBytes));
                log.Write($"Built set {setId}: {files} file(s) from {install.Count} package(s).");

                await SetStageAsync(job, "activate", log);
                await ActivateAsync(target, setId, log);
            }
            catch (Exception ex) when (ex is ThunderstoreException or ModResolveException or ModSyncException or IOException or InvalidDataException
                or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or InvalidOperationException or DbUpdateException)
            {
                logger.LogError(ex, "Job {Job} failed.", job.Id);
                problem = $"{job.Type} failed: {ex.Message}";
            }

            TryDeleteDownloads(downloads);
            if (problem is not null)
            {
                await FailSetAsync(setId, setFolder, log);
            }

            job.State = problem is null ? JobState.Succeeded : JobState.Failed;
            job.Message = problem ?? $"Mod set {setId} is active; it takes effect at the next start.";
            job.Progress = problem is null ? 100 : job.Progress;
            job.FinishedAt = context.Time.GetUtcNow();
            log.Write(job.Message);
            await jobs.SaveAsync(job);
            logger.LogInformation("Job {Job}: {State}. {Message}", job.Id, job.State, job.Message);
        }
        finally
        {
            // Whatever happened, the instance's mods are free again; a job left running is failed at the next start.
            gate.Exit(target.InstanceId);
            _running.TryRemove(job.Id, out _);
        }
    }

    /// <summary>The chosen versions (the latest where none was named), then their dependencies (13 §4.5).</summary>
    private async Task<IReadOnlyList<ResolvedMod>> ResolveAsync(ModsTarget target, IReadOnlyList<ModEntryRequest> entries, JobLog log)
    {
        var versions = new Dictionary<PackageRef, ThunderstoreVersion>();
        var latest = new Dictionary<string, ThunderstorePackage>(StringComparer.Ordinal);

        async Task<ThunderstorePackage> PackageAsync(string package)
        {
            if (!latest.TryGetValue(package, out var found))
            {
                latest[package] = found = await thunderstore.GetPackageAsync(package, CancellationToken.None);
                versions[new PackageRef(package, found.Latest.Version)] = found.Latest;
                if (found.IsDeprecated)
                {
                    log.Write($"Warning: {package} is deprecated on Thunderstore.");
                }
            }

            return found;
        }

        async Task<ThunderstoreVersion> VersionAsync(PackageRef package)
        {
            if (!versions.TryGetValue(package, out var found))
            {
                versions[package] = found = await thunderstore.GetVersionAsync(package.Package, package.Version, CancellationToken.None);
            }

            return found;
        }

        var choices = new List<ModChoice>();
        foreach (var entry in entries)
        {
            var version = entry.Version;
            if (version is null)
            {
                version = (await PackageAsync(entry.Package)).Latest.Version;
                log.Write($"{entry.Package}: the latest version is {version}.");
            }

            choices.Add(new ModChoice(entry.Package, version, entry.Enabled));
        }

        var resolution = await ModResolver.ResolveAsync(
            choices,
            target.Mods.LoaderPackage,
            async package => (await VersionAsync(package)).Dependencies,
            async package => (await PackageAsync(package)).Latest.Version);
        foreach (var warning in resolution.Warnings)
        {
            log.Write($"Warning: {warning}");
        }

        // A package that is not listed in the adapter's community may be made for another game: a warning, not a failure.
        var communities = (target.Mods.Sources ?? [])
            .Where(source => source.Type == "thunderstore" && source.Community is not null)
            .Select(source => source.Community!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in resolution.Mods)
        {
            log.Write($"{mod.Package} {mod.Version}: {mod.Origin}{(mod.Enabled ? string.Empty : ", disabled")}.");
            var listed = (await PackageAsync(mod.Package)).Listings.Select(listing => listing.Community).ToList();
            if (communities.Count > 0 && !listed.Any(communities.Contains))
            {
                log.Write($"Warning: {mod.Package} is not listed in Thunderstore's {string.Join(", ", communities)} community "
                    + $"(listed in: {(listed.Count == 0 ? "none" : string.Join(", ", listed))}); it may be made for another game.");
            }
        }

        return resolution.Mods;
    }

    private async Task SaveEntriesAsync(long setId, IReadOnlyList<ResolvedMod> mods)
    {
        await using var db = await context.Database.CreateDbContextAsync();
        db.ModEntries.AddRange(mods.Select(mod => new ModEntryRecord
        {
            SetId = setId,
            Package = mod.Package,
            Version = mod.Version,
            Enabled = mod.Enabled,
            Origin = mod.Origin,
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Each zip not yet in <c>cache\</c> with its trusted hash, into Core's own folder; Core never runs or loads them
    /// (13 §4.9).
    /// </summary>
    private async Task<Dictionary<PackageRef, (string File, ThunderstoreDownload Download)>> DownloadAsync(
        JobRecord job,
        ModsTarget target,
        List<PackageRef> install,
        string downloads,
        JobLog log)
    {
        var known = await TrustedHashesAsync(install);
        var fetched = new Dictionary<PackageRef, (string File, ThunderstoreDownload Download)>();
        Directory.CreateDirectory(downloads);
        foreach (var (index, package) in install.Index())
        {
            var cached = CacheFile(target.Paths, package);
            if (known.TryGetValue(package, out var sha256) && context.Files.Run(() => File.Exists(cached) && HashFile(cached) == sha256))
            {
                log.Write($"{package}: in the cache.");
                continue;
            }

            var file = Path.Combine(downloads, Packages.CacheFileName(package.Package, package.Version));
            var download = await thunderstore.DownloadAsync(package.Package, package.Version, file, CancellationToken.None);
            log.Write($"{package}: downloaded {download.Size.ToString(CultureInfo.InvariantCulture)} bytes, SHA-256 {download.Sha256}.");
            fetched[package] = (file, download);
            job.Progress = Math.Round(100.0 * (index + 1) / install.Count, 1);
            _ = jobs.SaveAsync(job);
        }

        return fetched;
    }

    /// <summary>
    /// Trust on first use (13 §4.5): a version's first download records its hash, and every later one must match.
    /// Verified zips are then copied into <c>cache\</c> as gamewarden-run.
    /// </summary>
    private async Task<Dictionary<PackageRef, string>> VerifyAsync(
        ModsTarget target,
        List<PackageRef> install,
        Dictionary<PackageRef, (string File, ThunderstoreDownload Download)> fetched,
        JobLog log)
    {
        var trusted = await TrustedHashesAsync(install);
        foreach (var (package, (_, download)) in fetched)
        {
            if (!trusted.TryGetValue(package, out var recorded))
            {
                recorded = await RecordAsync(package, download);
                if (recorded == download.Sha256)
                {
                    log.Write($"{package}: first download; its SHA-256 is now the trusted one.");
                }
            }

            if (recorded != download.Sha256)
            {
                throw new ModSyncException(
                    $"{package}: the download's SHA-256 ({download.Sha256}) differs from the one recorded at its first download ({recorded}); it is not installed.");
            }

            trusted[package] = recorded;
        }

        foreach (var (package, (file, _)) in fetched)
        {
            // Core opens its own file; gamewarden-run, which cannot read Core's tmp folder, writes the copy (ADR-0009 §3).
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var cached = CacheFile(target.Paths, package);
            context.Files.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                using var copy = new FileStream(cached, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(copy);
            });
        }

        log.Write($"{fetched.Count} download(s) verified and cached.");
        return trusted;
    }

    /// <summary>Records a first hash; when another job recorded one first, that one is returned.</summary>
    private async Task<string> RecordAsync(PackageRef package, ThunderstoreDownload download)
    {
        try
        {
            await using var db = await context.Database.CreateDbContextAsync();
            db.PackageVersions.Add(new PackageVersionRecord
            {
                Package = package.Package,
                Version = package.Version,
                Sha256 = download.Sha256,
                Size = download.Size,
                FirstSeenAt = context.Time.GetUtcNow(),
            });
            await db.SaveChangesAsync();
            return download.Sha256;
        }
        catch (DbUpdateException)
        {
            return (await TrustedHashesAsync([package])).GetValueOrDefault(package)
                ?? throw new ModSyncException($"{package}: its hash could not be recorded.");
        }
    }

    /// <summary>
    /// Runs as gamewarden-run, synchronously (ADR-0009 §3): each cached zip is hashed again and must still be the trusted
    /// one, then its entries go where the loader puts them (<see cref="PackageExtractor"/>). An entry that would leave the
    /// set folder, an absolute path, one too long or a package over <see cref="GameWardenOptions.ModPackageMaxBytes"/>
    /// fails the build (13 §4.5).
    /// </summary>
    private static int Build(string setFolder, ModsTarget target, List<PackageRef> install, Dictionary<PackageRef, string> trusted, long maxBytes)
    {
        Directory.CreateDirectory(setFolder);
        var files = 0;
        foreach (var package in install)
        {
            using var stream = new FileStream(CacheFile(target.Paths, package), FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (sha256 != trusted[package])
            {
                throw new ModSyncException($"{package}: the cached zip changed after it was verified (SHA-256 {sha256}); it is not installed.");
            }

            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var isLoaderPackage = package.Package == target.Mods.LoaderPackage;
            files += PackageExtractor.Extract(zip, package.ToString(), setFolder, entry => target.Loader.Place(package.Package, isLoaderPackage, entry), maxBytes);
        }

        return files;
    }

    /// <summary>
    /// The new set becomes active and the old active one previous, in one database write (13 §4.5). Then sets that are
    /// neither active, previous nor running go, with the cached zips no kept set uses, and <c>mods.json</c> is written.
    /// </summary>
    private async Task ActivateAsync(ModsTarget target, long setId, JobLog log)
    {
        await using var db = await context.Database.CreateDbContextAsync();
        var mods = await db.InstanceMods.FirstAsync(row => row.InstanceId == target.InstanceId);
        var set = await db.ModSets.FirstAsync(row => row.Id == setId);
        set.State = ModSetState.Ready;
        mods.PreviousSetId = mods.ActiveSetId;
        mods.ActiveSetId = setId;
        await db.SaveChangesAsync();
        log.Write(mods.PreviousSetId is { } previous ? $"Set {setId} is active; set {previous} is the previous one." : $"Set {setId} is active.");

        // The set is active from here on; tidying up is best effort.
        try
        {
            await PruneAsync(db, target, mods, log);
            await WriteMetadataAsync(db, target, mods);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogWarning(ex, "Tidying up the mods of {Instance} failed.", LogText.Clean(target.InstanceId));
            log.Write($"Warning: tidying up failed: {ex.Message}");
        }
    }

    private async Task PruneAsync(GameWardenDb db, ModsTarget target, InstanceModsRecord mods, JobLog log)
    {
        // The running set is read again: a start may have recorded it since the row was loaded (13 §4.3).
        var running = await db.InstanceMods.AsNoTracking().Where(row => row.InstanceId == target.InstanceId).Select(row => row.RunningSetId).FirstOrDefaultAsync();
        List<long> keep = [.. new[] { mods.ActiveSetId, mods.PreviousSetId, mods.RunningSetId, running }.OfType<long>().Distinct()];
        var stale = await db.ModSets.Where(set => set.InstanceId == target.InstanceId && !keep.Contains(set.Id)).Select(set => set.Id).ToListAsync();
        await db.ModEntries.Where(entry => stale.Contains(entry.SetId)).ExecuteDeleteAsync();
        await db.ModSets.Where(set => stale.Contains(set.Id)).ExecuteDeleteAsync();
        var used = (await db.ModEntries.Where(entry => keep.Contains(entry.SetId) && entry.Enabled).Select(entry => new { entry.Package, entry.Version }).ToListAsync())
            .Select(entry => Packages.CacheFileName(entry.Package, entry.Version))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> keptFolders = [.. keep.Select(id => id.ToString(CultureInfo.InvariantCulture))];
        var (sets, zips) = context.Files.Run(() =>
        {
            var setsRemoved = 0;
            var setsFolder = Path.Combine(target.Paths.Mods, "sets");
            foreach (var folder in Directory.Exists(setsFolder) ? Directory.GetDirectories(setsFolder) : [])
            {
                if (!keptFolders.Contains(Path.GetFileName(folder)))
                {
                    Directory.Delete(folder, recursive: true);
                    setsRemoved++;
                }
            }

            var zipsRemoved = 0;
            var cacheFolder = Path.Combine(target.Paths.Mods, "cache");
            foreach (var zip in Directory.Exists(cacheFolder) ? Directory.GetFiles(cacheFolder, "*.zip") : [])
            {
                if (!used.Contains(Path.GetFileName(zip)))
                {
                    File.Delete(zip);
                    zipsRemoved++;
                }
            }

            return (setsRemoved, zipsRemoved);
        });
        log.Write($"Removed {stale.Count} old set record(s), {sets} set folder(s) and {zips} cached zip(s).");
    }

    /// <summary><c>mods.json</c>, a copy of the active set; the database stays the source of truth (ADR-0020 decision 2).</summary>
    private async Task WriteMetadataAsync(GameWardenDb db, ModsTarget target, InstanceModsRecord mods)
    {
        if (mods.ActiveSetId is not { } active || await db.ModSets.AsNoTracking().FirstOrDefaultAsync(set => set.Id == active) is not { } set)
        {
            return;
        }

        var entries = await db.ModEntries.AsNoTracking().Where(entry => entry.SetId == active).ToListAsync();
        var json = JsonSerializer.Serialize(
            new ModsFile(
                "A copy of the active mod set, written by GameWarden; editing it changes nothing.",
                target.InstanceId,
                set.Id,
                set.CreatedAt,
                target.Mods.Loader,
                [.. Ordered(entries).Select(entry => new ModEntryView(entry.Package, entry.Version, entry.Enabled, entry.Origin))]),
            MetadataJson);
        var path = Path.Combine(target.Paths.Mods, MetadataFile);
        context.Files.Run(() => File.WriteAllText(path, json));
    }

    private async Task FailSetAsync(long setId, string setFolder, JobLog log)
    {
        try
        {
            await using var db = await context.Database.CreateDbContextAsync();
            await db.ModSets.Where(set => set.Id == setId && set.State == ModSetState.Building)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.State, ModSetState.Failed));
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Could not mark mod set {Set} failed.", setId);
        }

        TryDeleteSetFolder(setFolder, log);
        log.Write($"Set {setId} failed; the active set is unchanged.");
    }

    private void TryDeleteSetFolder(string setFolder, JobLog? log)
    {
        try
        {
            context.Files.Run(() =>
            {
                if (Directory.Exists(setFolder))
                {
                    Directory.Delete(setFolder, recursive: true);
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {Folder}.", setFolder);
            log?.Write($"Warning: could not delete {setFolder}: {ex.Message}");
        }
    }

    /// <summary>Core's own scratch folder: deleted as Core, not as gamewarden-run.</summary>
    private void TryDeleteDownloads(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {Folder}.", folder);
        }
    }

    private async Task<Dictionary<PackageRef, string>> TrustedHashesAsync(IReadOnlyCollection<PackageRef> packages)
    {
        await using var db = await context.Database.CreateDbContextAsync();
        var names = packages.Select(package => package.Package).Distinct().ToList();
        var rows = await db.PackageVersions.AsNoTracking().Where(row => names.Contains(row.Package)).ToListAsync();
        return rows
            .Select(row => (Ref: new PackageRef(row.Package, row.Version), row.Sha256))
            .Where(row => packages.Contains(row.Ref))
            .ToDictionary(row => row.Ref, row => row.Sha256);
    }

    private async Task SetStageAsync(JobRecord job, string stage, JobLog log)
    {
        job.State = JobState.Running;
        job.Stage = stage;
        job.Progress = null;
        log.Write($"Stage: {stage}");
        await jobs.SaveAsync(job);
    }

    private (ModsTarget? Target, ModsResult? Problem) FindTarget(string instanceId)
    {
        if (supervisor.Find(instanceId) is not { } actor)
        {
            return (null, new ModsResult(null, $"No instance '{instanceId}'.", "not_found", StatusCodes.Status404NotFound));
        }

        var adapterId = actor.Snapshot.AdapterId;
        if (context.Adapters.Find(adapterId)?.Mods is not { } mods)
        {
            return (null, new ModsResult(null, $"Adapter '{adapterId}' has no mod support.", ProblemCodes.InvalidState, StatusCodes.Status409Conflict));
        }

        if (loaders.FirstOrDefault(loader => loader.Id == mods.Loader) is not { } modLoader)
        {
            return (null, new ModsResult(null, $"This Core has no mod loader '{mods.Loader}'.", ProblemCodes.InvalidState, StatusCodes.Status409Conflict));
        }

        return (new ModsTarget(instanceId, InstancePaths.For(context.Options.DataRoot, instanceId), mods, modLoader), null);
    }

    private static async Task<ModsView> ViewAsync(GameWardenDb db, ModsTarget target, CancellationToken cancellationToken)
    {
        var mods = await db.InstanceMods.AsNoTracking().FirstOrDefaultAsync(row => row.InstanceId == target.InstanceId, cancellationToken);
        var sets = await db.ModSets.AsNoTracking().Where(set => set.InstanceId == target.InstanceId).ToListAsync(cancellationToken);
        var newestKept = Math.Max(mods?.ActiveSetId ?? 0, mods?.PreviousSetId ?? 0);
        var pending = sets.Where(set => set.State != ModSetState.Ready && set.Id > newestKept).MaxBy(set => set.Id);
        List<long> shown = [.. new[] { mods?.ActiveSetId, mods?.PreviousSetId, pending?.Id }.OfType<long>()];
        var entries = await db.ModEntries.AsNoTracking().Where(entry => shown.Contains(entry.SetId)).ToListAsync(cancellationToken);

        ModSetView? View(long? id) => sets.FirstOrDefault(set => set.Id == id) is { } set
            ? new ModSetView(
                set.Id,
                set.CreatedAt,
                set.Origin,
                set.State,
                set.JobId,
                [.. Ordered(entries.Where(entry => entry.SetId == set.Id)).Select(entry => new ModEntryView(entry.Package, entry.Version, entry.Enabled, entry.Origin))])
            : null;

        return new ModsView(target.InstanceId, target.Mods.Loader, mods?.Enabled ?? false, View(mods?.ActiveSetId), View(mods?.PreviousSetId), View(pending?.Id));
    }

    /// <summary>Chosen mods first, then dependencies, each by name.</summary>
    private static IEnumerable<ModEntryRecord> Ordered(IEnumerable<ModEntryRecord> entries) =>
        entries.OrderBy(entry => entry.Origin == ModResolver.Chosen ? 0 : 1).ThenBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase);

    private static string? Invalid(IReadOnlyList<ModEntryRequest>? entries)
    {
        if (entries is null)
        {
            return "entries is required (an empty list removes every mod).";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!Packages.IsPackage(entry.Package))
            {
                return $"'{entry.Package}' is not a Thunderstore package name (Namespace-Name).";
            }

            if (entry.Version is not null && !Packages.IsVersion(entry.Version))
            {
                return $"{entry.Package}: '{entry.Version}' is not a version (x.y.z).";
            }

            if (!seen.Add(entry.Package))
            {
                return $"{entry.Package} is in the list twice.";
            }
        }

        return null;
    }

    /// <summary><c>{paths.mods}\sets\&lt;id&gt;\</c>.</summary>
    /// <summary>
    /// The instance's mods row, created when missing. <c>INSERT OR IGNORE</c>, because a first mods job and a first
    /// <i>Mods on</i> may create it at the same time.
    /// </summary>
    private static async Task<InstanceModsRecord> RowAsync(GameWardenDb db, string instanceId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO instance_mods (InstanceId, Enabled) VALUES ({instanceId}, 0)", cancellationToken);
        return await db.InstanceMods.FirstAsync(row => row.InstanceId == instanceId, cancellationToken);
    }

    internal static string SetFolder(InstancePaths paths, long setId) => Path.Combine(paths.Mods, "sets", setId.ToString(CultureInfo.InvariantCulture));

    private static string CacheFile(InstancePaths paths, PackageRef package) =>
        Path.Combine(paths.Mods, "cache", Packages.CacheFileName(package.Package, package.Version));

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed record ModsTarget(string InstanceId, InstancePaths Paths, AdapterMods Mods, IModLoader Loader);

    /// <summary>The shape of <c>mods.json</c>.</summary>
    private sealed record ModsFile(string Note, string InstanceId, long SetId, DateTimeOffset CreatedAt, string Loader, IReadOnlyList<ModEntryView> Mods);
}
