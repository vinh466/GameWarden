using System.Runtime.InteropServices;

namespace GameWarden.Core.Metrics;

/// <summary>A game's process tree (docs/11 §13: CPU and RAM per game process tree), from a Toolhelp snapshot.</summary>
internal static unsafe partial class ProcessTree
{
    private const uint SnapProcess = 0x00000002;
    private static readonly nint InvalidHandle = -1;

    /// <summary><paramref name="root"/> and every process it started, directly or through others.</summary>
    public static IReadOnlyList<int> WithDescendants(int root)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in Snapshot())
        {
            if (pid != parent)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(pid);
            }
        }

        var tree = new List<int> { root };
        for (var i = 0; i < tree.Count; i++)
        {
            foreach (var child in children.GetValueOrDefault(tree[i]) ?? [])
            {
                if (!tree.Contains(child))
                {
                    tree.Add(child);
                }
            }
        }

        return tree;
    }

    private static List<(int Pid, int Parent)> Snapshot()
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
