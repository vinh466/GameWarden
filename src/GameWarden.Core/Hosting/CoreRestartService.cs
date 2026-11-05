using System.Diagnostics;
using GameWarden.Core.Activity;
using GameWarden.Core.Jobs;

namespace GameWarden.Core.Hosting;

public sealed class CoreRestartException(string message) : Exception(message);

/// <summary>
/// Restarts the Windows service from inside itself (docs/20 §6). The request returns before the service stops; a
/// detached PowerShell waits for the stop to finish and starts the service again. Game servers keep running
/// (ADR-0003). Development and Linux builds answer with a clear error instead of killing the process.
/// </summary>
public sealed class CoreRestartService(
    JobService jobs,
    ActivityLog activity,
    ILogger<CoreRestartService> logger)
{
    public const string ServiceName = "GameWardenCore";

    public void Request()
    {
        if (!OperatingSystem.IsWindows() || Environment.UserInteractive)
        {
            throw new CoreRestartException("Restarting Core from the panel is available on the Windows service only.");
        }

        if (jobs.AnyRunning)
        {
            throw new CoreRestartException("A job is running. Wait for it to finish before restarting Core.");
        }

        const string command =
            "Start-Sleep -Seconds 2; " +
            "Stop-Service -Name GameWardenCore -Force; " +
            "(Get-Service -Name GameWardenCore).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(120)); " +
            "Start-Service -Name GameWardenCore";
        var start = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);

        try
        {
            _ = Process.Start(start) ?? throw new CoreRestartException("Could not start the restart helper.");
        }
        catch (Exception ex) when (ex is not CoreRestartException)
        {
            logger.LogError(ex, "Could not start the Core restart helper.");
            throw new CoreRestartException($"Could not start the restart helper: {ex.Message}");
        }

        activity.Add("system", "info", "Core restart requested from the panel; it will reconnect.", null, null, "owner");
        logger.LogInformation("Core restart requested; the service stops in 2 seconds.");
    }
}
