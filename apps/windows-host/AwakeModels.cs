namespace VolturaAir.Host;

public enum AwakeMode
{
    Off,
    Indefinite,
    Timed,
    Expiration
}

public sealed record AwakeState(
    AwakeMode Mode,
    bool KeepScreenOn,
    int IntervalMinutes,
    DateTimeOffset? ExpiresAt)
{
    public bool IsActive => Mode != AwakeMode.Off;
}

public enum AwakeOperationFailure
{
    InvalidRequest,
    Rejected,
    Busy,
    TimedOut,
    Cancelled,
    ShuttingDown,
    Unavailable
}

public sealed record AwakeOperationResult(
    bool Succeeded,
    string? Error = null,
    AwakeOperationFailure? Failure = null)
{
    public static AwakeOperationResult Success { get; } = new(true);
}

public interface IAwakeService : IAsyncDisposable
{
    AwakeState State { get; }

    event EventHandler? StateChanged;

    Task<AwakeOperationResult> SetOffAsync(CancellationToken cancellationToken = default);

    Task<AwakeOperationResult> SetIndefiniteAsync(CancellationToken cancellationToken = default);

    Task<AwakeOperationResult> SetTimedAsync(TimeSpan duration, CancellationToken cancellationToken = default);

    Task<AwakeOperationResult> SetExpirationAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    Task<AwakeOperationResult> SetKeepScreenOnAsync(bool keepScreenOn, CancellationToken cancellationToken = default);
}

internal sealed class NoOpAwakeService(AwakeState? initialState = null) : IAwakeService
{
    private AwakeState _state = initialState ?? new AwakeState(AwakeMode.Off, false, 60, null);
    private bool _disposed;

    public AwakeState State => _state;

    public event EventHandler? StateChanged;

    public Task<AwakeOperationResult> SetOffAsync(CancellationToken cancellationToken = default) =>
        SetAsync(_state with { Mode = AwakeMode.Off, ExpiresAt = null }, cancellationToken);

    public Task<AwakeOperationResult> SetIndefiniteAsync(CancellationToken cancellationToken = default) =>
        SetAsync(_state with { Mode = AwakeMode.Indefinite, ExpiresAt = null }, cancellationToken);

    public Task<AwakeOperationResult> SetTimedAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var minutes = (int)Math.Ceiling(duration.TotalMinutes);
        return minutes is < 1 or > 525_600
            ? Task.FromResult(Failed(AwakeOperationFailure.InvalidRequest, "Choose an interval between 1 minute and 1 year."))
            : SetAsync(_state with
            {
                Mode = AwakeMode.Timed,
                IntervalMinutes = minutes,
                ExpiresAt = DateTimeOffset.Now.AddMinutes(minutes)
            }, cancellationToken);
    }

    public Task<AwakeOperationResult> SetExpirationAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        expiresAt <= DateTimeOffset.Now
            ? Task.FromResult(Failed(AwakeOperationFailure.InvalidRequest, "Choose a future date and time."))
            : SetAsync(_state with { Mode = AwakeMode.Expiration, ExpiresAt = expiresAt }, cancellationToken);

    public Task<AwakeOperationResult> SetKeepScreenOnAsync(bool keepScreenOn, CancellationToken cancellationToken = default) =>
        SetAsync(_state with { KeepScreenOn = keepScreenOn }, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private Task<AwakeOperationResult> SetAsync(AwakeState state, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Task.FromResult(Failed(AwakeOperationFailure.ShuttingDown, "Keep awake is shutting down."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Failed(AwakeOperationFailure.Cancelled, "The keep-awake request was cancelled."));
        }

        if (_state == state)
        {
            return Task.FromResult(AwakeOperationResult.Success);
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(AwakeOperationResult.Success);
    }

    private static AwakeOperationResult Failed(AwakeOperationFailure failure, string error) => new(false, error, failure);
}
