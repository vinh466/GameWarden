using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameWarden.Runner;

/// <summary>
/// The Runner on Linux, the development platform of ADR-0016 (not a deployment target): <c>SIGINT</c> to the game
/// instead of CTRL+C, a Unix domain socket for the current user only instead of an ACL'd pipe, and no Job Object.
/// </summary>
/// <remarks>
/// Without a Job Object, processes a launcher leaves behind are not tracked (<see cref="OtherProcessCount"/> is 0),
/// and a Runner that is killed leaves its game running. A kill from Core still takes the game's whole process tree.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxRunnerPlatform : IRunnerPlatform
{
    private const int SigInt = 2;
    private const nint SigDfl = 0;
    private const nint SigErr = -1;

    private NamedPipeServerStream? _listening;

    public string InterruptName => "SIGINT";

    public void Prepare(DiagnosticLog diagnostics)
    {
        diagnostics.Write("Running on the development platform (Linux, ADR-0016): SIGINT stops the game; no Job Object.");

        // Counterpart of the Runner's own console (02 §4.3): a CTRL+C in the terminal that runs Core goes to
        // Core's whole process group, and must not reach Runners and games, which outlive Core.
        if (setsid() == -1)
        {
            diagnostics.Write($"setsid failed (errno {Marshal.GetLastPInvokeError()}); a CTRL+C in Core's terminal also reaches the game.");
        }

        // Counterpart of the inherited CTRL+C-ignore flag (spike S1): a shell starts background jobs with SIGINT
        // ignored, the game inherits that, and .NET then never raises CancelKeyPress. Only an ignored SIGINT is
        // reset; a handler the runtime installed is left alone (the runtime resets it for the game itself).
        if (IsIgnored(SigInt))
        {
            diagnostics.Write(signal(SigInt, SigDfl) == SigErr
                ? $"Could not clear the inherited SIGINT-ignore flag (errno {Marshal.GetLastPInvokeError()}); SIGINT stops will not work."
                : "Cleared the inherited SIGINT-ignore flag.");
        }
    }

    /// <summary>Signals the game's main process only; the Runner has its own process group and is not involved.</summary>
    public bool TrySendInterrupt(Process game, [NotNullWhen(false)] out string? error)
    {
        if (kill(game.Id, SigInt) != 0)
        {
            error = $"kill(SIGINT) failed (errno {Marshal.GetLastPInvokeError()}).";
            return false;
        }

        error = null;
        return true;
    }

    public int OtherProcessCount() => 0;

    public void KillOtherProcesses()
    {
        // Nothing tracked: Process.Kill(entireProcessTree: true) already took every descendant still attached.
    }

    /// <summary>A Unix domain socket (<c>/tmp/CoreFxPipe_&lt;name&gt;</c>); <c>CurrentUserOnly</c> refuses clients of any other user.</summary>
    public NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        // .NET closes the pipe's listening socket when its last server stream is disposed, and resets a client that
        // connected just before (Core reconnecting while the previous session closes). An idle instance held for the
        // Runner's lifetime keeps the socket listening, so that client waits for the next session, as on Windows.
        _listening ??= NewPipeServer(pipeName);
        return NewPipeServer(pipeName);
    }

    /// <summary>Closes the listening socket and deletes its file.</summary>
    public void Dispose() => _listening?.Dispose();

    // Two instances: the idle one that keeps the socket listening, and the one that serves the current session.
    private static NamedPipeServerStream NewPipeServer(string pipeName) =>
        new(pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>Reads the mask of ignored signals (<c>SigIgn</c>, hexadecimal, bit n-1 for signal n) from /proc.</summary>
    private static bool IsIgnored(int signalNumber)
    {
        var line = File.ReadLines("/proc/self/status").FirstOrDefault(l => l.StartsWith("SigIgn:", StringComparison.Ordinal));
        return line is not null
            && ulong.TryParse(line["SigIgn:".Length..].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask)
            && (mask & (1UL << (signalNumber - 1))) != 0;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int setsid();

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint signal(int signal, nint handler);
}
