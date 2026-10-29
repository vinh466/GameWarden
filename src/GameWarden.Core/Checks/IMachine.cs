namespace GameWarden.Core.Checks;

/// <summary>
/// The machine's RAM, disk, CPU times and processes (ADR-0016): read for <c>GET /system</c>, the resource checks before
/// a start or an install (OQ-07) and the metrics (docs/11 §13). <see cref="Platform.PlatformServices"/> picks the adapter.
/// </summary>
public interface IMachine
{
    /// <summary>Logical processors, RAM and the free disk of the volume that holds <paramref name="dataRoot"/>.</summary>
    MachineSnapshot Snapshot(string dataRoot);

    /// <summary>
    /// Machine-wide processor time since boot in the platform's own unit: <c>Idle</c> and <c>Total</c> (which includes
    /// idle), so the busy share between two readings is <c>1 - ΔIdle / ΔTotal</c>. Null when the counters cannot be read.
    /// </summary>
    (long Idle, long Total)? CpuTimes();

    /// <summary><paramref name="rootPid"/> and every process it started, directly or through others.</summary>
    IReadOnlyList<int> WithDescendants(int rootPid);
}
