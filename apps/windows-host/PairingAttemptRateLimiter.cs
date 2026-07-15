namespace VolturaAir.Host;

internal sealed class PairingAttemptRateLimiter(
    int maxFailures = PairingAttemptRateLimiter.DefaultMaxFailures,
    int maxEntries = PairingAttemptRateLimiter.DefaultMaxEntries,
    TimeSpan? window = null,
    TimeSpan? lockout = null,
    Func<DateTimeOffset>? now = null)
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultLockout = TimeSpan.FromSeconds(30);
    public const int DefaultMaxFailures = 8;
    public const int DefaultMaxEntries = 1024;

    private readonly int _maxFailures = maxFailures;
    private readonly int _maxEntries = Math.Max(1, maxEntries);
    private readonly TimeSpan _window = window ?? DefaultWindow;
    private readonly TimeSpan _lockout = lockout ?? DefaultLockout;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    public bool IsBlocked(string key)
    {
        var now = _now();
        lock (_gate)
        {
            PruneExpired(now);
            if (!_attempts.TryGetValue(key, out var state))
            {
                return false;
            }

            if (state.LockedUntil is not null && state.LockedUntil > now)
            {
                return true;
            }

            return false;
        }
    }

    public void RecordFailure(string key)
    {
        var now = _now();
        lock (_gate)
        {
            PruneExpired(now);
            if (!_attempts.TryGetValue(key, out var state) || state.FirstFailureAt + _window <= now)
            {
                TrimForNewEntry(key);
                state = new AttemptState(now, 0, null);
            }

            state = state with { FailureCount = state.FailureCount + 1 };
            if (state.FailureCount >= _maxFailures)
            {
                state = state with { LockedUntil = now + _lockout };
            }

            _attempts[key] = state;
        }
    }

    public void Reset(string key)
    {
        lock (_gate)
        {
            _attempts.Remove(key);
        }
    }

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _attempts.Count;
            }
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var key in _attempts
            .Where(pair => pair.Value.LockedUntil is { } lockedUntil
                ? lockedUntil <= now
                : pair.Value.FirstFailureAt + _window <= now)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _attempts.Remove(key);
        }
    }

    private void TrimForNewEntry(string key)
    {
        if (_attempts.ContainsKey(key) || _attempts.Count < _maxEntries)
        {
            return;
        }

        var oldestKey = _attempts.MinBy(pair => pair.Value.FirstFailureAt).Key;
        _attempts.Remove(oldestKey);
    }

    private sealed record AttemptState(DateTimeOffset FirstFailureAt, int FailureCount, DateTimeOffset? LockedUntil);
}
