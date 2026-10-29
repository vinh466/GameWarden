using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameWarden.Runner;

/// <summary>
/// Puts the Runner into a Job Object with KILL_ON_JOB_CLOSE. Children (the game and whatever it starts)
/// join the job automatically, so when the Runner dies for any reason the OS closes the job handle and
/// kills them too: no orphaned game processes (02 §4.2).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class JobObject
{
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    // Never closed on purpose: closing it would kill the Runner itself. The OS closes it when the Runner exits.
    private static nint s_job;

    public static void KillChildrenWhenRunnerDies(DiagnosticLog diagnostics)
    {
        var job = CreateJobObjectW(0, null);
        if (job == 0)
        {
            diagnostics.Write($"CreateJobObject failed (error {Marshal.GetLastPInvokeError()}); the game will not die with the Runner.");
            return;
        }

        var limits = default(JobObjectExtendedLimit);
        limits.Basic.LimitFlags = JobObjectLimitKillOnJobClose;
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobObjectExtendedLimit)))
        {
            diagnostics.Write($"SetInformationJobObject failed (error {Marshal.GetLastPInvokeError()}).");
            return;
        }

        if (!AssignProcessToJobObject(job, GetCurrentProcess()))
        {
            diagnostics.Write($"AssignProcessToJobObject failed (error {Marshal.GetLastPInvokeError()}).");
            return;
        }

        s_job = job;
    }

    /// <summary>
    /// Processes in the job other than the Runner. Launchers (e.g. PalServer.exe) start the real server and exit;
    /// the game counts as running while any of them lives.
    /// </summary>
    public static int OtherProcessCount()
    {
        if (s_job == 0)
        {
            return 0;
        }

        var accounting = default(JobObjectBasicAccounting);
        return QueryInformationJobObject(s_job, JobObjectBasicAccountingInformation, &accounting, (uint)sizeof(JobObjectBasicAccounting), null)
            ? Math.Max(0, (int)accounting.ActiveProcesses - 1)
            : 0;
    }

    /// <summary>Kills every process in the job except the Runner, including children whose parent already exited.</summary>
    public static void KillOtherProcesses()
    {
        if (s_job == 0)
        {
            return;
        }

        const int capacity = 512;
        var buffer = stackalloc nuint[capacity + 1]; // header (two DWORDs) + ids
        if (!QueryInformationJobObject(s_job, JobObjectBasicProcessIdList, buffer, (uint)((capacity + 1) * sizeof(nuint)), null))
        {
            return;
        }

        var count = ((uint*)buffer)[1];
        for (var i = 0; i < count; i++)
        {
            var pid = (int)buffer[1 + i];
            if (pid == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }
    }

    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobObjectBasicProcessIdList = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccounting
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint job, int informationClass, void* information, uint length, uint* returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimit
    {
        public JobObjectBasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateJobObjectW(nint jobAttributes, char* name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int informationClass, void* information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
