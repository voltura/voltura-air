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

public sealed record AwakeOperationResult(bool Succeeded, string? Error = null)
{
    public static AwakeOperationResult Success { get; } = new(true);
}

public interface IAwakeService : IDisposable
{
    AwakeState State { get; }

    event EventHandler? StateChanged;

    AwakeOperationResult SetOff();

    AwakeOperationResult SetIndefinite();

    AwakeOperationResult SetTimed(TimeSpan duration);

    AwakeOperationResult SetExpiration(DateTimeOffset expiresAt);

    AwakeOperationResult SetKeepScreenOn(bool keepScreenOn);
}

internal sealed class NoOpAwakeService : IAwakeService
{
    public NoOpAwakeService(AwakeState? initialState = null)
    {
        State = initialState ?? new AwakeState(AwakeMode.Off, false, 60, null);
    }

    public AwakeState State { get; private set; }

    public event EventHandler? StateChanged;

    public AwakeOperationResult SetOff() => Set(State with { Mode = AwakeMode.Off, ExpiresAt = null });

    public AwakeOperationResult SetIndefinite() => Set(State with { Mode = AwakeMode.Indefinite, ExpiresAt = null });

    public AwakeOperationResult SetTimed(TimeSpan duration) => Set(State with
    {
        Mode = AwakeMode.Timed,
        IntervalMinutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes)),
        ExpiresAt = DateTimeOffset.Now.Add(duration)
    });

    public AwakeOperationResult SetExpiration(DateTimeOffset expiresAt) =>
        expiresAt <= DateTimeOffset.Now
            ? new AwakeOperationResult(false, "Choose a future date and time.")
            : Set(State with { Mode = AwakeMode.Expiration, ExpiresAt = expiresAt });

    public AwakeOperationResult SetKeepScreenOn(bool keepScreenOn) => Set(State with { KeepScreenOn = keepScreenOn });

    public void Dispose()
    {
    }

    private AwakeOperationResult Set(AwakeState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return AwakeOperationResult.Success;
    }
}
