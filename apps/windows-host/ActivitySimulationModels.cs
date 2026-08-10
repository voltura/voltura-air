namespace VolturaAir.Host;

internal enum ActivityPulseDispatchResult
{
    Sent,
    Busy
}

internal interface IActivityPulseSender
{
    ActivityPulseDispatchResult TrySendActivityPulse();
}

internal sealed record ActivitySimulationOperationResult(bool Succeeded, string? Error = null)
{
    public static ActivitySimulationOperationResult Success { get; } = new(true);
}

internal sealed class ActivitySimulationFailureEventArgs(string error) : EventArgs
{
    public string Error { get; } = error;
}

internal interface IActivitySimulationService : IAsyncDisposable
{
    bool Enabled { get; }

    event EventHandler? StateChanged;

    event EventHandler<ActivitySimulationFailureEventArgs>? FailureStreakStarted;

    Task<ActivitySimulationOperationResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}

internal sealed class NoOpActivityPulseSender : IActivityPulseSender
{
    public static NoOpActivityPulseSender Instance { get; } = new();

    private NoOpActivityPulseSender()
    {
    }

    public ActivityPulseDispatchResult TrySendActivityPulse() => ActivityPulseDispatchResult.Sent;
}

internal sealed class InertActivitySimulationService : IActivitySimulationService
{
    private bool _enabled;

    public bool Enabled => _enabled;

    public event EventHandler? StateChanged;

    public event EventHandler<ActivitySimulationFailureEventArgs>? FailureStreakStarted
    {
        add { }
        remove { }
    }

    public Task<ActivitySimulationOperationResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new ActivitySimulationOperationResult(false, "The simulated-activity change was cancelled."));
        }

        _enabled = enabled;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(ActivitySimulationOperationResult.Success);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
