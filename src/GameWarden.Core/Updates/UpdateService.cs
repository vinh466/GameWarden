using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Jobs;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Updates;

/// <param name="FromZip">A package on this machine instead of GitHub (dev builds, 10 §7).</param>
/// <param name="AllowUnsigned">Skip the signature check for <paramref name="FromZip"/>; the Owner's own dev builds only.</param>
public sealed record UpdateRequest(bool Prerelease = false, string? Version = null, string? FromZip = null, bool AllowUnsigned = false);

public sealed record UpdateCheck(string Current, string? Latest, bool Available, string? Url);

public sealed record UpdateStarted(string From, string To, string Message);

/// <summary>Versions in <c>versions\</c> (10 §6 "Clean up" keeps the last two) and the one to switch back to.</summary>
public sealed record InstalledVersions(string Current, IReadOnlyList<string> Installed, string? Previous, bool CanRestoreDatabase);

public sealed record SwitchVersionRequest(string Version, bool RestoreDatabase = false);

public sealed class UpdateRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Self-update (ADR-0013, 10 §6): find the package, verify it, extract it next to the current version, snapshot the
/// database, then hand over to the new version's Setup engine, which switches the service and rolls back on failure.
/// Games keep running throughout; only the API is down while the service restarts.
/// </summary>
public sealed class UpdateService(
    ReleaseFeed feed,
    JobService jobs,
    IHttpClientFactory http,
    IDbContextFactory<GameWardenDb> database,
    IOptions<GameWardenOptions> options,
    ILogger<UpdateService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Public keys the signature is checked against; tests use their own.</summary>
    public IReadOnlyList<string> PublicKeys { get; init; } = UpdateKeys.Release;

    /// <summary><c>C:\Program Files\GameWarden</c> when Core runs from <c>versions\&lt;v&gt;\</c>; null in development.</summary>
    public static string? InstallRoot { get; } = DetectInstallRoot(AppContext.BaseDirectory);

    public UpdateCheck? LastCheck { get; private set; }

    public static string? DetectInstallRoot(string baseDirectory)
    {
        var folder = new DirectoryInfo(baseDirectory.TrimEnd('\\', '/'));
        return folder.Parent is { Name: "versions", Parent: { } root } ? root.FullName : null;
    }

    public UpdateResult? LastResult()
    {
        var path = Path.Combine(options.Value.UpdatesPath, UpdateResult.FileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(path), ContractJson.Options) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public async Task<UpdateCheck> CheckAsync(bool prerelease, CancellationToken cancellationToken)
    {
        var current = SemanticVersion.Parse(CoreInfo.Version);
        var latest = await feed.LatestAsync(prerelease, cancellationToken);
        LastCheck = new UpdateCheck(current.ToString(), latest?.Version.ToString(), latest is not null && latest.Version > current, latest?.Url);
        return LastCheck;
    }

    public async Task<UpdateStarted> StartAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        RefuseOnDevelopmentPlatform("Updates work");
        if (InstallRoot is null)
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, "Updates work in an installed GameWarden only (this Core does not run from versions\\<version>\\).");
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, "An update is already being prepared.");
        }

        try
        {
            if (jobs.AnyRunning)
            {
                throw new UpdateRefusedException(ProblemCodes.InstanceBusy, "A job is running; update when it has finished (gamewarden jobs).");
            }

            var current = CoreInfo.Version;
            var (zip, packageVersion) = request.FromZip is not null
                ? FromLocalZip(request.FromZip, request.AllowUnsigned)
                : await DownloadAsync(request, cancellationToken);
            var version = Canonical(packageVersion); // becomes a folder name and a Setup argument
            if (SemanticVersion.Parse(version) == SemanticVersion.Parse(current))
            {
                throw new UpdateRefusedException(ProblemCodes.InvalidState, $"GameWarden {version} is already running.");
            }

            var folder = Extract(zip, version);
            var snapshot = await SnapshotDatabaseAsync(version, cancellationToken);
            var setup = Path.Combine(folder, "GameWarden.Setup.exe");
            var info = new ProcessStartInfo(setup)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = folder,
            };
            foreach (var argument in new[] { "apply", version, "--snapshot", snapshot, "--quiet" })
            {
                info.ArgumentList.Add(argument);
            }

            // Setup stops this service, so it must not depend on Core: it is a separate process, not in a job.
            using (Process.Start(info) ?? throw new InvalidOperationException($"Could not start {setup}."))
            {
            }

            logger.LogInformation("Updating from {From} to {To}: Setup takes over (snapshot {Snapshot}).", current, version, snapshot);
            return new UpdateStarted(current, version, $"Updating to {version}. Core restarts in a moment; games keep running.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public InstalledVersions Installed()
    {
        var current = CoreInfo.Version;
        if (InstallRoot is null || !Directory.Exists(Path.Combine(InstallRoot, "versions")))
        {
            return new InstalledVersions(current, [], null, false);
        }

        var installed = Directory.GetDirectories(Path.Combine(InstallRoot, "versions"))
            .Select(Path.GetFileName)
            .Where(name => name is not null && SemanticVersion.TryParse(name, out _))
            .Select(name => name!)
            .OrderByDescending(SemanticVersion.Parse)
            .ToList();
        var others = installed.Where(version => version != current).ToList();
        // The version this one was updated from, when it is still there; otherwise the newest older one.
        var previous = LastResult() is { From: { } from } && others.Contains(from)
            ? from
            : others.FirstOrDefault(version => SemanticVersion.Parse(version) < SemanticVersion.Parse(current)) ?? others.FirstOrDefault();
        return new InstalledVersions(current, installed, previous, File.Exists(SnapshotOf(current)));
    }

    /// <summary>
    /// `gamewarden version use` from the panel: the other version's Setup switches the service to it (10 §6); with
    /// <paramref name="restoreDatabase"/>, also back to the database as it was before the update to this version.
    /// </summary>
    public UpdateStarted SwitchTo(string requested, bool restoreDatabase)
    {
        RefuseOnDevelopmentPlatform("Switching versions works");
        if (InstallRoot is null)
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, "Switching versions works in an installed GameWarden only.");
        }

        if (jobs.AnyRunning)
        {
            throw new UpdateRefusedException(ProblemCodes.InstanceBusy, "A job is running; switch when it has finished.");
        }

        var version = Canonical(requested);
        var current = CoreInfo.Version;
        var folder = Path.Combine(InstallRoot, "versions", version);
        var setup = Path.Combine(folder, "GameWarden.Setup.exe");
        if (version == current || !File.Exists(setup))
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, version == current ? $"GameWarden {version} is already running." : $"Version {version} is not installed.");
        }

        var snapshot = SnapshotOf(current);
        if (restoreDatabase && !File.Exists(snapshot))
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, "There is no database snapshot from the update to this version.");
        }

        var info = new ProcessStartInfo(setup) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        foreach (var argument in restoreDatabase ? new[] { "apply", version, "--quiet", "--restore-db", snapshot } : ["apply", version, "--quiet"])
        {
            info.ArgumentList.Add(argument);
        }

        using (Process.Start(info) ?? throw new InvalidOperationException($"Could not start {setup}."))
        {
        }

        logger.LogInformation("Switching from {From} back to {To} (database restored: {Restore}).", current, version, restoreDatabase);
        return new UpdateStarted(current, version, $"Switching to {version}. Core restarts in a moment; games keep running.");
    }

    /// <summary>The Setup engine that applies an update is a Windows program; the development platform has no self-update (ADR-0016).</summary>
    private static void RefuseOnDevelopmentPlatform(string what)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, $"{what} in an installed GameWarden only, on Windows; this Core runs on the development platform (ADR-0016).");
        }
    }

    /// <summary>The database as it was before the update to <paramref name="version"/>.</summary>
    private string SnapshotOf(string version) => Path.Combine(options.Value.UpdatesPath, version, "db-before.db");

    /// <summary>
    /// The version rebuilt from numbers and known pre-release words only (<c>0.2.0</c>, <c>0.2.0-rc.1</c>,
    /// <c>0.2.0-dev.42</c>), so nothing from a request or a package reaches the file system or a command line as is.
    /// </summary>
    internal static string Canonical(string version)
    {
        if (!SemanticVersion.TryParse(version, out var parsed))
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, "The package version is not a version like 1.2.3 or 1.2.3-rc.1.");
        }

        var text = new System.Text.StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{parsed.Major}.{parsed.Minor}.{parsed.Patch}"));
        for (var i = 0; i < parsed.Prerelease.Count; i++)
        {
            var identifier = parsed.Prerelease[i];
            text.Append(i == 0 ? '-' : '.');
            if (int.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                text.Append(number.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(PrereleaseWords.FirstOrDefault(word => word == identifier)
                    ?? throw new UpdateRefusedException(ProblemCodes.ValidationFailed, $"Pre-release versions may use only numbers and {string.Join(", ", PrereleaseWords)}."));
            }
        }

        return text.ToString();
    }

    private static readonly string[] PrereleaseWords = ["alpha", "beta", "preview", "rc", "dev", "local"];

    private (string Zip, string Version) FromLocalZip(string zip, bool allowUnsigned)
    {
        if (!File.Exists(zip))
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, $"{zip} does not exist.");
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(zip))!;
        var sums = Path.Combine(folder, UpdateVerifier.SumsFile);
        var signature = Path.Combine(folder, UpdateVerifier.SignatureFile);
        if (File.Exists(sums) && File.Exists(signature))
        {
            Verify(zip, sums, signature);
        }
        else if (!allowUnsigned)
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, $"No {UpdateVerifier.SumsFile} and {UpdateVerifier.SignatureFile} next to the zip; pass --allow-unsigned for your own dev build.");
        }
        else
        {
            logger.LogWarning("Installing the UNSIGNED package {Zip} at the Owner's request.", LogText.Clean(zip));
        }

        return (zip, ReadVersion(zip));
    }

    private async Task<(string Zip, string Version)> DownloadAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        var release = request.Version is not null
            ? await feed.FindAsync(request.Version, cancellationToken)
            : await feed.LatestAsync(request.Prerelease, cancellationToken);
        if (release is null)
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, request.Version is null ? "No release found." : $"No release {request.Version}.");
        }

        if (request.Version is null && release.Version <= SemanticVersion.Parse(CoreInfo.Version))
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, $"GameWarden {CoreInfo.Version} is up to date (latest: {release.Version}).");
        }

        var version = release.Version.ToString();
        var zipName = $"GameWarden-{version}-win-x64.zip";
        var folder = Path.Combine(options.Value.UpdatesPath, version);
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { UpdateVerifier.SumsFile, UpdateVerifier.SignatureFile, zipName })
        {
            if (!release.Assets.TryGetValue(name, out var url))
            {
                throw new UpdateRefusedException(ProblemCodes.ValidationFailed, $"Release {version} has no {name}.");
            }

            logger.LogInformation("Downloading {Url}.", LogText.Clean(url.ToString()));
            await using var source = await http.CreateClient("github").GetStreamAsync(url, cancellationToken);
            await using var target = File.Create(Path.Combine(folder, name));
            await source.CopyToAsync(target, cancellationToken);
        }

        var zip = Path.Combine(folder, zipName);
        Verify(zip, Path.Combine(folder, UpdateVerifier.SumsFile), Path.Combine(folder, UpdateVerifier.SignatureFile));
        var packaged = ReadVersion(zip);
        if (SemanticVersion.Parse(packaged) != release.Version)
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, $"The package says {packaged}, the release {version}.");
        }

        return (zip, version);
    }

    private void Verify(string zip, string sums, string signature)
    {
        if (PublicKeys.Count == 0)
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, "This build has no release key to check updates with; use --from <zip> --allow-unsigned.");
        }

        try
        {
            UpdateVerifier.Verify(zip, sums, signature, PublicKeys);
        }
        catch (CryptographicException ex)
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, ex.Message);
        }
    }

    private static string ReadVersion(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.GetEntry("version.txt") ?? throw new UpdateRefusedException(ProblemCodes.ValidationFailed, "The package has no version.txt.");
        using var reader = new StreamReader(entry.Open());
        var version = reader.ReadToEnd().Trim();
        return SemanticVersion.TryParse(version, out var parsed)
            ? parsed.ToString()
            : throw new UpdateRefusedException(ProblemCodes.ValidationFailed, $"The package version '{version}' is not valid.");
    }

    /// <summary>Extracts to <c>versions\&lt;v&gt;\</c>; a folder a Runner still uses is the same release and is kept.</summary>
    private string Extract(string zip, string version)
    {
        var folder = Path.Combine(InstallRoot!, "versions", version);
        var size = new FileInfo(zip).Length;
        if (new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace < 2 * size)
        {
            throw new UpdateRefusedException(ProblemCodes.ResourcesInsufficient, $"Not enough free disk for the update ({2 * size / (1024 * 1024)} MB needed).");
        }

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(Path.Combine(folder, "GameWarden.Setup.exe")))
            {
                logger.LogInformation("{Folder} is in use; keeping it.", LogText.Clean(folder));
                return folder;
            }

            throw;
        }

        var partial = folder + ".partial";
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        ZipFile.ExtractToDirectory(zip, partial);
        Directory.Move(partial, folder);
        return folder;
    }

    /// <summary><c>VACUUM INTO</c>: a consistent copy while Core runs, restored by Setup on rollback.</summary>
    private async Task<string> SnapshotDatabaseAsync(string version, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.Value.UpdatesPath, version, "db-before.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);
        await using var db = await database.CreateDbContextAsync(cancellationToken);
#pragma warning disable EF1002 // the path is ours, not user input; VACUUM INTO takes no parameters
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{path.Replace("'", "''", StringComparison.Ordinal)}'", cancellationToken);
#pragma warning restore EF1002
        return path;
    }
}

/// <summary>Daily check for a newer release (10 §6); the panel badge came with M2; the Discord message waits in the backlog (ADR-0019).</summary>
public sealed class UpdateCheckService(UpdateService updates, ILogger<UpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (UpdateService.InstallRoot is null)
        {
            return; // development
        }

        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                var check = await updates.CheckAsync(prerelease: false, stoppingToken);
                if (check.Available)
                {
                    logger.LogInformation("GameWarden {Latest} is available (running {Current}): gamewarden update", check.Latest, check.Current);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogInformation("Update check failed: {Reason}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
