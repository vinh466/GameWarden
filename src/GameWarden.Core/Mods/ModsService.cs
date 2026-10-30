using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>The answer to a code preview, a share code, an update check or a config read: its value, or why not.</summary>
public sealed record ModsResult<T>(T? Value, string? Error, string? Code, int StatusCode)
    where T : class
{
    public static ModsResult<T> Ok(T value) => new(value, null, null, StatusCodes.Status200OK);

    public static ModsResult<T> Problem(int status, string code, string error) => new(null, error, code, status);

    public static ModsResult<T> From(ModsResult problem) => new(null, problem.Error, problem.Code, problem.StatusCode);
}

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
    ModCodePreviews previews,
    ILogger<ModsService> logger)
{
    public const string JobType = "mods.sync";

    /// <summary>Origin of a set made from <c>PUT /mods</c> with <c>entries</c>.</summary>
    public const string EditOrigin = "edit";

    /// <summary>Origin of a set made from <c>PUT /mods</c> with a code's <c>previewId</c>.</summary>
    public const string CodeOrigin = "code";

    /// <summary>A set's folder of the config files of the code it came from (docs/13 §4.3, §4.7).</summary>
    public const string CodeConfigFolder = "code-config";

    /// <summary>The largest config file <c>GET /mods/configs/content</c> shows.</summary>
    public const long ConfigViewMaxBytes = 1024 * 1024;

    /// <summary>Thunderstore requests at a time for a preview or an update check (docs/13 §4.8).</summary>
    public const int ThunderstoreParallelism = 4;

    /// <summary>502: Thunderstore did not answer as it should, after the policy's tries (05 §5).</summary>
    public const string UpstreamUnavailable = "upstream_unavailable";

    /// <summary><c>{paths.mods}\mods.json</c>: a copy of the active set for people and manual backups (docs/13 §4.2).</summary>
    public const string MetadataFile = "mods.json";

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);

    /// <summary>The last update check of each instance, kept in memory for <c>GET /mods</c> (docs/13 §4.8).</summary>
    private readonly ConcurrentDictionary<string, ModUpdateCheck> _newerVersions = new(StringComparer.Ordinal);

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

    /// <summary>
    /// <c>PUT /mods</c>: a new set built by a <c>mods.sync</c> job, from the whole list of chosen mods or from a code
    /// preview (docs/13 §4.4): then the entries are exactly the code's, versions and <i>enabled</i> included, and the
    /// code's config files go into the set's <c>code-config\</c>.
    /// </summary>
    public async Task<JobStartResult> StartSyncAsync(string instanceId, IReadOnlyList<ModEntryRequest>? entries, string? previewId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return new JobStartResult(null, problem!.Error, problem.Code, problem.StatusCode);
        }

        var origin = EditOrigin;
        IReadOnlyList<CodeConfigFile> codeConfigs = [];
        if (previewId is not null)
        {
            if (entries is not null)
            {
                return new JobStartResult(null, "Send entries or previewId, not both.", ProblemCodes.ValidationFailed, StatusCodes.Status400BadRequest);
            }

            if (previews.Find(instanceId, previewId) is not { } preview)
            {
                return new JobStartResult(
                    null,
                    $"The code preview '{previewId}' is unknown or expired (a preview lasts {ModCodePreviews.Lifetime.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes); preview the code again.",
                    "not_found",
                    StatusCodes.Status404NotFound);
            }

            entries = [.. preview.Mods.Select(mod => new ModEntryRequest(mod.Package, mod.Version, mod.Enabled))];
            codeConfigs = preview.ConfigFiles;
            origin = CodeOrigin;
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
                Origin = origin,
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
        _running[job.Id] = Task.Run(() => RunSyncAsync(job, set.Id, target, entries!, codeConfigs));
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

    /// <summary>
    /// <c>POST /mods/code-preview</c> (docs/13 §4.4): fetches an r2modman code, reads its mods and config files, asks
    /// Thunderstore for each package's state (at most four at a time) and keeps the preview for 30 minutes. Nothing
    /// changes. Config paths are checked as if the set's id were the longest one, so the build cannot refuse them later.
    /// </summary>
    public async Task<ModsResult<ModCodePreview>> PreviewCodeAsync(string instanceId, string? code, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModCodePreview>.From(problem!);
        }

        if (!R2ModmanCode.TryNormalizeCode(code, out var key))
        {
            return ModsResult<ModCodePreview>.Problem(
                StatusCodes.Status400BadRequest,
                ProblemCodes.ValidationFailed,
                $"'{code}' is not an r2modman profile code; r2modman's Export profile as code gives one like 0192f1c4-7a51-7c3e-9d0b-5e2f8a6b4c13.");
        }

        R2ModmanProfile profile;
        try
        {
            var text = await thunderstore.GetProfileAsync(key, cancellationToken);
            var longestSet = SetFolder(target.Paths, long.MaxValue);
            profile = R2ModmanCode.Read(text, path =>
            {
                ModLayout.Resolve(Path.Combine(longestSet, CodeConfigFolder), path);
                ModLayout.Resolve(Path.Combine(longestSet, target.Loader.ConfigFolder), path);
            });
        }
        catch (ThunderstoreException ex)
        {
            return ModsResult<ModCodePreview>.Problem(
                ex.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status502BadGateway,
                ex.NotFound ? "not_found" : UpstreamUnavailable,
                ex.Message);
        }
        catch (R2ModmanCodeException ex)
        {
            return ModsResult<ModCodePreview>.Problem(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, ex.Message);
        }

        Dictionary<string, ModPackageState> states;
        try
        {
            states = await PackageStatesAsync(profile.Mods.Select(mod => mod.Package), cancellationToken);
        }
        catch (ThunderstoreException ex)
        {
            return ModsResult<ModCodePreview>.Problem(StatusCodes.Status502BadGateway, UpstreamUnavailable, ex.Message);
        }

        List<string> warnings = [.. profile.Warnings];
        var missing = profile.Mods.Where(mod => states[mod.Package] == ModPackageState.NotFound).Select(mod => mod.Package).ToList();
        if (missing.Count > 0)
        {
            warnings.Add($"Not on Thunderstore: {string.Join(", ", missing)}. Applying the code fails while it lists them; "
                + "apply it, then remove them, or ask for a code without them.");
        }

        var preview = previews.Add(instanceId, key, profile);
        logger.LogInformation(
            "Code {Code} previewed for {Instance}: {Mods} mod(s), {Configs} config file(s).",
            key,
            LogText.Clean(instanceId),
            profile.Mods.Count,
            profile.ConfigFiles.Count);
        return ModsResult<ModCodePreview>.Ok(new ModCodePreview(
            preview.Id,
            key,
            profile.ProfileName,
            preview.ExpiresAt,
            [.. profile.Mods.Select(mod => new ModCodePreviewEntry(mod.Package, mod.Version, mod.Enabled, states[mod.Package]))],
            warnings,
            [.. profile.ConfigFiles.Select(file => file.Path)]));
    }

    /// <summary>
    /// <c>POST /mods/share-code</c> (docs/13 §4.6): an r2modman code of the active set's enabled mods, named after the
    /// instance, without config files; the code and its time are stored for <c>GET /mods</c>.
    /// </summary>
    public async Task<ModsResult<ModShareCode>> ShareCodeAsync(string instanceId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModShareCode>.From(problem!);
        }

        var displayName = supervisor.Find(instanceId)?.Snapshot.DisplayName is { Length: > 0 } name ? name : instanceId;
        List<ModEntryRecord> entries;
        await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
        {
            entries = [.. Ordered(await ActiveEntriesAsync(db, instanceId, cancellationToken) ?? []).Where(entry => entry.Enabled)];
        }

        if (entries.Count == 0)
        {
            return ModsResult<ModShareCode>.Problem(
                StatusCodes.Status409Conflict,
                ProblemCodes.InvalidState,
                $"'{instanceId}' has no active mod set with mods to share; add mods first.");
        }

        string code;
        try
        {
            var text = R2ModmanCode.Write(displayName, entries.Select(entry => new R2ModmanMod(entry.Package, entry.Version, true)));
            code = await thunderstore.CreateProfileAsync(text, cancellationToken);
        }
        catch (ThunderstoreException ex)
        {
            return ModsResult<ModShareCode>.Problem(StatusCodes.Status502BadGateway, UpstreamUnavailable, ex.Message);
        }

        var createdAt = context.Time.GetUtcNow();
        await using (var db = await context.Database.CreateDbContextAsync(CancellationToken.None))
        {
            var row = await RowAsync(db, instanceId, CancellationToken.None);
            row.ShareCode = code;
            row.ShareCodeCreatedAt = createdAt;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        logger.LogInformation("Share code {Code} made for {Instance}.", code, LogText.Clean(instanceId));
        return ModsResult<ModShareCode>.Ok(new ModShareCode(code, createdAt));
    }

    /// <summary>
    /// <c>POST /mods/check-updates</c> (docs/13 §4.8): each enabled package of the active set against its latest version
    /// on Thunderstore, at most four requests at a time. The result is kept for <c>GET /mods</c>; nothing changes.
    /// </summary>
    public async Task<ModsResult<ModUpdateCheck>> CheckUpdatesAsync(string instanceId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModUpdateCheck>.From(problem!);
        }

        List<ModEntryRecord>? entries;
        await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
        {
            entries = await ActiveEntriesAsync(db, instanceId, cancellationToken);
        }

        if (entries is null)
        {
            return ModsResult<ModUpdateCheck>.Problem(StatusCodes.Status409Conflict, ProblemCodes.InvalidState, $"'{instanceId}' has no active mod set to check.");
        }

        var installed = entries.Where(entry => entry.Enabled).ToList();
        var latest = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await Parallel.ForEachAsync(
                installed,
                new ParallelOptions { MaxDegreeOfParallelism = ThunderstoreParallelism, CancellationToken = cancellationToken },
                async (entry, token) =>
                {
                    try
                    {
                        latest[entry.Package] = (await thunderstore.GetPackageAsync(entry.Package, token)).Latest.Version;
                    }
                    catch (ThunderstoreException ex) when (ex.NotFound)
                    {
                        logger.LogWarning("{Package} of {Instance} is no longer on Thunderstore.", entry.Package, LogText.Clean(instanceId));
                    }
                });
        }
        catch (ThunderstoreException ex)
        {
            return ModsResult<ModUpdateCheck>.Problem(StatusCodes.Status502BadGateway, UpstreamUnavailable, ex.Message);
        }

        var check = new ModUpdateCheck(
            context.Time.GetUtcNow(),
            [
                .. Ordered(installed)
                    .Where(entry => latest.TryGetValue(entry.Package, out var version) && Packages.CompareVersions(version, entry.Version) > 0)
                    .Select(entry => new ModUpdateInfo(entry.Package, entry.Version, latest[entry.Package])),
            ]);
        _newerVersions[instanceId] = check;
        logger.LogInformation("Update check of {Instance}: {Count} newer version(s).", LogText.Clean(instanceId), check.Updates.Count);
        return ModsResult<ModUpdateCheck>.Ok(check);
    }

    /// <summary><c>GET /mods/configs</c> (docs/13 §4.7): the files of the active set's config folder, subfolders included.</summary>
    public async Task<ModsResult<ModConfigList>> ConfigsAsync(string instanceId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModConfigList>.From(problem!);
        }

        if (await ActiveSetIdAsync(instanceId, cancellationToken) is not { } setId)
        {
            return ModsResult<ModConfigList>.Ok(new ModConfigList(null, []));
        }

        var folder = ConfigFolder(target, setId);
        var files = context.Files.Run(() => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(file => new FileInfo(file))
                .Select(info => new ModConfigFileInfo(Path.GetRelativePath(folder, info.FullName).Replace('\\', '/'), info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToList()
            : []);
        return ModsResult<ModConfigList>.Ok(new ModConfigList(setId, files));
    }

    /// <summary>
    /// <c>GET /mods/configs/content?path=</c>: one config file of the active set as text, at most
    /// <see cref="ConfigViewMaxBytes"/>; the path must stay inside the config folder (<see cref="ModLayout"/>).
    /// </summary>
    public async Task<ModsResult<ModConfigContent>> ConfigContentAsync(string instanceId, string? path, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModConfigContent>.From(problem!);
        }

        if (await ActiveSetIdAsync(instanceId, cancellationToken) is not { } setId)
        {
            return ModsResult<ModConfigContent>.Problem(StatusCodes.Status404NotFound, "not_found", $"'{instanceId}' has no active mod set.");
        }

        var folder = ConfigFolder(target, setId);
        string relative;
        string full;
        try
        {
            relative = (string.IsNullOrEmpty(path) ? null : ModLayout.Normalize(path)) ?? throw new InvalidDataException("path names no file.");
            full = ModLayout.Resolve(folder, relative);
        }
        catch (InvalidDataException ex)
        {
            return ModsResult<ModConfigContent>.Problem(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"path: {ex.Message}");
        }

        var (info, bytes) = context.Files.Run<(FileInfo?, byte[]?)>(() =>
        {
            var file = new FileInfo(full);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return (null, null);
            }

            return (file, file.Length > ConfigViewMaxBytes ? null : File.ReadAllBytes(full));
        });
        if (info is null)
        {
            return ModsResult<ModConfigContent>.Problem(StatusCodes.Status404NotFound, "not_found", $"Mod set {setId} has no config file '{relative}'.");
        }

        if (bytes is null || bytes.LongLength > ConfigViewMaxBytes)
        {
            return ModsResult<ModConfigContent>.Problem(
                StatusCodes.Status409Conflict,
                ProblemCodes.InvalidState,
                $"'{relative}' is {info.Length.ToString(CultureInfo.InvariantCulture)} bytes; only files up to {ConfigViewMaxBytes.ToString(CultureInfo.InvariantCulture)} bytes are shown.");
        }

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ModsResult<ModConfigContent>.Ok(new ModConfigContent(
            setId,
            relative,
            bytes.LongLength,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            await reader.ReadToEndAsync(cancellationToken)));
    }

    /// <summary>Thunderstore's state of each package, at most four requests at a time.</summary>
    private async Task<Dictionary<string, ModPackageState>> PackageStatesAsync(IEnumerable<string> packages, CancellationToken cancellationToken)
    {
        var states = new ConcurrentDictionary<string, ModPackageState>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            packages.Distinct(StringComparer.Ordinal),
            new ParallelOptions { MaxDegreeOfParallelism = ThunderstoreParallelism, CancellationToken = cancellationToken },
            async (package, token) =>
            {
                try
                {
                    states[package] = (await thunderstore.GetPackageAsync(package, token)).IsDeprecated ? ModPackageState.Deprecated : ModPackageState.Ok;
                }
                catch (ThunderstoreException ex) when (ex.NotFound)
                {
                    states[package] = ModPackageState.NotFound;
                }
            });
        return new Dictionary<string, ModPackageState>(states, StringComparer.Ordinal);
    }

    private async Task<long?> ActiveSetIdAsync(string instanceId, CancellationToken cancellationToken)
    {
        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        return await db.InstanceMods.AsNoTracking().Where(row => row.InstanceId == instanceId).Select(row => row.ActiveSetId).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The entries of the active set, or null without one.</summary>
    private static async Task<List<ModEntryRecord>?> ActiveEntriesAsync(GameWardenDb db, string instanceId, CancellationToken cancellationToken)
    {
        var active = await db.InstanceMods.AsNoTracking().Where(row => row.InstanceId == instanceId).Select(row => row.ActiveSetId).FirstOrDefaultAsync(cancellationToken);
        return active is { } setId ? await db.ModEntries.AsNoTracking().Where(entry => entry.SetId == setId).ToListAsync(cancellationToken) : null;
    }

    private static string ConfigFolder(ModsTarget target, long setId) =>
        Path.Combine(SetFolder(target.Paths, setId), target.Loader.ConfigFolder.Replace('/', Path.DirectorySeparatorChar));

    private async Task RunSyncAsync(JobRecord job, long setId, ModsTarget target, IReadOnlyList<ModEntryRequest> entries, IReadOnlyList<CodeConfigFile> codeConfigs)
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
                var files = context.Files.Run(() => Build(setFolder, target, install, trusted, maxBytes, codeConfigs));
                log.Write($"Built set {setId}: {files} file(s) from {install.Count} package(s)"
                    + (codeConfigs.Count > 0 ? $", and {codeConfigs.Count} config file(s) of the code in {CodeConfigFolder}." : "."));

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
    /// fails the build (13 §4.5). A code's config files go into <c>code-config\</c> with the same path checks; they are
    /// copied over the set's configs at its first start (§4.7).
    /// </summary>
    private static int Build(
        string setFolder,
        ModsTarget target,
        List<PackageRef> install,
        Dictionary<PackageRef, string> trusted,
        long maxBytes,
        IReadOnlyList<CodeConfigFile> codeConfigs)
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

        var codeConfigFolder = Path.Combine(setFolder, CodeConfigFolder);
        foreach (var config in codeConfigs)
        {
            string path;
            try
            {
                path = ModLayout.Resolve(codeConfigFolder, config.Path);
            }
            catch (InvalidDataException ex)
            {
                throw new ModSyncException($"The code's config file '{config.Path}' is refused: {ex.Message}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, config.Content);
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

    private async Task<ModsView> ViewAsync(GameWardenDb db, ModsTarget target, CancellationToken cancellationToken)
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

        // The last check, against the active set as it is now: an update applied since then is no longer newer.
        ModUpdateCheck? newer = null;
        if (_newerVersions.TryGetValue(target.InstanceId, out var check))
        {
            var installed = entries.Where(entry => entry.SetId == mods?.ActiveSetId && entry.Enabled).ToDictionary(entry => entry.Package, entry => entry.Version, StringComparer.Ordinal);
            newer = check with
            {
                Updates =
                [
                    .. check.Updates
                        .Where(update => installed.TryGetValue(update.Package, out var current) && Packages.CompareVersions(update.Latest, current) > 0)
                        .Select(update => update with { Current = installed[update.Package] }),
                ],
            };
        }

        return new ModsView(
            target.InstanceId,
            target.Mods.Loader,
            mods?.Enabled ?? false,
            View(mods?.ActiveSetId),
            View(mods?.PreviousSetId),
            View(pending?.Id),
            mods?.ShareCode,
            mods?.ShareCodeCreatedAt,
            newer);
    }

    /// <summary>Chosen mods first, then dependencies, each by name.</summary>
    private static IEnumerable<ModEntryRecord> Ordered(IEnumerable<ModEntryRecord> entries) =>
        entries.OrderBy(entry => entry.Origin == ModResolver.Chosen ? 0 : 1).ThenBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase);

    private static string? Invalid(IReadOnlyList<ModEntryRequest>? entries)
    {
        if (entries is null)
        {
            return "entries or previewId is required (an empty list of entries removes every mod).";
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
