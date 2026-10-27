namespace GameWarden.Core.Instances;

/// <summary>Crash and restart policy, defaults from docs/03-domain-model.md §4.</summary>
public sealed record RestartPolicy
{
    public static RestartPolicy Default { get; } = new();

    public IReadOnlyList<TimeSpan> Backoff { get; init; } =
        [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(300)];

    /// <summary>Restarts allowed within <see cref="Window"/> before the instance becomes Faulted.</summary>
    public int MaxRestarts { get; init; } = 5;

    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Running this long without a crash resets the counter.</summary>
    public TimeSpan ResetAfterRunning { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>Decides what happens after a crash. Pure: the caller passes the clock.</summary>
public sealed class RestartTracker(RestartPolicy policy)
{
    private readonly List<DateTimeOffset> _restarts = [];

    public int RecentRestarts => _restarts.Count;

    public void Reset() => _restarts.Clear();

    /// <summary>
    /// Records a crash. Returns the back-off before the next start, or <c>null</c> when the restart limit
    /// is reached and the instance must become Faulted.
    /// </summary>
    public TimeSpan? OnCrash(DateTimeOffset now, DateTimeOffset? runningSince)
    {
        if (runningSince is { } since && now - since >= policy.ResetAfterRunning)
        {
            _restarts.Clear();
        }

        _restarts.RemoveAll(at => now - at > policy.Window);
        if (_restarts.Count >= policy.MaxRestarts)
        {
            return null;
        }

        var delay = policy.Backoff[Math.Min(_restarts.Count, policy.Backoff.Count - 1)];
        _restarts.Add(now);
        return delay;
    }
}
