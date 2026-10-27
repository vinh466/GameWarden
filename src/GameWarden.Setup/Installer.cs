using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GameWarden.Contracts;
using Microsoft.Win32;

namespace GameWarden.Setup;

internal sealed record InstallOptions(string? FromZip, string? DataRoot, bool Quiet, bool DryRun);

/// <summary>Install, apply (update, rollback, switch), uninstall: one code path for all of them (ADR-0013).</summary>
internal sealed class Installer(SetupLog log)
{
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(120);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // ----- install ------------------------------------------------------------------------------

    public async Task<int> InstallAsync(InstallOptions options)
    {
        using var payload = Payload.Open(options.FromZip);
        if (payload is null)
        {
            log.Error("This Setup has no package inside. Use the GameWarden-Setup-<version>.exe from the Releases page, or pass --from <zip>.");
            return 2;
        }

        var version = payload.Version;
        var state = InstallState.Load();
        var dataRoot = Path.GetFullPath(options.DataRoot ?? state?.DataRoot ?? InstallLayout.DefaultDataRoot).TrimEnd('\\');
        if (state is not null && !string.Equals(state.DataRoot, dataRoot, StringComparison.OrdinalIgnoreCase))
        {
            log.Error($"GameWarden is installed with the data root {state.DataRoot}. Uninstall first to use another one.");
            return 2;
        }

        var layout = new InstallLayout(state?.InstallRoot ?? InstallLayout.DefaultInstallRoot, dataRoot);
        log.Info($"GameWarden {version}");
        log.Info($"  Program files  {layout.VersionFolder(version)}");
        log.Info($"  Data           {layout.DataRoot}   (game files, saves, backups, logs)");
        log.Info($"  Service        {InstallLayout.ServiceName}, automatic start, port {layout.PanelPort()} on 127.0.0.1");
        log.Info(state?.CurrentVersion is { } current
            ? $"  Installed now  {current} — Setup switches to {version} and rolls back if it does not come up."
            : "  New install: also creates the local user gamewarden-run that runs the game servers (ADR-0008).");
        if (options.DryRun)
        {
            log.Info("Dry run: nothing changed.");
            return 0;
        }

        if (!options.Quiet && !Confirm("Continue? [Y/n] ", defaultYes: true))
        {
            return 1;
        }

        var problem = CheckMachine(layout, payload, upgrade: state is not null);
        if (problem is not null)
        {
            log.Error(problem);
            return 1;
        }

        Machine.CreateProtectedFolder(layout.DataRoot);
        Machine.CreateProtectedFolder(InstallLayout.ProgramDataFolder);
        log.AttachTo(layout.DataRoot);
        Directory.CreateDirectory(layout.VersionsPath);

        if (Directory.Exists(layout.VersionFolder(version)))
        {
            // Files of a version that Runners run from cannot be replaced.
            if (RunnersIn(layout) is not { } inUse || inUse.Contains(version))
            {
                log.Error($"{version} is already installed and running game servers use it; stop them (gamewarden stop <id>) and run Setup again.");
                return 1;
            }

            if (state?.CurrentVersion == version)
            {
                log.Info($"Reinstalling {version}.");
                StopService();
            }
        }

        log.Info($"Copying files to {layout.VersionFolder(version)} ...");
        payload.ExtractTo(layout.VersionFolder(version));

        if (state?.CurrentVersion is { } from && from != version)
        {
            return await ApplyAsync(version, snapshot: null); // an upgrade with the new Setup.exe: same path as a self-update
        }

        log.Info("Registering the service ...");
        ServiceControl.Install(layout.ServiceCommandLine(version));
        ServiceControl.SetDataRoot(layout.DataRoot);
        SwitchFiles(layout, version);
        new InstallState(layout.InstallRoot, layout.DataRoot, version, state?.PreviousVersion).Save();

        log.Info("Starting GameWarden Core ...");
        var failure = await StartAndCheckAsync(layout, version);
        WriteResult(layout, new UpdateResult(state?.CurrentVersion, version, failure is null ? "succeeded" : "failed", failure ?? "Installed.", DateTimeOffset.UtcNow));
        if (failure is not null)
        {
            log.Error($"GameWarden Core did not come up: {failure}");
            log.Error($"See {Path.Combine(layout.DataRoot, "logs", "core")} and the Windows Event Log (Application).");
            return 1;
        }

        Prune(layout, version, state?.PreviousVersion);
        log.Info(string.Empty);
        log.Info($"GameWarden {version} is installed and running.");
        log.Info("Open a NEW terminal as Administrator and try:  gamewarden system");
        log.Info("Valheim:  gamewarden create valheim-main --adapter valheim --set serverName=MyServer --set world=MyWorld");
        log.Info("          gamewarden secret valheim-main password");
        log.Info("          gamewarden install valheim-main --wait");
        log.Info("          gamewarden start valheim-main");
        return 0;
    }

    // ----- apply (update, rollback, version use) -------------------------------------------------

    /// <summary>
    /// Switches the service to <paramref name="version"/> (already in <c>versions\</c>) and checks that it comes up;
    /// otherwise restores <paramref name="snapshot"/> and switches back (10 §6). Games keep running throughout.
    /// </summary>
    public async Task<int> ApplyAsync(string version, string? snapshot)
    {
        var state = InstallState.Load();
        if (state is null)
        {
            log.Error("GameWarden is not installed.");
            return 2;
        }

        var layout = state.Layout;
        log.AttachTo(layout.DataRoot);
        if (!File.Exists(layout.CoreExe(version)))
        {
            log.Error($"{layout.VersionFolder(version)} has no GameWarden.Core.exe.");
            return 2;
        }

        var from = state.CurrentVersion;
        log.Info($"Switching GameWarden from {from ?? "(none)"} to {version}.");
        StopService();
        if (snapshot is null && File.Exists(layout.DatabasePath))
        {
            // Core's self-update brings a VACUUM INTO snapshot; a Setup.exe upgrade copies the stopped database.
            snapshot = Path.Combine(layout.UpdatesPath, version, "db-before.db");
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.Copy(layout.DatabasePath, snapshot, overwrite: true);
            CopyIfExists(layout.DatabasePath + "-wal", snapshot + "-wal");
            log.Info($"Database snapshot: {snapshot}");
        }

        SwitchTo(layout, state with { CurrentVersion = version, PreviousVersion = from }, version);

        var failure = await StartAndCheckAsync(layout, version);
        if (failure is null)
        {
            WriteResult(layout, new UpdateResult(from, version, "succeeded", $"Now running {version}.", DateTimeOffset.UtcNow));
            Prune(layout, version, from);
            log.Info($"GameWarden {version} is running.");
            return 0;
        }

        log.Error($"{version} did not come up: {failure}");
        if (from is null || from == version)
        {
            WriteResult(layout, new UpdateResult(from, version, "failed", failure, DateTimeOffset.UtcNow));
            return 1;
        }

        log.Info($"Rolling back to {from} ...");
        StopService();
        if (snapshot is not null && File.Exists(snapshot))
        {
            RestoreDatabase(layout, snapshot);
        }

        SwitchTo(layout, state, from);
        var rollbackFailure = await StartAndCheckAsync(layout, from);
        var outcome = rollbackFailure is null ? "rolledBack" : "failed";
        var message = rollbackFailure is null
            ? $"{version} did not come up ({failure}); back on {from}."
            : $"{version} did not come up ({failure}), and neither did {from} ({rollbackFailure}).";
        WriteResult(layout, new UpdateResult(from, version, outcome, message, DateTimeOffset.UtcNow));
        log.Error(message);
        return 1;
    }

    // ----- uninstall -------------------------------------------------------------------------------

    public async Task<int> UninstallAsync(bool removeData, bool quiet)
    {
        var state = InstallState.Load();
        var layout = state?.Layout ?? new InstallLayout(InstallLayout.DefaultInstallRoot, InstallLayout.DefaultDataRoot);
        log.Info("Uninstall GameWarden: game servers stop, the service, firewall rules and the user gamewarden-run are removed.");
        log.Info(removeData
            ? $"ALL DATA in {layout.DataRoot} is deleted too: worlds, backups, logs."
            : $"Your data in {layout.DataRoot} (worlds, backups, logs) is kept.");
        if (!quiet && !Confirm("Uninstall? [y/N] ", defaultYes: false))
        {
            return 1;
        }

        if (Directory.Exists(layout.DataRoot))
        {
            log.AttachTo(layout.DataRoot);
        }

        string? cleanupProblem = "the service is not running";
        if (ServiceControl.Status() == System.ServiceProcess.ServiceControllerStatus.Running)
        {
            log.Info("Asking Core to stop the games and clean up (up to 2 minutes) ...");
            cleanupProblem = await CoreApi.UninstallCleanupAsync(layout, log);
        }

        if (cleanupProblem is not null)
        {
            log.Info($"Core could not clean up ({cleanupProblem}); removing firewall rules and the user directly.");
            Run("powershell.exe", "-NoProfile -NonInteractive -Command \"Remove-NetFirewallRule -Group 'GameWarden' -ErrorAction SilentlyContinue\"");
            Run("net.exe", "user gamewarden-run /delete");
            using var userList = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList", writable: true);
            userList?.DeleteValue("gamewarden-run", throwOnMissingValue: false);
        }

        if (ServiceControl.Exists())
        {
            StopService();
            ServiceControl.Delete();
            log.Info("Removed the service.");
        }

        KillProcessesUnder(layout.InstallRoot); // Runners still running end their games with them
        Machine.SetOnPath(layout.InstallRoot, present: false);
        Machine.DeleteUninstallEntry();
        InstallState.Delete();
        DeleteQuietly(InstallLayout.ProgramDataFolder);
        if (removeData)
        {
            log.Dispose();
            DeleteQuietly(layout.DataRoot);
            Console.WriteLine($"Deleted {layout.DataRoot}.");
        }

        // This exe runs from the install folder, so the folder goes once it has exited.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 6 127.0.0.1 >nul & rmdir /s /q \"{layout.InstallRoot}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        });
        Console.WriteLine("GameWarden is uninstalled.");
        return 0;
    }

    public int Status()
    {
        var state = InstallState.Load();
        if (state is null)
        {
            Console.WriteLine("GameWarden is not installed.");
            return 1;
        }

        var layout = state.Layout;
        Console.WriteLine($"Install root     {layout.InstallRoot}");
        Console.WriteLine($"Data root        {layout.DataRoot}");
        Console.WriteLine($"Current version  {state.CurrentVersion}");
        Console.WriteLine($"Previous         {state.PreviousVersion ?? "-"}");
        Console.WriteLine($"Installed        {string.Join(", ", layout.InstalledVersions())}");
        Console.WriteLine($"Service          {ServiceControl.Status()?.ToString() ?? "not installed"}");
        if (File.Exists(layout.LastResultPath))
        {
            Console.WriteLine($"Last result      {File.ReadAllText(layout.LastResultPath).ReplaceLineEndings(" ")}");
        }

        return 0;
    }

    // ----- steps -----------------------------------------------------------------------------------

    private static string? CheckMachine(InstallLayout layout, Payload payload, bool upgrade)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return "GameWarden needs Windows 10, Windows 11 or Windows Server 2016 or later.";
        }

        var drive = new DriveInfo(Path.GetPathRoot(layout.InstallRoot)!);
        if (drive.AvailableFreeSpace < 2 * payload.UncompressedSize)
        {
            return $"Not enough free space on {drive.Name}: {payload.UncompressedSize * 2 / (1024 * 1024)} MB needed.";
        }

        if (!upgrade && PortInUse(layout.PanelPort()))
        {
            return $"Port {layout.PanelPort()} is already used by another program; GameWarden needs it on 127.0.0.1.";
        }

        return null;
    }

    private static bool PortInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    private void StopService()
    {
        if (!ServiceControl.Exists())
        {
            return;
        }

        log.Info("Stopping the service (games keep running) ...");
        if (!ServiceControl.Stop(ServiceStopTimeout))
        {
            log.Error("The service did not stop in time; ending GameWarden.Core.");
            foreach (var core in Process.GetProcessesByName("GameWarden.Core"))
            {
                using (core)
                {
                    core.Kill();
                    core.WaitForExit(10_000);
                }
            }
        }
    }

    private void SwitchTo(InstallLayout layout, InstallState state, string version)
    {
        ServiceControl.SetCommandLine(layout.ServiceCommandLine(version));
        SwitchFiles(layout, version);
        state.Save();
    }

    private static void SwitchFiles(InstallLayout layout, string version)
    {
        File.WriteAllText(layout.ShimPath, SetupPlan.ShimText(version));
        Machine.SetOnPath(layout.InstallRoot, present: true);
        Machine.WriteUninstallEntry(layout, version);
    }

    private async Task<string?> StartAndCheckAsync(InstallLayout layout, string version)
    {
        try
        {
            ServiceControl.Start(TimeSpan.FromSeconds(60));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException or System.ComponentModel.Win32Exception)
        {
            return $"the service did not start: {ex.Message}";
        }

        return await CoreApi.WaitForVersionAsync(layout, version, HealthTimeout, log);
    }

    private void RestoreDatabase(InstallLayout layout, string snapshot)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            File.Delete(layout.DatabasePath + suffix);
        }

        File.Copy(snapshot, layout.DatabasePath, overwrite: true);
        CopyIfExists(snapshot + "-wal", layout.DatabasePath + "-wal");
        log.Info($"Restored the database from {snapshot}.");
    }

    private static void CopyIfExists(string from, string to)
    {
        if (File.Exists(from))
        {
            File.Copy(from, to, overwrite: true);
        }
    }

    private void Prune(InstallLayout layout, string current, string? previous)
    {
        var doomed = SetupPlan.VersionsToDelete(layout.InstalledVersions(), current, previous, RunnersIn(layout));
        foreach (var version in doomed)
        {
            try
            {
                Directory.Delete(layout.VersionFolder(version), recursive: true);
                log.Info($"Removed old version {version}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Info($"Could not remove old version {version} yet: {ex.Message}");
            }
        }
    }

    /// <summary>Versions some Runner runs from; null when a Runner's path cannot be read (then nothing is pruned).</summary>
    private static HashSet<string>? RunnersIn(InstallLayout layout)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Process.EnterDebugMode(); // read the paths of processes owned by gamewarden-run
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        foreach (var runner in Process.GetProcessesByName("GameWarden.Runner"))
        {
            using (runner)
            {
                try
                {
                    if (runner.MainModule?.FileName is { } path && SetupPlan.VersionOf(path, layout.VersionsPath) is { } version)
                    {
                        versions.Add(version);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    return null;
                }
            }
        }

        return versions;
    }

    private void KillProcessesUnder(string installRoot)
    {
        var root = Path.GetFullPath(installRoot).TrimEnd('\\') + "\\";
        foreach (var name in new[] { "GameWarden.Core", "GameWarden.Runner" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            process.Kill(entireProcessTree: true);
                            log.Info($"Ended {name} (pid {process.Id}).");
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
        }
    }

    private void WriteResult(InstallLayout layout, UpdateResult result)
    {
        try
        {
            Directory.CreateDirectory(layout.UpdatesPath);
            File.WriteAllText(layout.LastResultPath, JsonSerializer.Serialize(result, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Could not write {layout.LastResultPath}: {ex.Message}");
        }
    }

    private void Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true });
            process?.WaitForExit(60_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Error($"{file} {arguments}: {ex.Message}");
        }
    }

    private static void DeleteQuietly(string folder)
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
            Console.Error.WriteLine($"Could not delete {folder}: {ex.Message}");
        }
    }

    private static bool Confirm(string question, bool defaultYes)
    {
        if (Console.IsInputRedirected)
        {
            return defaultYes;
        }

        Console.Write(question);
        var answer = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? defaultYes : answer.StartsWith('y') || answer.StartsWith('Y');
    }
}
