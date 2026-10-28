namespace GameWarden.Core.Access;

/// <summary>
/// 06 §4: five failed sign-ins for one user name within 15 minutes lock that name for 15 minutes. The per-address
/// limit is ASP.NET Core's rate limiter (<see cref="AccessServices.LoginRateLimit"/>).
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, List<DateTimeOffset>> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long <paramref name="userName"/> stays locked, or null when it may try.</summary>
    public TimeSpan? LockedFor(string userName)
    {
        lock (_failures)
        {
            var now = time.GetUtcNow();
            if (!_failures.TryGetValue(userName, out var failures))
            {
                return null;
            }

            failures.RemoveAll(at => now - at >= Window);
            if (failures.Count < MaxFailures)
            {
                return null;
            }

            return failures[^1] + Window - now;
        }
    }

    public void Failed(string userName)
    {
        lock (_failures)
        {
            var now = time.GetUtcNow();
            if (_failures.Count > 1000)
            {
                // Guessing many names must not grow this without bound: forget names with no recent failure.
                foreach (var stale in _failures.Where(pair => pair.Value.TrueForAll(at => now - at >= Window)).Select(pair => pair.Key).ToList())
                {
                    _failures.Remove(stale);
                }
            }

            if (!_failures.TryGetValue(userName, out var failures))
            {
                _failures[userName] = failures = [];
            }

            failures.Add(now);
        }
    }

    public void Succeeded(string userName)
    {
        lock (_failures)
        {
            _failures.Remove(userName);
        }
    }
}
