using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

namespace GameWarden.Runner;

/// <summary>
/// What the Runner does differently per operating system (ADR-0016, the Runner rows of its ports table).
/// Program.cs picks the implementation once at startup and disposes it when the Runner exits; the rest of the Runner
/// never calls an OS API directly.
/// </summary>
internal interface IRunnerPlatform : IDisposable
{
    /// <summary>The graceful stop this platform sends, for the console feed: <c>CTRL+C</c> or <c>SIGINT</c>.</summary>
    string InterruptName { get; }

    /// <summary>Prepares the Runner's process before the game starts: console or signal rules, and child containment.</summary>
    void Prepare(DiagnosticLog diagnostics);

    /// <summary>Asks the game to stop gracefully (adapter stop method <c>ctrl-c</c>, 02 §4.3).</summary>
    /// <param name="error">Why it could not be sent, naming the platform's mechanism; becomes the pipe's <c>Rejected</c> reason.</param>
    bool TrySendInterrupt(Process game, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Processes the game started that still run after the main process exited (launchers such as PalServer.exe
    /// start the real server and exit). The game counts as running while any of them lives.
    /// </summary>
    int OtherProcessCount();

    /// <summary>Kills the processes of <see cref="OtherProcessCount"/>, including children whose parent already exited.</summary>
    void KillOtherProcesses();

    /// <summary>Creates one server instance of the Runner pipe, restricted to who may talk to this Runner.</summary>
    NamedPipeServerStream CreatePipeServer(string pipeName);
}
