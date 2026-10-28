using System.ComponentModel;
using System.Runtime.InteropServices;
using GameWarden.Core.Processes;

namespace GameWarden.Core.Isolation;

/// <summary>
/// Starts Runners as <c>gamewarden-run</c> (ADR-0008): batch logon token → user environment block →
/// <c>CreateProcessAsUser</c> with <c>lpDesktop = ""</c> and <c>CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT</c>.
/// </summary>
public sealed unsafe class RunAsUserProcessHost(RunAsAccount account) : IProcessHost
{
    private readonly CurrentUserProcessHost _local = new(); // IsAlive and Kill work across users for LocalSystem

    public RunnerProcess StartRunner(string runnerPath, string launchSpecPath, string workingDirectory)
    {
        var token = account.Token;
        var tokenAdded = false;
        token.DangerousAddRef(ref tokenAdded);
        try
        {
            if (!Win32.CreateEnvironmentBlock(out var environment, token.DangerousGetHandle(), inherit: false))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateEnvironmentBlock for {account.UserName} failed.");
            }

            try
            {
                // CreateProcessAsUser may write to the command line buffer, so it gets its own copy.
                var commandLine = (CommandLine.Build([runnerPath, "--spec", launchSpecPath]) + '\0').ToCharArray();
                fixed (char* application = runnerPath, line = commandLine, directory = workingDirectory, desktop = "")
                {
                    var startup = new Win32.StartupInfo { Size = sizeof(Win32.StartupInfo), Desktop = desktop };
                    Win32.ProcessInformation process;
                    if (!Win32.CreateProcessAsUser(
                            token.DangerousGetHandle(),
                            application,
                            line,
                            0,
                            0,
                            inheritHandles: false,
                            Win32.CreateUnicodeEnvironment | Win32.CreateNoWindow,
                            environment,
                            directory,
                            &startup,
                            &process))
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateProcessAsUser({runnerPath}) as {account.UserName} failed.");
                    }

                    try
                    {
                        var startedAt = Win32.GetProcessTimes(process.Process, out var creation, out _, out _, out _)
                            ? DateTimeOffset.FromFileTime(creation)
                            : DateTimeOffset.Now;
                        return new RunnerProcess(process.ProcessId, startedAt);
                    }
                    finally
                    {
                        Win32.CloseHandle(process.Thread);
                        Win32.CloseHandle(process.Process);
                    }
                }
            }
            finally
            {
                Win32.DestroyEnvironmentBlock(environment);
            }
        }
        finally
        {
            if (tokenAdded)
            {
                token.DangerousRelease();
            }
        }
    }

    public bool IsAlive(int pid, DateTimeOffset? startedAt) => _local.IsAlive(pid, startedAt);

    public void Kill(int pid) => _local.Kill(pid);
}
