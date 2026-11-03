using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Core.Hosting;
using GameWarden.Core.Jobs;
using GameWarden.Core.Mods;
using GameWarden.Core.Notifications;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Updates;

/// <param name="Version">A release to install instead of the newest one.</param>
/// <param name="FromZip">A package on this machine instead of GitHub (dev builds, 10 §7).</param>
/// <param name="AllowUnsigned">Skip the signature check for <paramref name="FromZip"/>; the Owner's own dev builds only.</param>
public sealed record UpdateRequest(string? Version = null, string? FromZip = null, bool AllowUnsigned = false);

public sealed record UpdateCheck(
    string Current,
    string? Latest,
    bool Available,
    string? Url,
    string? Notes,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CheckedAt);

public sealed record UpdateStarted(string From, string To, string Message);

public sealed class UpdateRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Development self-update (ADR-0027, 10 §6): find the newest release, verify it, extract it next to the current
/// version, then hand over to the new version's Setup engine. There is no rollback and no previous-version switch:
/// the development line only moves forward, and a failed release is fixed by installing the next one.
/// </summary>
public sealed class UpdateService(
    ReleaseFeed feed,
    JobService jobs,
    ModsService mods,
    IHttpClientFactory http,
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

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var current = SemanticVersion.Parse(CoreInfo.Version);
        var latest = await feed.LatestAsync(cancellationToken);
        LastCheck = new UpdateCheck(
            current.ToString(),
            latest?.Version.ToString(),
            latest is not null && latest.Version > current,
            latest?.Url,
            latest?.Notes,
            latest?.PublishedAt,
            DateTimeOffset.UtcNow);
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
            if (jobs.AnyRunning || mods.AnyRunning)
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
            var setup = Path.Combine(folder, "GameWarden.Setup.exe");
            var info = new ProcessStartInfo(setup)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = folder,
            };
            foreach (var argument in new[] { "apply", version, "--quiet" })
            {
                info.ArgumentList.Add(argument);
            }

            // Setup stops this service, so it must not depend on Core: it is a separate process, not in a job.
            using (Process.Start(info) ?? throw new InvalidOperationException($"Could not start {setup}."))
            {
            }

            logger.LogInformation("Updating from {From} to {To}: Setup takes over.", current, version);
            return new UpdateStarted(current, version, $"Updating to {version}. Core restarts in a moment; games keep running.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The Setup engine that applies an update is a Windows program; the development platform has no self-update (ADR-0016).</summary>
    private static void RefuseOnDevelopmentPlatform(string what)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UpdateRefusedException(ProblemCodes.InvalidState, $"{what} in an installed GameWarden only, on Windows; this Core runs on the development platform (ADR-0016).");
        }
    }

    /// <summary>
    /// The version rebuilt from numbers and known pre-release words only (<c>0.2.0</c>, <c>0.2.0-dev.42</c>,
    /// <c>0.2.0-rc.1</c>), so nothing from a request or a package reaches the file system or a command line as is.
    /// </summary>
    internal static string Canonical(string version)
    {
        if (!SemanticVersion.TryParse(version, out var parsed))
        {
            throw new UpdateRefusedException(ProblemCodes.ValidationFailed, "The package version is not a version like 1.2.3 or 1.2.3-dev.1.");
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
            : await feed.LatestAsync(cancellationToken);
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
        long uncompressed;
        using (var archive = ZipFile.OpenRead(zip))
        {
            uncompressed = archive.Entries.Sum(entry => entry.Length);
        }

        if (new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace < uncompressed + new FileInfo(zip).Length)
        {
            throw new UpdateRefusedException(ProblemCodes.ResourcesInsufficient, $"Not enough free disk for the update ({uncompressed / (1024 * 1024)} MB needed).");
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
}

/// <summary>Daily check for a newer release (10 §6); the panel badge came with M2, and docs/18 publishes the event.</summary>
public sealed class UpdateCheckService(
    UpdateService updates,
    NotificationService notifications,
    ILogger<UpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (UpdateService.InstallRoot is null)
        {
            return; // development
        }

        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                var check = await updates.CheckAsync(stoppingToken);
                if (check.Available)
                {
                    logger.LogInformation("GameWarden {Latest} is available (running {Current}): gamewarden update", check.Latest, check.Current);
                    notifications.Publish(
                        "update.available",
                        "warning",
                        $"GameWarden {check.Latest} is available",
                        $"Running {check.Current}. Use `gamewarden update` or the System page to install it.",
                        url: check.Url,
                        dedupeKey: $"update.available:{check.Latest}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A flaky GitHub answer must never stop the service that supervises the games.
                logger.LogInformation("Update check failed: {Reason}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
