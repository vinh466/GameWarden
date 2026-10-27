using System.ComponentModel;
using System.Diagnostics;

namespace GameWarden.Core.Processes;

public sealed record RunnerProcess(int Pid, DateTimeOffset StartedAt);

/// <summary>
/// Starts Runners. Production starts them as <c>gamewarden-run</c> through LogonUser + CreateProcessAsUser
/// (ADR-0008, tested by hand on the VM); development and CI use <see cref="CurrentUserProcessHost"/> (02 §11).
/// </summary>
public interface IProcessHost
{
    RunnerProcess StartRunner(string runnerPath, string launchSpecPath, string workingDirectory);

    /// <summary>True when <paramref name="pid"/> is still the process started at <paramref name="startedAt"/> (guards PID reuse).</summary>
    bool IsAlive(int pid, DateTimeOffset? startedAt);

    void Kill(int pid);
}

public sealed class CurrentUserProcessHost : IProcessHost
{
    public RunnerProcess StartRunner(string runnerPath, string launchSpecPath, string workingDirectory)
    {
        var info = new ProcessStartInfo(runnerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true, // CREATE_NO_WINDOW: the Runner gets its own hidden console (02 §4.3)
            WorkingDirectory = workingDirectory,
        };
        info.ArgumentList.Add("--spec");
        info.ArgumentList.Add(launchSpecPath);
        using var runner = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {runnerPath}.");
        return new RunnerProcess(runner.Id, new DateTimeOffset(runner.StartTime));
    }

    public bool IsAlive(int pid, DateTimeOffset? startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited
                && (startedAt is null || (new DateTimeOffset(process.StartTime) - startedAt.Value).Duration() < TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
