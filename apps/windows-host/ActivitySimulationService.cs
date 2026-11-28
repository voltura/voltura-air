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
    private readonly TimeSpan _loopStopTimeout;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private Task _retiringLoop = Task.CompletedTask;
    private bool _enabled;
    private int _failureStreak;
    private int _disposeState;

    internal ActivitySimulationService(
        IActivityPulseSender pulseSender,
        bool enabled,
        Action<bool>? save = null,
        IAppLogWriter? appLog = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? loopStopTimeout = null)
    {
        _pulseSender = pulseSender;
        _enabled = enabled;
        _save = save ?? AppActivitySimulationSettings.Save;
        _appLog = appLog ?? NullAppLog.Instance;
        _delay = delay ?? Task.Delay;
        _loopStopTimeout = loopStopTimeout ?? ShutdownTimeout;

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
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return new(false, "Simulated activity is shutting down.");
        }
        try
        {
            await _updates.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(false, "The simulated-activity change was cancelled.");
        }
        catch (ObjectDisposedException)
        {
            return new(false, "Simulated activity is shutting down.");
        }

        var publishStateChanged = false;
        Task? retiring = null;
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
            else if (enabled && !_retiringLoop.IsCompleted)
            {
                result = new(false, "The previous simulated-activity pulse is still finishing. Try again.");
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
                    Interlocked.Exchange(ref _failureStreak, 0);
                    _retiringLoop = retiring = StopLoopAsync(stoppedCancellation, stoppedLoop);
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
            if (retiring is not null)
            {
                await WaitForLoopStopAsync(retiring, "disable_timeout").ConfigureAwait(false);
            }
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
        Task stopping;
        try
        {
            Task? loop;
            CancellationTokenSource? cancellation;
            lock (_gate)
            {
                _enabled = false;
                loop = _loopTask;
                cancellation = _loopCancellation;
                _loopTask = null;
                _loopCancellation = null;
            }
            stopping = Task.WhenAll(_retiringLoop, StopLoopAsync(cancellation, loop));
        }
        finally
        {
            _updates.Release();
        }

        try
        {
            await WaitForLoopStopAsync(stopping, "shutdown_timeout").ConfigureAwait(false);
        }
        finally
        {
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
                TrySendPulse(cancellationToken);
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

    private void TrySendPulse(CancellationToken cancellationToken)
    {
        try
        {
            if (_pulseSender.TrySendActivityPulse() == ActivityPulseDispatchResult.Busy ||
                cancellationToken.IsCancellationRequested || !Enabled)
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
            if (cancellationToken.IsCancellationRequested || !Enabled ||
                Interlocked.CompareExchange(ref _failureStreak, 1, 0) != 0)
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
        try
        {
            if (cancellation is not null) await cancellation.CancelAsync().ConfigureAwait(false);
            if (loop is not null) await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    private async Task WaitForLoopStopAsync(Task stopping, string timeoutOutcome)
    {
        try
        {
            await stopping.WaitAsync(_loopStopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            WriteLog(timeoutOutcome, "A simulated-activity pulse is still finishing in Windows.");
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
