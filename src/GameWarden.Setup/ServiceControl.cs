using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace GameWarden.Setup;

/// <summary>The GameWardenCore service through the Service Control Manager (ADR-0014, 10 §5).</summary>
internal static unsafe partial class ServiceControl
{
    /// <summary>Windows waits this long for Core to stop the games when it shuts down (ADR-0014).</summary>
    public const int PreshutdownTimeoutMs = 120_000;

    private const uint ScManagerAllAccess = 0xF003F;
    private const uint ServiceAllAccess = 0xF01FF;
    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint ServiceAutoStart = 2;
    private const uint ServiceErrorNormal = 1;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ConfigDescription = 1;
    private const uint ConfigFailureActions = 2;
    private const uint ConfigDelayedAutoStart = 3;
    private const uint ConfigFailureActionsFlag = 4;
    private const uint ConfigPreshutdownInfo = 7;
    private const int ScActionRestart = 1;
    private const int ErrorServiceDoesNotExist = 1060;
    private const string Description = "Runs and supervises the game servers of GameWarden (github.com/vinh466/GameWarden).";

    public static bool Exists()
    {
        var services = ServiceController.GetServices();
        try
        {
            return services.Any(service => service.ServiceName == InstallLayout.ServiceName);
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

    public static ServiceControllerStatus? Status()
    {
        if (!Exists())
        {
            return null;
        }

        using var service = new ServiceController(InstallLayout.ServiceName);
        return service.Status;
    }

    /// <summary>Creates the service, or points the existing one at <paramref name="commandLine"/>; then applies its settings.</summary>
    public static void Install(string commandLine)
    {
        var scm = OpenSCManagerW(null, null, ScManagerAllAccess);
        Check(scm != 0, "OpenSCManager");
        try
        {
            var service = OpenServiceW(scm, InstallLayout.ServiceName, ServiceAllAccess);
            if (service == 0)
            {
                service = CreateServiceW(
                    scm,
                    InstallLayout.ServiceName,
                    "GameWarden Core",
                    ServiceAllAccess,
                    ServiceWin32OwnProcess,
                    ServiceAutoStart,
                    ServiceErrorNormal,
                    commandLine,
                    null,
                    0,
                    null,
                    null, // LocalSystem (ADR-0002)
                    null);
                Check(service != 0, "CreateService");
            }
            else
            {
                Check(ChangeServiceConfigW(service, ServiceNoChange, ServiceAutoStart, ServiceNoChange, commandLine, null, 0, null, null, null, null), "ChangeServiceConfig");
            }

            try
            {
                Configure(service);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    /// <summary>Points the service at another version's Core (update, rollback, <c>version use</c>).</summary>
    public static void SetCommandLine(string commandLine)
    {
        WithService(service => Check(
            ChangeServiceConfigW(service, ServiceNoChange, ServiceNoChange, ServiceNoChange, commandLine, null, 0, null, null, null, null),
            "ChangeServiceConfig"));
    }

    public static void Delete() => WithService(service => Check(DeleteService(service), "DeleteService"));

    /// <summary>A data root other than the default reaches Core as an environment variable of the service.</summary>
    public static void SetDataRoot(string dataRoot)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{InstallLayout.ServiceName}", writable: true)
            ?? throw new InvalidOperationException("The service's registry key is missing.");
        if (string.Equals(Path.GetFullPath(dataRoot).TrimEnd('\\'), InstallLayout.DefaultDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            key.DeleteValue("Environment", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue("Environment", new[] { $"GameWarden__DataRoot={dataRoot}" }, RegistryValueKind.MultiString);
        }
    }

    public static void Start(TimeSpan timeout)
    {
        using var service = new ServiceController(InstallLayout.ServiceName);
        if (service.Status != ServiceControllerStatus.Running)
        {
            if (service.Status != ServiceControllerStatus.StartPending)
            {
                service.Start();
            }

            service.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }
    }

    /// <summary>Stops the service; Core detaches and the games keep running (ADR-0003). False if it did not stop in time.</summary>
    public static bool Stop(TimeSpan timeout)
    {
        if (!Exists())
        {
            return true;
        }

        using var service = new ServiceController(InstallLayout.ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            return true;
        }

        if (service.Status != ServiceControllerStatus.StopPending)
        {
            service.Stop();
        }

        try
        {
            service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            return true;
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Delayed automatic start, restart on failure (10 s, 30 s, 60 s; reset after a day) and the pre-shutdown timeout.</summary>
    private static void Configure(nint service)
    {
        fixed (char* text = Description)
        {
            var description = text;
            Check(ChangeServiceConfig2W(service, ConfigDescription, &description), "ChangeServiceConfig2(description)");
        }

        var delayed = 1;
        Check(ChangeServiceConfig2W(service, ConfigDelayedAutoStart, &delayed), "ChangeServiceConfig2(delayed start)");

        var actions = stackalloc ScAction[3];
        actions[0] = new ScAction { Type = ScActionRestart, Delay = 10_000 };
        actions[1] = new ScAction { Type = ScActionRestart, Delay = 30_000 };
        actions[2] = new ScAction { Type = ScActionRestart, Delay = 60_000 };
        var failure = new FailureActions { ResetPeriod = 86_400, ActionCount = 3, Actions = actions };
        Check(ChangeServiceConfig2W(service, ConfigFailureActions, &failure), "ChangeServiceConfig2(failure actions)");

        var onNonCrashFailures = 1; // also when Core exits with an error code
        Check(ChangeServiceConfig2W(service, ConfigFailureActionsFlag, &onNonCrashFailures), "ChangeServiceConfig2(failure flag)");

        var preshutdown = (uint)PreshutdownTimeoutMs;
        Check(ChangeServiceConfig2W(service, ConfigPreshutdownInfo, &preshutdown), "ChangeServiceConfig2(pre-shutdown)");
    }

    private static void WithService(Action<nint> action)
    {
        var scm = OpenSCManagerW(null, null, ScManagerAllAccess);
        Check(scm != 0, "OpenSCManager");
        try
        {
            var service = OpenServiceW(scm, InstallLayout.ServiceName, ServiceAllAccess);
            if (service == 0 && Marshal.GetLastPInvokeError() == ErrorServiceDoesNotExist)
            {
                throw new InvalidOperationException($"The {InstallLayout.ServiceName} service is not installed.");
            }

            Check(service != 0, "OpenService");
            try
            {
                action(service);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{what} failed");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScAction
    {
        public int Type;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FailureActions
    {
        public uint ResetPeriod;
        public char* RebootMessage;
        public char* Command;
        public uint ActionCount;
        public ScAction* Actions;
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenServiceW(nint scm, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateServiceW(
        nint scm,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        nint tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfigW(
        nint service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        nint tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2W(nint service, uint infoLevel, void* info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint service);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}
