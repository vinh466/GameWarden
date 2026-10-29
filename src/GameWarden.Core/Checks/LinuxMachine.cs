using System.Globalization;
using GameWarden.Core.Metrics;

namespace GameWarden.Core.Checks;

/// <summary>The development platform's machine probe (ADR-0016): <c>/proc/meminfo</c>, <c>/proc/stat</c> and <c>/proc/&lt;pid&gt;/stat</c>.</summary>
public sealed class LinuxMachine : IMachine
{
    public MachineSnapshot Snapshot(string dataRoot)
    {
        var memory = ProcFs.ParseMemInfo(ReadOrNull("/proc/meminfo"));
        return new MachineSnapshot(
            Environment.ProcessorCount,
            memory?.TotalMb ?? 0,
            memory?.AvailableMb ?? 0,
            MachineResources.DiskFreeMb(dataRoot));
    }

    public (long Idle, long Total)? CpuTimes() => ProcFs.ParseCpuTimes(ReadOrNull("/proc/stat"));

    public IReadOnlyList<int> WithDescendants(int rootPid) => ProcessTree.WithDescendants(rootPid, Processes());

    private static List<(int Pid, int Parent)> Processes()
    {
        var processes = new List<(int, int)>();
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories("/proc");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return processes;
        }

        foreach (var folder in folders)
        {
            if (int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && ProcFs.ParseParentPid(ReadOrNull(Path.Combine(folder, "stat"))) is { } parent)
            {
                processes.Add((pid, parent));
            }
        }

        return processes;
    }

    /// <summary>A process may exit between listing <c>/proc</c> and reading its files.</summary>
    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Parsers for the <c>/proc</c> files <see cref="LinuxMachine"/> reads (proc(5)); pure, so tested on any OS.</summary>
public static class ProcFs
{
    /// <summary><c>MemTotal</c> and <c>MemAvailable</c> of <c>/proc/meminfo</c> (in kB there), in MB.</summary>
    public static (long TotalMb, long AvailableMb)? ParseMemInfo(string? meminfo)
    {
        long? total = null;
        long? available = null;
        foreach (var line in (meminfo ?? string.Empty).Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0
                || line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [{ } number, ..]
                || !long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes))
            {
                continue;
            }

            switch (line[..colon])
            {
                case "MemTotal":
                    total = kilobytes / 1024;
                    break;
                case "MemAvailable":
                    available = kilobytes / 1024;
                    break;
            }
        }

        return total is { } totalMb && available is { } availableMb ? (totalMb, availableMb) : null;
    }

    /// <summary>
    /// The machine line (<c>cpu</c>) of <c>/proc/stat</c> in clock ticks: idle is <c>idle + iowait</c>; the total is
    /// <c>user</c> to <c>steal</c>, as guest time is already counted in <c>user</c> and <c>nice</c>.
    /// </summary>
    public static (long Idle, long Total)? ParseCpuTimes(string? stat)
    {
        var line = (stat ?? string.Empty).Split('\n').FirstOrDefault(line => line.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var ticks = new List<long>();
        foreach (var field in line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8))
        {
            if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            ticks.Add(value);
        }

        // user nice system idle [iowait irq softirq steal]
        return ticks.Count < 4 ? null : (ticks[3] + (ticks.Count > 4 ? ticks[4] : 0), ticks.Sum());
    }

    /// <summary>
    /// The parent PID in <c>/proc/&lt;pid&gt;/stat</c> (<c>pid (comm) state ppid ...</c>). The command name may hold spaces
    /// and parentheses, so the fields are read after its last <c>)</c>.
    /// </summary>
    public static int? ParseParentPid(string? stat)
    {
        var end = stat?.LastIndexOf(')') ?? -1;
        return end >= 0
            && stat![(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, { } parent, ..]
            && int.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                ? pid
                : null;
    }
}
