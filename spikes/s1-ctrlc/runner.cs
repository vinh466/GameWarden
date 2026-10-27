// Prototype Runner: spawns the game on its own (windowless) console and stops it.
// Modes:
//   handler     - Runner registers a handler returning TRUE, then CTRL_C to group 0   (proposed design)
//   stdin       - Runner writes "exit" to the game's stdin
//   ignore-null - Runner calls SetConsoleCtrlHandler(NULL, TRUE) BEFORE spawning     (pitfall)
//   newgroup    - game spawned with CREATE_NEW_PROCESS_GROUP                          (pitfall)
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class Runner
{
    delegate bool HandlerRoutine(uint ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetConsoleProcessList(uint[] list, uint count);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr h, uint code);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    const uint CTRL_C_EVENT = 0;
    const uint CREATE_NEW_PROCESS_GROUP = 0x200;

    static HandlerRoutine keepAlive;

    static int Main(string[] args)
    {
        string mode = args[0];
        string game = args[1];
        Console.WriteLine("runner: mode=" + mode);

        // Every mode except plain "handler" first clears any CTRL+C-ignore flag inherited from the parent chain.
        if (mode != "handler")
            Console.WriteLine("runner: SetConsoleCtrlHandler(NULL, FALSE) reset = " + SetConsoleCtrlHandler(null, false));

        if (mode == "ignore-null")
        {
            SetConsoleCtrlHandler(null, true);
        }
        else
        {
            keepAlive = delegate(uint t) { Console.WriteLine("runner: got ctrl event " + t + " -> ignored (handler returns TRUE)"); return true; };
            SetConsoleCtrlHandler(keepAlive, true);
        }

        Process p = null;
        IntPtr hProcess = IntPtr.Zero;
        if (mode == "newgroup")
        {
            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            if (!CreateProcessW(null, "\"" + game + "\"", IntPtr.Zero, IntPtr.Zero, false, CREATE_NEW_PROCESS_GROUP,
                    IntPtr.Zero, null, ref si, out pi))
            {
                Console.WriteLine("runner: CreateProcess failed, err=" + Marshal.GetLastWin32Error());
                return 1;
            }
            hProcess = pi.hProcess;
        }
        else
        {
            ProcessStartInfo psi = new ProcessStartInfo(game);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardInput = true;
            psi.CreateNoWindow = false; // share the Runner's console
            p = Process.Start(psi);
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Console.WriteLine("  [game stdout via pipe] " + e.Data); };
            p.BeginOutputReadLine();
        }
        Thread.Sleep(1500);

        uint[] procs = new uint[16];
        Console.WriteLine("runner: processes attached to this console = " + GetConsoleProcessList(procs, 16));

        if (mode == "stdin")
        {
            p.StandardInput.WriteLine("exit");
            p.StandardInput.Flush();
            Console.WriteLine("runner: wrote 'exit' to game stdin");
        }
        else
        {
            bool ok = GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
            Console.WriteLine("runner: GenerateConsoleCtrlEvent(CTRL_C, 0) = " + ok + (ok ? "" : " err=" + Marshal.GetLastWin32Error()));
        }

        bool exited;
        uint code = 0;
        if (p != null)
        {
            exited = p.WaitForExit(5000);
            if (exited) { p.WaitForExit(); code = (uint)p.ExitCode; } else p.Kill();
        }
        else
        {
            exited = WaitForSingleObject(hProcess, 5000) == 0;
            if (exited) GetExitCodeProcess(hProcess, out code); else TerminateProcess(hProcess, 1);
        }

        if (exited)
            Console.WriteLine("runner: RESULT game exited with code " + code + (code == 42 ? " (graceful via CTRL+C)" : code == 7 ? " (graceful via stdin)" : ""));
        else
            Console.WriteLine("runner: RESULT game did NOT react within 5 s -> killed");
        Console.WriteLine("runner: still alive after the signal");
        return 0;
    }
}
