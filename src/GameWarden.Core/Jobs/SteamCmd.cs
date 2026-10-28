using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameWarden.Contracts;
using GameWarden.Contracts.Runner;
using GameWarden.Core.Isolation;
using GameWarden.Core.Supervision;

namespace GameWarden.Core.Jobs;

/// <summary>Retries and limits for SteamCMD runs (03 §5: three tries with back-off).</summary>
public sealed record SteamCmdPolicy(IReadOnlyList<TimeSpan> RetryDelays, TimeSpan RunTimeout)
{
    public static SteamCmdPolicy Default { get; } = new([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)], TimeSpan.FromHours(2));

    public int Attempts => RetryDelays.Count + 1;
}

public sealed record SteamCmdResult(bool Success, bool Retryable, string Message);

/// <summary>
/// SteamCMD, run as gamewarden-run inside a Runner like a game (ADR-0008): the Runner gives it a console and
/// waits for the processes it starts when it updates itself. Callers hold the global SteamCMD lock (OQ-22).
/// </summary>
public sealed partial class SteamCmd(SupervisorContext context, IHttpClientFactory http, SteamCmdPolicy policy, ILogger<SteamCmd> logger)
{
    // Not worth retrying: wrong app, no licence, no disk space (0x202).
    private static readonly string[] PermanentErrors = ["No subscription", "Invalid platform", "0x202", "Invalid app"];

    private bool _selfUpdated;

    public string ExecutablePath => context.Options.ResolvedSteamCmdPath;

    /// <summary>Downloads SteamCMD when missing, then lets it update itself once per Core run.</summary>
    public async Task EnsureReadyAsync(string runId, JobLog log, Action<Processes.RunnerProcess> runnerStarted, CancellationToken cancellationToken)
    {
        if (!context.Files.Run(() => File.Exists(ExecutablePath)))
        {
            await DownloadAsync(log, cancellationToken);
        }

        if (_selfUpdated)
        {
            return;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            log.Write($"SteamCMD self-update (try {attempt} of 3).");
            var exitCode = await RunAsync($"{runId}-self{attempt}", ["+quit"], line => log.Write(line), runnerStarted, cancellationToken);
            if (exitCode == 0)
            {
                _selfUpdated = true;
                return;
            }

            // SteamCMD exits non-zero right after it replaced itself; the next run is the real check.
            log.Write($"SteamCMD exited with code {exitCode}.");
        }

        throw new InvalidOperationException("SteamCMD did not finish its self-update after 3 tries; see the job log.");
    }

    /// <summary><c>+app_update</c> into <paramref name="installDir"/>, reporting progress in percent.</summary>
    public async Task<SteamCmdResult> AppUpdateAsync(
        string runId,
        int appId,
        string installDir,
        bool validate,
        JobLog log,
        Action<double> progress,
        Action<Processes.RunnerProcess> runnerStarted,
        CancellationToken cancellationToken)
    {
        var app = appId.ToString(CultureInfo.InvariantCulture);
        List<string> arguments = ["+force_install_dir", installDir, "+login", "anonymous", "+app_update", app];
        if (validate)
        {
            arguments.Add("validate");
        }

        arguments.Add("+quit");
        var succeeded = false;
        string? error = null;
        var exitCode = await RunAsync(runId, arguments, line =>
        {
            log.Write(line);
            if (Progress().Match(line) is { Success: true } match)
            {
                progress(double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture));
            }
            else if (Success().IsMatch(line))
            {
                succeeded = true;
            }
            else if (line.Contains("ERROR!", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Error!", StringComparison.Ordinal))
            {
                error = line.Trim();
            }
        }, runnerStarted, cancellationToken);

        if (succeeded && exitCode == 0)
        {
            progress(100);
            return new SteamCmdResult(true, false, $"App {app} is installed and up to date.");
        }

        var message = error ?? $"SteamCMD exited with code {exitCode} without reporting success.";
        return new SteamCmdResult(false, !PermanentErrors.Any(permanent => message.Contains(permanent, StringComparison.OrdinalIgnoreCase)), message);
    }

    /// <summary>Runs SteamCMD with <paramref name="arguments"/> in a Runner and relays its console; returns its exit code.</summary>
    private async Task<int> RunAsync(string runId, IReadOnlyList<string> arguments, Action<string> onLine, Action<Processes.RunnerProcess> runnerStarted, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(context.Options.SteamCmdFolder, "runner", runId);
        var spec = new LaunchSpec
        {
            InstanceId = runId,
            Executable = ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
            LogDirectory = folder,
            StateDirectory = folder,
            LingerSeconds = 60,
        };
        var specPath = Path.Combine(folder, "launchSpec.json");
        var json = JsonSerializer.Serialize(spec, ContractJson.Options);
        context.Files.Run(() =>
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(specPath, json);
        });

        var runner = context.ProcessHost.StartRunner(context.Options.ResolvedRunnerPath, specPath, spec.WorkingDirectory);
        runnerStarted(runner);
        try
        {
            await using var client = await RunnerClient.ConnectAsync(runId, runner.Pid, context.RunnerConnectTimeout, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(policy.RunTimeout);

            // Lines written before we connected come from the tail; the same line may also arrive as an event.
            var seen = new HashSet<OutputLine>();
            foreach (var line in await client.GetTailAsync(1000, timeout.Token))
            {
                seen.Add(line);
                onLine(line.Text);
            }

            var exitCode = client.Welcome.Status.ExitCode;
            if (exitCode is null)
            {
                try
                {
                    await foreach (var message in client.Events.ReadAllAsync(timeout.Token))
                    {
                        if (message is OutputEvent output && !seen.Contains(output.Line))
                        {
                            onLine(output.Line.Text);
                        }
                        else if (message is ExitedEvent exited)
                        {
                            exitCode = exited.ExitCode;
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    onLine($"SteamCMD ran longer than {policy.RunTimeout}; killing it.");
                    await client.KillAsync(CancellationToken.None);
                    return -1;
                }
            }

            try
            {
                await client.AckExitAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or RunnerCommandRejectedException or ObjectDisposedException)
            {
                // The Runner quits as soon as it gets the acknowledgement, sometimes before its reply arrives.
            }

            return exitCode ?? -1;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or RunnerIdentityException or RunnerCommandRejectedException)
        {
            logger.LogWarning(ex, "SteamCMD Runner {RunId} failed.", runId);
            context.ProcessHost.Kill(runner.Pid);
            onLine($"Lost the SteamCMD Runner: {ex.Message}");
            return -1;
        }
        finally
        {
            try
            {
                context.Files.Run(() => Directory.Delete(folder, recursive: true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The Runner may still hold its log for a moment; the folder is small.
            }
        }
    }

    private async Task DownloadAsync(JobLog log, CancellationToken cancellationToken)
    {
        byte[]? bytes = null;
        var failures = new List<string>();
        foreach (var url in context.Options.ResolvedSteamCmdDownloadUrls)
        {
            log.Write($"Downloading SteamCMD from {url}.");
            try
            {
                bytes = await http.CreateClient("steamcmd").GetByteArrayAsync(new Uri(url), cancellationToken);
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.Write($"Failed: {ex.Message}");
                failures.Add($"{new Uri(url).Host}: {ex.Message}");
            }
        }

        if (bytes is null)
        {
            throw new InvalidOperationException(
                $"SteamCMD could not be downloaded ({string.Join("; ", failures)}). Check the VM's internet access and DNS: gamewarden system shows whether a Steam host is blocked.");
        }

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.GetEntry("steamcmd.exe") is null)
        {
            throw new InvalidDataException("The SteamCMD download has no steamcmd.exe.");
        }

        var folder = Path.GetDirectoryName(ExecutablePath)!;
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        context.Files.Run(() =>
        {
            Directory.CreateDirectory(folder);
            foreach (var entry in zip.Entries.Where(entry => entry.Name.Length > 0))
            {
                var target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"The SteamCMD download has an unsafe path: {entry.FullName}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        });
        log.Write($"SteamCMD extracted to {folder} ({bytes.Length / 1024} KB download).");
    }

    [GeneratedRegex(@"progress: (?<percent>\d+(\.\d+)?) \(")]
    private static partial Regex Progress();

    [GeneratedRegex(@"Success! App '\d+'")]
    private static partial Regex Success();
}
