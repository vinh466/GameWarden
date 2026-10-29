using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GameWarden.Core.Metrics;

namespace GameWarden.Core.Checks;

/// <summary>
/// The Windows machine probe (ADR-0016): <c>GlobalMemoryStatusEx</c> for RAM, <c>GetSystemTimes</c> for CPU and a
/// Toolhelp snapshot for a game's process tree (docs/11 §13).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsMachine : IMachine
{
    private const uint SnapProcess = 0x00000002;
    private static readonly nint InvalidHandle = -1;

    public MachineSnapshot Snapshot(string dataRoot)
    {
        var memory = new MemoryStatusEx { Length = (uint)sizeof(MemoryStatusEx) };
        var haveMemory = GlobalMemoryStatusEx(&memory);
        return new MachineSnapshot(
            Environment.ProcessorCount,
            haveMemory ? (long)(memory.TotalPhys / (1024 * 1024)) : 0,
            haveMemory ? (long)(memory.AvailPhys / (1024 * 1024)) : 0,
            MachineResources.DiskFreeMb(dataRoot));
    }

    public (long Idle, long Total)? CpuTimes() =>
        GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel + user) : null; // kernel time includes idle time

    public IReadOnlyList<int> WithDescendants(int rootPid) => ProcessTree.WithDescendants(rootPid, Processes());

    private static List<(int Pid, int Parent)> Processes()
    {
        var processes = new List<(int, int)>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle)
        {
            return processes;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
            for (var more = Process32First(snapshot, &entry); more; more = Process32Next(snapshot, &entry))
            {
                processes.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return processes;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(MemoryStatusEx* buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(nint snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(nint snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
