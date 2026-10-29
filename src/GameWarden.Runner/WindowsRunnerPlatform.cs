using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GameWarden.Runner;

/// <summary>
/// The Runner on Windows, its production platform (ADR-0016): the console rules from spike S1 (02 §4.3), a
/// kill-on-close Job Object (02 §4.2), and a pipe only SYSTEM, Administrators and the Runner's own user may open.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRunnerPlatform : IRunnerPlatform
{
    public string InterruptName => "CTRL+C";

    public void Prepare(DiagnosticLog diagnostics)
    {
        // Order matters: own a console and clear the inherited CTRL+C-ignore flag before the game starts (02 §4.3).
        ConsoleControl.Prepare(diagnostics);
        JobObject.KillChildrenWhenRunnerDies(diagnostics);
    }

    /// <summary>A console event: it reaches every process on the Runner's console, including the game's children.</summary>
    public bool TrySendInterrupt(Process game, [NotNullWhen(false)] out string? error)
    {
        if (!ConsoleControl.TrySendCtrlC(out var code))
        {
            error = $"GenerateConsoleCtrlEvent failed (error {code}).";
            return false;
        }

        error = null;
        return true;
    }

    public int OtherProcessCount() => JobObject.OtherProcessCount();

    public void KillOtherProcesses() => JobObject.KillOtherProcesses();

    public NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    public void Dispose()
    {
        // Nothing to release: the Job Object handle stays open on purpose until the Runner exits (JobObject).
    }
}
