using System.Globalization;
using GameWarden.Core.Adapters;

namespace GameWarden.Core.Checks;

/// <summary>What the VM has right now (05 <c>GET /system</c>, OQ-07).</summary>
public sealed record MachineSnapshot(int CpuCores, long RamTotalMb, long RamFreeMb, long DiskFreeMb);

/// <summary>Minimum and recommended resources of an adapter against the VM (08 §2, OQ-07); <see cref="IMachine"/> reads the VM.</summary>
public static class MachineResources
{
    /// <summary>Disk needed for an install is the adapter's figure plus 20 % (08 §2).</summary>
    public const double DiskHeadroom = 1.2;

    /// <summary>Free space of the volume that holds <paramref name="dataRoot"/>, in MB; 0 when it cannot be read.</summary>
    public static long DiskFreeMb(string dataRoot)
    {
        try
        {
            // Windows: the drive. Linux: the mount the folder lives on, which need not be "/" (e.g. a separate /home).
            var path = Path.GetFullPath(dataRoot);
            var volume = OperatingSystem.IsWindows()
                ? Path.GetPathRoot(path)!
                : MountOf(path, DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName));
            return new DriveInfo(volume).AvailableFreeSpace / (1024 * 1024);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>The longest of <paramref name="mountPoints"/> that holds the Unix path <paramref name="path"/>; <c>/</c> when none does.</summary>
    public static string MountOf(string path, IEnumerable<string> mountPoints) =>
        mountPoints
            .Where(mount => path == mount || path.StartsWith(mount.TrimEnd('/') + "/", StringComparison.Ordinal))
            .MaxBy(mount => mount.Length) ?? "/";

    /// <summary>
    /// What the machine lacks compared with <paramref name="needed"/>: CPU and free RAM before a start, free disk
    /// (with 20 % headroom) before an install.
    /// </summary>
    public static IReadOnlyList<string> Shortfalls(ResourceAmounts? needed, MachineSnapshot machine, bool forInstall)
    {
        var shortfalls = new List<string>();
        if (needed is null)
        {
            return shortfalls;
        }

        if (forInstall)
        {
            if (needed.DiskMb is { } disk && machine.DiskFreeMb < (long)(disk * DiskHeadroom))
            {
                shortfalls.Add(string.Create(CultureInfo.InvariantCulture, $"free disk {(long)(disk * DiskHeadroom)} MB (have {machine.DiskFreeMb} MB)"));
            }

            return shortfalls;
        }

        if (needed.CpuCores is { } cores && machine.CpuCores < cores)
        {
            shortfalls.Add(string.Create(CultureInfo.InvariantCulture, $"{cores} CPU cores (have {machine.CpuCores})"));
        }

        if (needed.RamMb is { } ram && machine.RamFreeMb < ram)
        {
            shortfalls.Add(string.Create(CultureInfo.InvariantCulture, $"{ram} MB free RAM (have {machine.RamFreeMb} MB)"));
        }

        return shortfalls;
    }
}
