namespace GameWarden.Core.Hosting;

/// <summary>
/// Why Core is stopping. A service stop or an update detaches and leaves the games running (ADR-0003, 10 §8 step 4);
/// Windows shutting down (PRESHUTDOWN) stops every game gracefully and keeps its desired state (ADR-0014).
/// </summary>
public sealed class ShutdownSignal
{
    private volatile bool _systemShuttingDown;

    public bool SystemShuttingDown => _systemShuttingDown;

    public void OnSystemShutdown() => _systemShuttingDown = true;
}
