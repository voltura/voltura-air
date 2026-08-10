namespace VolturaAir.Host;

internal sealed class ActivitySimulationService : IActivitySimulationService
{
    internal static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(59);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _updates = new(1, 1);
    private readonly IActivityPulseSender _pulseSender;
    private readonly Action<bool> _save;
    private readonly IAppLogWriter _appLog;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private bool _enabled;
    private int _failureStreak;
    private int _disposeState;

    internal ActivitySimulationService(
        IActivityPulseSender pulseSender,
        bool enabled,
        Action<bool>? save = null,
        IAppLogWriter? appLog = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _pulseSender = pulseSender;
        _enabled = enabled;
        _save = save ?? AppActivitySimulationSettings.Save;
        _appLog = appLog ?? NullAppLog.Instance;
        _delay = delay ?? Task.Delay;

        if (enabled)
        {
            StartLoop();
        }
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public event EventHandler? StateChanged;

    public event EventHandler<ActivitySimulationFailureEventArgs>? FailureStreakStarted;

    public async Task<ActivitySimulationOperationResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _updates.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(false, "The simulated-activity change was cancelled.");
        }

        var publishStateChanged = false;
        ActivitySimulationOperationResult result;
        try
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                result = new(false, "Simulated activity is shutting down.");
            }
            else if (Enabled == enabled)
            {
                result = ActivitySimulationOperationResult.Success;
            }
            else
            {
                try
                {
                    _save(enabled);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    result = new(false, $"Simulated activity could not be saved: {exception.Message}");
                    return result;
                }

                Task? stoppedLoop = null;
                CancellationTokenSource? stoppedCancellation = null;
                lock (_gate)
                {
                    _enabled = enabled;
                    if (!enabled)
                    {
                        stoppedLoop = _loopTask;
                        stoppedCancellation = _loopCancellation;
                        _loopTask = null;
                        _loopCancellation = null;
                    }
                }

                if (enabled)
                {
                    StartLoop();
                }
                else
                {
                    await StopLoopAsync(stoppedCancellation, stoppedLoop).ConfigureAwait(false);
                    Interlocked.Exchange(ref _failureStreak, 0);
                }

                publishStateChanged = true;
                result = ActivitySimulationOperationResult.Success;
            }
        }
        finally
        {
            _updates.Release();
        }

        if (publishStateChanged)
        {
            ActivitySimulationNotifications.PublishStateChanged(this, StateChanged, _appLog);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await _updates.WaitAsync().ConfigureAwait(false);
        Task? loop;
        CancellationTokenSource? cancellation;
        try
        {
            lock (_gate)
            {
                _enabled = false;
                loop = _loopTask;
                cancellation = _loopCancellation;
                _loopTask = null;
                _loopCancellation = null;
            }
        }
        finally
        {
            _updates.Release();
        }

        if (loop is null)
        {
            cancellation?.Dispose();
            _updates.Dispose();
            return;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        try
        {
            await loop.WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            WriteLog("shutdown_timeout", "The simulated-activity loop did not stop before the shutdown deadline.");
        }
        finally
        {
            cancellation?.Dispose();
            _updates.Dispose();
        }
    }

    private void StartLoop()
    {
        var cancellation = new CancellationTokenSource();
        var loop = RunLoopAsync(cancellation.Token);
        lock (_gate)
        {
            _loopCancellation = cancellation;
            _loopTask = loop;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _delay(PulseInterval, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                TrySendPulse();
                if (!Enabled || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void TrySendPulse()
    {
        try
        {
            if (_pulseSender.TrySendActivityPulse() == ActivityPulseDispatchResult.Busy)
            {
                return;
            }

            if (Interlocked.Exchange(ref _failureStreak, 0) != 0)
            {
                WriteLog("recovered");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Interlocked.CompareExchange(ref _failureStreak, 1, 0) != 0)
            {
                return;
            }

            WriteLog("failed", exception.Message, exception as InputDispatchException);
            ActivitySimulationNotifications.PublishFailureStreakStarted(
                this,
                FailureStreakStarted,
                new ActivitySimulationFailureEventArgs(
                    "Windows did not accept the simulated activity pulse. Voltura Air will retry in 59 seconds."),
                _appLog);
        }
    }

    private static async Task StopLoopAsync(CancellationTokenSource? cancellation, Task? loop)
    {
        if (loop is null)
        {
            cancellation?.Dispose();
            return;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    private void WriteLog(string outcome, string? detail = null, InputDispatchException? inputException = null) =>
        _appLog.Write(new AppLogEntry(
            Event: "activity_simulation",
            Source: "windows_host",
            Action: "f15_key_up",
            Outcome: outcome,
            Code: inputException is null ? null : "native_send_failed",
            Win32Error: inputException?.Win32Error,
            Detail: detail));
}
