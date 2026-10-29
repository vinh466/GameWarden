using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameWarden.Core.Hosting;

/// <summary>
/// Runs Core as the Windows service <c>GameWardenCore</c> (ADR-0014). Unlike .NET's own service lifetime it accepts
/// PRESHUTDOWN, so Windows waits (up to the pre-shutdown timeout set by Setup) while the games save their worlds.
/// STOP detaches from the games; PRESHUTDOWN also sets <see cref="ShutdownSignal.SystemShuttingDown"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsServiceLifetime(
    IHostApplicationLifetime lifetime,
    ShutdownSignal shutdown,
    ILogger<WindowsServiceLifetime> logger) : IHostLifetime, IDisposable
{
    public const string ServiceName = "GameWardenCore";

    internal const uint ControlStop = 0x1;
    internal const uint ControlInterrogate = 0x4;
    internal const uint ControlPreshutdown = 0xF;

    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint StateStopped = 1;
    private const uint StateStartPending = 2;
    private const uint StateStopPending = 3;
    private const uint StateRunning = 4;
    private const uint AcceptStop = 0x1;
    private const uint AcceptPreshutdown = 0x100;
    private const uint NoError = 0;
    private const uint ErrorCallNotImplemented = 120;
    private const uint PendingWaitHintMs = 30_000;

    // The service callbacks are static; there is one service per process.
    private static WindowsServiceLifetime? s_current;

    private readonly TaskCompletionSource _serviceMainCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private nint _statusHandle;
    private uint _state;
    private uint _checkpoint;
    private Timer? _pendingPump;

    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        s_current = this;
        lifetime.ApplicationStarted.Register(() => SetState(StateRunning));
        lifetime.ApplicationStopping.Register(() => SetState(StateStopPending));
        lifetime.ApplicationStopped.Register(() => SetState(StateStopped));

        // StartServiceCtrlDispatcher blocks until the service stops, so it gets its own thread.
        new Thread(RunDispatcher) { IsBackground = true, Name = "Service control dispatcher" }.Start();
        return _serviceMainCalled.Task.WaitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        lock (_gate)
        {
            _pendingPump?.Dispose();
            _pendingPump = null;
        }
    }

    /// <summary>What a service control does; separate from the native callback so it can be unit-tested.</summary>
    internal uint OnControl(uint control)
    {
        switch (control)
        {
            case ControlPreshutdown:
                logger.LogInformation("Windows is shutting down (PRESHUTDOWN): stopping the games gracefully.");
                shutdown.OnSystemShutdown();
                RequestStop();
                return NoError;
            case ControlStop:
                logger.LogInformation("Service stop requested: detaching from the games, which keep running.");
                RequestStop();
                return NoError;
            case ControlInterrogate:
                return NoError;
            default:
                return ErrorCallNotImplemented;
        }
    }

    private void RequestStop()
    {
        SetState(StateStopPending);
        // The control handler must return quickly; the host stops on the thread pool.
        ThreadPool.QueueUserWorkItem(_ => lifetime.StopApplication());
    }

    private void RunDispatcher()
    {
        fixed (char* name = "") // ignored for SERVICE_WIN32_OWN_PROCESS, but must not be null
        {
            var table = stackalloc ServiceTableEntry[2];
            table[0] = new ServiceTableEntry { ServiceName = name, ServiceProc = &ServiceMain };
            table[1] = default;
            if (!StartServiceCtrlDispatcherW(table))
            {
                _serviceMainCalled.TrySetException(new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "StartServiceCtrlDispatcher failed. --service is for the Service Control Manager; run Core without it in a console."));
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void ServiceMain(uint argc, char** argv)
    {
        var self = s_current;
        if (self is null)
        {
            return;
        }

        try
        {
            nint handle;
            fixed (char* name = ServiceName)
            {
                handle = RegisterServiceCtrlHandlerExW(name, &Handler, 0);
            }

            if (handle == 0)
            {
                self._serviceMainCalled.TrySetException(new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterServiceCtrlHandlerEx failed."));
                return;
            }

            lock (self._gate)
            {
                self._statusHandle = handle;
            }

            self.SetState(StateStartPending);
            self._serviceMainCalled.TrySetResult();
        }
        catch (Exception ex)
        {
            self._serviceMainCalled.TrySetException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static uint Handler(uint control, uint eventType, void* eventData, void* context)
    {
        try
        {
            return s_current?.OnControl(control) ?? NoError;
        }
        catch
        {
            return NoError; // never let an exception cross into the SCM
        }
    }

    private void SetState(uint state)
    {
        lock (_gate)
        {
            if (_statusHandle == 0 || _state == StateStopped || (state == _state && !IsPending(state)))
            {
                return;
            }

            _state = state;
            Report();
            if (IsPending(state))
            {
                // Keep telling the SCM we are making progress while hosted services start or stop.
                _pendingPump ??= new Timer(_ => Pump(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }
            else
            {
                _pendingPump?.Dispose();
                _pendingPump = null;
            }
        }
    }

    private void Pump()
    {
        lock (_gate)
        {
            if (IsPending(_state))
            {
                Report();
            }
        }
    }

    private void Report()
    {
        var pending = IsPending(_state);
        _checkpoint = pending ? _checkpoint + 1 : 0;
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = _state,
            ControlsAccepted = _state == StateRunning ? AcceptStop | AcceptPreshutdown : 0,
            CheckPoint = _checkpoint,
            WaitHint = pending ? PendingWaitHintMs : 0,
        };
        if (!SetServiceStatus(_statusHandle, &status))
        {
            logger.LogWarning("SetServiceStatus({State}) failed with error {Error}.", _state, Marshal.GetLastPInvokeError());
        }
    }

    private static bool IsPending(uint state) => state is StateStartPending or StateStopPending;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceTableEntry
    {
        public char* ServiceName;
        public delegate* unmanaged<uint, char**, void> ServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartServiceCtrlDispatcherW(ServiceTableEntry* serviceTable);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial nint RegisterServiceCtrlHandlerExW(char* serviceName, delegate* unmanaged<uint, uint, void*, void*, uint> handler, nint context);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetServiceStatus(nint serviceStatus, ServiceStatus* status);
}
