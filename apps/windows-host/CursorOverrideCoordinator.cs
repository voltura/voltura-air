namespace VolturaAir.Host;

internal interface ICursorOverrideController
{
    void ApplyCustomPointer(CustomPointerSettings settings);

    void SetPresentationLaserPointer(bool enabled);

    void ApplyPresentationLaserPointerSettings(PresentationLaserPointerSettings settings);
}

internal sealed class CursorOverrideCoordinator : ICursorOverrideController, IDisposable
{
    private static readonly TimeSpan InitialReadyTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryReadyTimeout = TimeSpan.FromMilliseconds(250);
    private readonly Lock _gate = new();
    private readonly CursorWatchdogService _recovery;
    private readonly CustomPointerService _pointer;
    private readonly IAppLogWriter _appLog;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _recoveryWorkerGate = new(1, 1);
    private Task? _recoveryWorker;
    private bool _laserEnabled;
    private bool _disposed;

    internal CursorOverrideCoordinator(
        CursorWatchdogService recovery,
        CustomPointerService pointer,
        IAppLogWriter appLog)
    {
        _recovery = recovery;
        _pointer = pointer;
        _appLog = appLog;
        _recovery.MonitoringLost += OnMonitoringLost;
    }

    internal event EventHandler? OverridesRevoked;

    internal bool IsRecoveryReady => _recovery.IsReady;

    internal async Task StartAsync()
    {
        if (!_pointer.RevokeOverrides())
        {
            DisablePersistedCustomPointer();
            LogRecovery("unavailable");
            QueueRecovery();
            return;
        }

        if (!await TryEnsureRecoveryAsync(InitialReadyTimeout).ConfigureAwait(false))
        {
            DisablePersistedCustomPointer();
            LogRecovery("unavailable");
            QueueRecovery();
            return;
        }

        var laserSettings = AppPointerSettings.GetPresentationLaserPointer();
        var customSettings = AppPointerSettings.GetCustomPointer();
        lock (_gate)
        {
            ThrowIfDisposed();
            _pointer.ApplyPresentationLaserPointerSettings(laserSettings);
            if (customSettings.Enabled)
            {
                ApplyProtected(() => _pointer.Apply(customSettings));
            }
        }
    }

    public void ApplyCustomPointer(CustomPointerSettings settings)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (settings.Enabled)
            {
                ApplyProtected(() => _pointer.Apply(settings));
            }
            else
            {
                _pointer.Apply(settings);
            }
        }
    }

    public void SetPresentationLaserPointer(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (enabled)
            {
                ApplyProtected(() => _pointer.SetPresentationLaserPointer(enabled: true));
                _laserEnabled = true;
            }
            else
            {
                _pointer.SetPresentationLaserPointer(enabled: false);
                _laserEnabled = false;
            }
        }
    }

    public void ApplyPresentationLaserPointerSettings(PresentationLaserPointerSettings settings)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_laserEnabled)
            {
                ApplyProtected(() => _pointer.ApplyPresentationLaserPointerSettings(settings));
            }
            else
            {
                _pointer.ApplyPresentationLaserPointerSettings(settings);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _recovery.MonitoringLost -= OnMonitoringLost;
        _lifetimeCancellation.Cancel();
        try
        {
            _recoveryWorker?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        RestoreForShutdown();
        _pointer.Dispose();
        _recovery.Dispose();
        _recoveryWorkerGate.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private void ApplyProtected(Action apply)
    {
        if (!_recovery.IsReady)
        {
            throw new CursorRecoveryUnavailableException("Cursor overrides are temporarily unavailable.");
        }

        apply();
        if (_recovery.IsReady)
        {
            return;
        }

        _ = _pointer.RevokeOverrides();
        DisablePersistedCustomPointer();
        QueueRecovery();
        throw new CursorRecoveryUnavailableException("Cursor overrides are temporarily unavailable.");
    }

    private void OnMonitoringLost(object? sender, EventArgs eventArgs)
    {
        LogRecovery("lost");
        QueueRecovery();
    }

    private void QueueRecovery()
    {
        lock (_gate)
        {
            if (_disposed || _recoveryWorker is { IsCompleted: false })
            {
                return;
            }

            _recoveryWorker = Task.Run(() => RecoverAndRestartAsync(_lifetimeCancellation.Token));
        }
    }

    private async Task RecoverAndRestartAsync(CancellationToken cancellationToken)
    {
        await _recoveryWorkerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisablePersistedCustomPointer();
            lock (_gate)
            {
                _laserEnabled = false;
            }

            OverridesRevoked?.Invoke(this, EventArgs.Empty);

            var delay = TimeSpan.FromMilliseconds(25);
            while (!_pointer.RevokeOverrides())
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
            }

            delay = TimeSpan.FromMilliseconds(100);
            while (!await TryEnsureRecoveryAsync(RetryReadyTimeout).ConfigureAwait(false))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
            }

            LogRecovery("ready");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _recoveryWorkerGate.Release();
        }
    }

    private async Task<bool> TryEnsureRecoveryAsync(TimeSpan timeout)
    {
        try
        {
            return await Task.Run(
                () => _recovery.TryEnsureMonitoring(timeout),
                _lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CursorRecoveryUnavailableException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static void DisablePersistedCustomPointer()
    {
        var current = AppPointerSettings.GetCustomPointer();
        if (current.Enabled)
        {
            AppPointerSettings.SetCustomPointer(current with { Enabled = false });
        }
    }

    private void LogRecovery(string outcome) =>
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "cursor_recovery",
            Outcome: outcome));

    private void RestoreForShutdown()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        var delay = 25;
        while (!_pointer.RevokeOverrides() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(delay);
            delay = Math.Min(delay * 2, 250);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed class InertCursorOverrideController : ICursorOverrideController
{
    internal static InertCursorOverrideController Instance { get; } = new();

    private InertCursorOverrideController()
    {
    }

    public void ApplyCustomPointer(CustomPointerSettings settings)
    {
    }

    public void SetPresentationLaserPointer(bool enabled)
    {
    }

    public void ApplyPresentationLaserPointerSettings(PresentationLaserPointerSettings settings)
    {
    }
}
