namespace VolturaAir.Host;

internal sealed class PairingAttemptRateLimiter
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultLockout = TimeSpan.FromSeconds(30);
    public const int DefaultMaxFailures = 8;

    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockout;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    public PairingAttemptRateLimiter(
        int maxFailures = DefaultMaxFailures,
        TimeSpan? window = null,
        TimeSpan? lockout = null,
        Func<DateTimeOffset>? now = null)
    {
        _maxFailures = maxFailures;
        _window = window ?? DefaultWindow;
        _lockout = lockout ?? DefaultLockout;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsBlocked(string key)
    {
        var now = _now();
        lock (_gate)
        {
            if (!_attempts.TryGetValue(key, out var state))
            {
                return false;
            }

            if (state.LockedUntil is not null && state.LockedUntil > now)
            {
                return true;
            }

            if (state.LockedUntil is not null || state.FirstFailureAt + _window <= now)
            {
                _attempts.Remove(key);
            }

            return false;
        }
    }

    public void RecordFailure(string key)
    {
        var now = _now();
        lock (_gate)
        {
            if (!_attempts.TryGetValue(key, out var state) || state.FirstFailureAt + _window <= now)
            {
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

    private sealed record AttemptState(DateTimeOffset FirstFailureAt, int FailureCount, DateTimeOffset? LockedUntil);
}
