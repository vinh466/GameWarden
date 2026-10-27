using System.Globalization;
using System.Runtime.InteropServices;
using GameWarden.Core.Adapters;

namespace GameWarden.Core.Checks;

/// <summary>What the VM has right now (05 <c>GET /system</c>, OQ-07).</summary>
public sealed record MachineSnapshot(int CpuCores, long RamTotalMb, long RamFreeMb, long DiskFreeMb);

/// <summary>Minimum and recommended resources of an adapter against the VM (08 §2, OQ-07).</summary>
public static partial class MachineResources
{
    /// <summary>Disk needed for an install is the adapter's figure plus 20 % (08 §2).</summary>
    public const double DiskHeadroom = 1.2;

    public static unsafe MachineSnapshot Read(string dataRoot)
    {
        var memory = new MemoryStatusEx { Length = (uint)sizeof(MemoryStatusEx) };
        var haveMemory = GlobalMemoryStatusEx(&memory);
        long diskFree;
        try
        {
            diskFree = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dataRoot))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            diskFree = 0;
        }

        return new MachineSnapshot(
            Environment.ProcessorCount,
            haveMemory ? (long)(memory.TotalPhys / (1024 * 1024)) : 0,
            haveMemory ? (long)(memory.AvailPhys / (1024 * 1024)) : 0,
            diskFree / (1024 * 1024));
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GlobalMemoryStatusEx(MemoryStatusEx* buffer);
}
