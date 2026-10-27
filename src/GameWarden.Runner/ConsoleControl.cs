using System.Runtime.InteropServices;

namespace GameWarden.Runner;

/// <summary>
/// Console rules from spike S1 (docs/02-architecture.md §4.3): the Runner owns a console, clears the
/// CTRL+C-ignore flag it may have inherited, survives its own CTRL+C, and starts the game on the same console.
/// </summary>
internal static unsafe partial class ConsoleControl
{
    private const uint CtrlCEvent = 0;
    private const uint CtrlBreakEvent = 1;
    private const int SwHide = 0;

    public static void Prepare(DiagnosticLog diagnostics)
    {
        uint attached;
        if (GetConsoleProcessList(&attached, 1) == 0)
        {
            // Started without a console (e.g. DETACHED_PROCESS): create one and hide it.
            if (AllocConsole())
            {
                var window = GetConsoleWindow();
                if (window != 0)
                {
                    ShowWindow(window, SwHide);
                }

                diagnostics.Write("No console was attached; allocated a hidden one.");
            }
            else
            {
                diagnostics.Write($"AllocConsole failed (error {Marshal.GetLastPInvokeError()}); CTRL+C stops will not work.");
            }
        }

        // The "ignore CTRL+C" attribute is inherited from the parent chain; the game would inherit it too.
        if (!SetConsoleCtrlHandler(null, false))
        {
            diagnostics.Write($"Could not clear the CTRL+C-ignore flag (error {Marshal.GetLastPInvokeError()}).");
        }

        if (!SetConsoleCtrlHandler(&OnConsoleControl, true))
        {
            diagnostics.Write($"Could not register the CTRL+C handler (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>Raises CTRL+C for every process on the Runner's console: the game and the Runner, which ignores it.</summary>
    public static bool TrySendCtrlC(out int error)
    {
        var sent = GenerateConsoleCtrlEvent(CtrlCEvent, 0);
        error = sent ? 0 : Marshal.GetLastPInvokeError();
        return sent;
    }

    [UnmanagedCallersOnly]
    private static int OnConsoleControl(uint controlType) => controlType is CtrlCEvent or CtrlBreakEvent ? 1 : 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(delegate* unmanaged<uint, int> handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetConsoleProcessList(uint* processList, uint processCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
}
