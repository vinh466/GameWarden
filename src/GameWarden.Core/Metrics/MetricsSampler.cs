using System.ComponentModel;
using System.Diagnostics;
using GameWarden.Core.Checks;
using GameWarden.Core.Events;
using GameWarden.Core.Supervision;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Metrics;

/// <summary>CPU (percent of the whole machine) and RAM at one moment.</summary>
public sealed record MetricPoint(DateTimeOffset At, double CpuPercent, long RamMb);

public sealed record MetricSeries(MetricPoint? Latest, IReadOnlyList<MetricPoint> History);

/// <summary><c>GET /metrics</c>: the machine and every running server, with the last ten minutes.</summary>
public sealed record MetricsReport(MetricSeries Machine, IReadOnlyDictionary<string, MetricSeries> Instances);

/// <summary>
/// CPU and RAM of the VM and of each running game's process tree (docs/11 §13, D6), sampled every 5 s with ten
/// minutes of history for the panel's sparklines. Each sample also goes out on the event bus (realtime).
/// </summary>
public sealed class MetricsSampler(
    Supervisor supervisor,
    IMachine machine,
    CoreEvents events,
    IOptions<GameWardenOptions> options,
    TimeProvider time,
    ILogger<MetricsSampler> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    public const int HistoryLength = 120;

    private readonly object _gate = new();
    private readonly Queue<MetricPoint> _machine = new();
    private readonly Dictionary<string, Queue<MetricPoint>> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TimeSpan> _cpuByPid = [];
    private (long Idle, long Total)? _system;
    private DateTimeOffset _sampledAt;

    public MetricsReport Report()
    {
        lock (_gate)
        {
            return new MetricsReport(
                Series(_machine),
                _instances.ToDictionary(pair => pair.Key, pair => Series(pair.Value), StringComparer.Ordinal));
        }
    }

    /// <summary>Share of the whole machine: <paramref name="used"/> processor time over all cores' time in <paramref name="elapsed"/>.</summary>
    public static double CpuPercent(TimeSpan used, TimeSpan elapsed, int cores) =>
        elapsed <= TimeSpan.Zero || cores <= 0 ? 0 : Math.Round(Math.Clamp(used / (elapsed * cores) * 100, 0, 100), 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TrySample(); // the first sample only primes the CPU counters
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (TrySample() is { } sample)
                {
                    events.RaiseMetricsSampled(sample);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>A failed sample is logged and skipped; it must not stop Core.</summary>
    private MetricsSample? TrySample()
    {
        try
        {
            return Sample();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Sampling CPU and RAM failed.");
            return null;
        }
    }

    private MetricsSample Sample()
    {
        var now = time.GetUtcNow();
        var elapsed = now - _sampledAt;
        var first = _sampledAt == default;
        _sampledAt = now;
        var snapshot = machine.Snapshot(options.Value.DataRoot);
        var cores = snapshot.CpuCores;

        double machineCpu = 0;
        if (machine.CpuTimes() is { } current)
        {
            if (_system is { } previous && current.Total > previous.Total)
            {
                machineCpu = Math.Round((1 - (double)(current.Idle - previous.Idle) / (current.Total - previous.Total)) * 100, 1);
            }

            _system = current;
        }

        var machinePoint = new MetricPoint(now, Math.Clamp(machineCpu, 0, 100), snapshot.RamTotalMb - snapshot.RamFreeMb);
        var instances = new Dictionary<string, MetricPoint>(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        foreach (var server in supervisor.List().Where(server => server.GamePid is not null))
        {
            var (used, ramBytes) = Measure(server.GamePid!.Value, seen);
            instances[server.Id] = new MetricPoint(now, first ? 0 : CpuPercent(used, elapsed, cores), ramBytes / (1024 * 1024));
        }

        foreach (var gone in _cpuByPid.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            _cpuByPid.Remove(gone);
        }

        lock (_gate)
        {
            Append(_machine, machinePoint);
            foreach (var stopped in _instances.Keys.Where(id => !instances.ContainsKey(id)).ToList())
            {
                _instances.Remove(stopped);
            }

            foreach (var (id, point) in instances)
            {
                if (!_instances.TryGetValue(id, out var history))
                {
                    _instances[id] = history = new Queue<MetricPoint>();
                }

                Append(history, point);
            }
        }

        return new MetricsSample(machinePoint, instances);
    }

    /// <summary>Processor time used since the last sample and the working set, summed over the game's process tree.</summary>
    private (TimeSpan Used, long RamBytes) Measure(int gamePid, HashSet<int> seen)
    {
        var used = TimeSpan.Zero;
        long ram = 0;
        foreach (var pid in machine.WithDescendants(gamePid))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                var total = process.TotalProcessorTime;
                ram += process.WorkingSet64;
                if (_cpuByPid.TryGetValue(pid, out var previous) && total >= previous)
                {
                    used += total - previous;
                }

                _cpuByPid[pid] = total;
                seen.Add(pid);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Exited between the snapshot and now, or not ours to read.
            }
        }

        return (used, ram);
    }

    private static void Append(Queue<MetricPoint> history, MetricPoint point)
    {
        history.Enqueue(point);
        while (history.Count > HistoryLength)
        {
            history.Dequeue();
        }
    }

    private static MetricSeries Series(Queue<MetricPoint> history) => new(history.LastOrDefault(), [.. history]);
}

/// <summary>One sample for the realtime hub: the machine and each running server.</summary>
public sealed record MetricsSample(MetricPoint Machine, IReadOnlyDictionary<string, MetricPoint> Instances);
