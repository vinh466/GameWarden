using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Management;
using System.Runtime.Versioning;
using GameWarden.Core.Checks;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Agents;

/// <summary>
/// Read-only host diagnostics for the Agent (docs/15). The first slice covers the system identity and a bounded
/// process list; Event Log, firewall and storage probes join the same service in the next phase.
/// </summary>
public sealed class AgentDiagnosticsService(
    IOptions<GameWardenOptions> options,
    IsolationMode isolation,
    IMachine machine,
    AgentRedactor redactor,
    TimeProvider time)
{
    public AgentSystemDiagnostics SystemInfo()
    {
        var snapshot = machine.Snapshot(options.Value.DataRoot);
        return new AgentSystemDiagnostics(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            Environment.MachineName,
            Environment.TickCount64 / 1000,
            Environment.ProcessId,
            options.Value.DataRoot,
            isolation.Enabled,
            options.Value.ResolvedVpnRanges,
            options.Value.ResolvedPanelAccessNetworks,
            new AgentMachineView(snapshot.CpuCores, snapshot.RamTotalMb, snapshot.RamFreeMb, snapshot.DiskFreeMb),
            time.GetUtcNow());
    }

    public AgentStorageView Storage()
    {
        var now = time.GetUtcNow();
        var volumes = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => new AgentStorageVolume(
                drive.Name,
                drive.DriveType.ToString(),
                drive.TotalSize / (1024 * 1024),
                drive.AvailableFreeSpace / (1024 * 1024)))
            .ToList();

        var root = options.Value.DataRoot;
        var top = new[] { "instances", "logs", "backups", "updates", "cache", "tmp", "db" }
            .Select(folder => Measure(Path.Combine(root, folder), folder, now))
            .ToList();

        var instances = new List<AgentFolderSize>();
        var instancesRoot = Path.Combine(root, "instances");
        if (Directory.Exists(instancesRoot))
        {
            foreach (var directory in new DirectoryInfo(instancesRoot).EnumerateDirectories().OrderBy(info => info.Name))
            {
                foreach (var (folder, label) in new[]
                         {
                             (InstancePaths.For(root, directory.Name).Data, "data"),
                             (InstancePaths.For(root, directory.Name).Server, "server"),
                             (InstancePaths.For(root, directory.Name).Config, "config"),
                             (InstancePaths.For(root, directory.Name).Mods, "mods"),
                             (InstancePaths.For(root, directory.Name).Logs, "logs"),
                         })
                {
                    instances.Add(Measure(folder, Path.Combine(directory.Name, label), now));
                }
            }
        }

        return new AgentStorageView(volumes, top, instances, now);
    }

    public AgentNetworkView Network()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Select(network => new AgentNetworkInterface(
                network.Name,
                network.Description,
                network.OperationalStatus.ToString(),
                [.. network.GetIPProperties().UnicastAddresses.Select(address => address.Address.ToString())]))
            .ToList();

        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = properties.GetActiveTcpListeners().Select(endpoint => new AgentListener("tcp", endpoint.Address.ToString(), endpoint.Port))
            .Concat(properties.GetActiveUdpListeners().Select(endpoint => new AgentListener("udp", endpoint.Address.ToString(), endpoint.Port)))
            .OrderBy(listener => listener.Port)
            .ToList();
        var connections = properties.GetActiveTcpConnections()
            .OrderBy(connection => connection.RemoteEndPoint.Port)
            .Take(200)
            .Select(connection => new AgentConnection(
                Format(connection.LocalEndPoint),
                Format(connection.RemoteEndPoint),
                connection.State.ToString()))
            .ToList();

        return new AgentNetworkView(interfaces, listeners, connections, time.GetUtcNow());
    }

    public AgentEventsView Events(string? log, string? level, int? hours, int? limit)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AgentEventsView(false, "Windows Event Log is available on the Windows VM only.", [], time.GetUtcNow());
        }

        return ReadEvents(log, level, hours, limit);
    }

    [SupportedOSPlatform("windows")]
    private AgentEventsView ReadEvents(string? log, string? level, int? hours, int? limit)
    {
        var logName = (log ?? "Application").Trim();
        if (!string.Equals(logName, "Application", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(logName, "System", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentRequestException(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "log must be Application or System.");
        }

        var window = Math.Clamp(hours ?? 24, 1, 168);
        var take = Math.Clamp(limit ?? 100, 1, 300);
        var query = new EventLogQuery(logName, PathType.LogName, $"*[System[TimeCreated[timediff(@SystemTime) <= {window * 60 * 60 * 1000}]]]");
        var entries = new List<AgentEventEntry>();
        try
        {
            using var reader = new EventLogReader(query);
            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    var eventLevel = Level(record.Level);
                    if (level is { Length: > 0 } wanted && !string.Equals(eventLevel, wanted.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entries.Add(new AgentEventEntry(
                        record.TimeCreated is { } created ? new DateTimeOffset(created.ToUniversalTime()) : time.GetUtcNow(),
                        eventLevel,
                        redactor.Redact(record.ProviderName),
                        record.Id,
                        redactor.Redact(record.FormatDescription())));
                    if (entries.Count >= take)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or IOException)
        {
            return new AgentEventsView(true, redactor.Redact(ex.Message), [], time.GetUtcNow());
        }

        return new AgentEventsView(true, null, entries, time.GetUtcNow());
    }

    private static string Level(byte? value) => value switch
    {
        1 => "critical",
        2 => "error",
        3 => "warning",
        4 => "information",
        5 => "verbose",
        _ => "unknown",
    };

    private static string Format(IPEndPoint endpoint) =>
        endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{endpoint.Address}]:{endpoint.Port}"
            : $"{endpoint.Address}:{endpoint.Port}";

    private static AgentFolderSize Measure(string path, string label, DateTimeOffset now)
    {
        if (!Directory.Exists(path))
        {
            return new AgentFolderSize(label, 0, null, false);
        }

        var watch = Stopwatch.StartNew();
        long bytes = 0;
        DateTimeOffset? newest = null;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(3))
                {
                    return new AgentFolderSize(label, bytes / (1024 * 1024), newest, true);
                }

                var info = new FileInfo(file);
                bytes += info.Length;
                var modified = new DateTimeOffset(info.LastWriteTimeUtc);
                if (newest is null || modified > newest)
                {
                    newest = modified;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return new AgentFolderSize(label, bytes / (1024 * 1024), newest, false);
    }

    public IReadOnlyList<AgentProcessInfo> Processes(int? limit)
    {
        var take = Math.Clamp(limit ?? 100, 1, 300);
        var commandLines = OperatingSystem.IsWindows() ? WindowsCommandLines() : new Dictionary<int, string>();
        var processes = new List<AgentProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                DateTimeOffset? started = null;
                try
                {
                    started = process.StartTime.ToUniversalTime();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }

                processes.Add(new AgentProcessInfo(
                    process.Id,
                    process.ProcessName,
                    process.WorkingSet64 / (1024 * 1024),
                    started,
                    redactor.Redact(CommandLine(process.Id, commandLines))));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // The process ended while we looked at it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return [.. processes.OrderByDescending(process => process.WorkingSetMb).Take(take)];
    }

    private static string? CommandLine(int pid, IReadOnlyDictionary<int, string> windows)
    {
        if (OperatingSystem.IsWindows())
        {
            return windows.GetValueOrDefault(pid);
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var bytes = File.ReadAllBytes($"/proc/{pid}/cmdline");
                var text = System.Text.Encoding.UTF8.GetString(bytes).Replace('\0', ' ').Trim();
                return text.Length == 0 ? null : text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static Dictionary<int, string> WindowsCommandLines()
    {
        var lines = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["ProcessId"] is { } pid && item["CommandLine"] is { } commandLine)
                    {
                        lines[Convert.ToInt32(pid)] = commandLine.ToString() ?? string.Empty;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
        }

        return lines;
    }
}
