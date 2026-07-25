using System.Windows.Threading;

namespace VolturaAir.Host;

internal sealed class PowerPointAutomationService : IPowerPointAutomationService
{
    private static readonly TimeSpan SlowOperationThreshold = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumResponseWait = TimeSpan.FromSeconds(5);
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IAppLogWriter _appLog;
    private readonly Lock _snapshotGate = new();
    private readonly Lock _operationTailGate = new();
    private readonly Dictionary<string, Task<PowerPointAutomationResult>>
        _pointerRestores = new(StringComparer.Ordinal);
    private Dispatcher? _dispatcher;
#pragma warning disable CA2213 // Disposed on its owning STA dispatcher by DisposeBridge.
    private PowerPointComBridge? _bridge;
#pragma warning restore CA2213
    private PowerPointAutomationSnapshot _snapshot = PowerPointAutomationSnapshot.Unavailable;
    private Task _latestAutomationTask = Task.CompletedTask;
    private int _disposeState;

    internal PowerPointAutomationService(IAppLogWriter appLog)
    {
        _appLog = appLog;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Voltura Air PowerPoint automation"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
        RequestEventRefresh();
    }

    public PowerPointAutomationSnapshot Snapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler? SnapshotChanged;

    public Task<PowerPointAutomationResult> RefreshAsync(CancellationToken cancellationToken) =>
        RunOperationAsync(bridge =>
        {
            var snapshot = bridge.ReadSnapshot();
            Publish(snapshot);
            return snapshot.State == PowerPointDiscoveryState.Ready
                ? new(true, null, "PowerPoint presentations refreshed.", snapshot)
                : new(
                    false,
                    snapshot.State == PowerPointDiscoveryState.Inaccessible
                        ? "powerpoint-inaccessible"
                        : "powerpoint-unavailable",
                    snapshot.State == PowerPointDiscoveryState.Inaccessible
                        ? "PowerPoint denied access to its open presentations."
                        : "Open PowerPoint and a presentation on the PC, then refresh.",
                    snapshot);
        }, cancellationToken);

    public Task<PowerPointAutomationResult> ExecuteAsync(
        PowerPointCommand command,
        CancellationToken cancellationToken)
    {
        if (command is { Action: "pointer", Enabled: false } &&
            command.RuntimePresentationId is { Length: > 0 })
        {
            return QueuePointerRestoreAsync(command);
        }

        return RunOperationAsync(bridge =>
        {
            var result = bridge.Execute(command);
            Publish(result.Snapshot);
            return result;
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        var dispatcher = _dispatcher;
        if (dispatcher is not null)
        {
            var shutdownTask = dispatcher.InvokeAsync(() =>
            {
                DisposeBridge();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }).Task;
            try
            {
                await shutdownTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                WriteOutcome("shutdown-timeout");
            }
        }

        var stopped = !_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(3));
        if (stopped)
        {
            _operationGate.Dispose();
        }

        _ready.Dispose();
    }

    private async Task<PowerPointAutomationResult> RunOperationAsync(
        Func<PowerPointComBridge, PowerPointAutomationResult> operation,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return Unavailable("PowerPoint automation is shutting down.");
        }

        if (!_operationGate.Wait(0, cancellationToken))
        {
            return Busy();
        }

        Task<PowerPointAutomationResult>? operationTask = null;
        try
        {
            var dispatcher = _dispatcher;
            var bridge = _bridge;
            if (dispatcher is null || bridge is null)
            {
                _operationGate.Release();
                return Unavailable("PowerPoint automation is unavailable.");
            }

            operationTask = dispatcher.InvokeAsync(() => operation(bridge)).Task;
            lock (_operationTailGate)
            {
                _latestAutomationTask = operationTask;
            }

            _ = operationTask.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        _ = completed.Exception;
                    }

                    _operationGate.Release();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            var response = await AwaitAuthoritativeOperationAsync(
                operationTask,
                SlowOperationThreshold,
                MaximumResponseWait,
                () => WriteOutcome("slow")).ConfigureAwait(false);
            if (!response.Completed)
            {
                WriteOutcome("response-timeout");
                return Busy(
                    "PowerPoint is still completing the previous command. " +
                    "Wait a moment before trying another presentation control.");
            }

            return response.Result;
        }
        catch (OperationCanceledException)
        {
            if (operationTask is null)
            {
                _operationGate.Release();
            }

            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (operationTask is null)
            {
                _operationGate.Release();
            }

            WriteOutcome("failed", exception.Message);
            return Unavailable("PowerPoint automation could not complete the operation.");
        }
    }

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _bridge = new PowerPointComBridge(RequestEventRefresh);
        _ready.Set();
        Dispatcher.Run();
    }

    private void DisposeBridge()
    {
        _bridge?.Dispose();
        _bridge = null;
    }

    private void RequestEventRefresh()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            if (_bridge is null || Volatile.Read(ref _disposeState) != 0)
            {
                return;
            }

            var snapshot = _bridge.ReadSnapshot();
            Publish(snapshot);
        }, DispatcherPriority.Background);
    }

    private void Publish(PowerPointAutomationSnapshot snapshot)
    {
        var changed = false;
        lock (_snapshotGate)
        {
            if (_snapshot != snapshot)
            {
                _snapshot = snapshot;
                changed = true;
            }
        }

        if (changed)
        {
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Task<PowerPointAutomationResult> QueuePointerRestoreAsync(
        PowerPointCommand command)
    {
        var runtimePresentationId = command.RuntimePresentationId!;
        lock (_operationTailGate)
        {
            if (_pointerRestores.TryGetValue(
                    runtimePresentationId,
                    out var existing))
            {
                return existing;
            }

            var queued = QueueAfterOperationAsync(
                _latestAutomationTask,
                () => RunPointerRestoreAsync(command));
            _pointerRestores[runtimePresentationId] = queued;
            _ = queued.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        _ = completed.Exception;
                    }

                    lock (_operationTailGate)
                    {
                        if (_pointerRestores.TryGetValue(
                                runtimePresentationId,
                                out var current) &&
                            ReferenceEquals(current, completed))
                        {
                            _pointerRestores.Remove(runtimePresentationId);
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return queued;
        }
    }

    private async Task<PowerPointAutomationResult> RunPointerRestoreAsync(
        PowerPointCommand command)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return Unavailable("PowerPoint automation is shutting down.");
        }

        var dispatcher = _dispatcher;
        var bridge = _bridge;
        if (dispatcher is null || bridge is null)
        {
            return Unavailable("PowerPoint automation is unavailable.");
        }

        var operationTask = dispatcher.InvokeAsync(() =>
        {
            var result = bridge.Execute(command);
            Publish(result.Snapshot);
            return result;
        }, DispatcherPriority.Send).Task;
        var response = await AwaitAuthoritativeOperationAsync(
            operationTask,
            SlowOperationThreshold,
            MaximumResponseWait,
            () => WriteOutcome("pointer-restore-slow")).ConfigureAwait(false);
        if (!response.Completed)
        {
            WriteOutcome("pointer-restore-response-timeout");
            return Busy(
                "PowerPoint is still restoring its automatic pointer setting.");
        }

        return response.Result;
    }

    private PowerPointAutomationResult Busy(
        string message = "PowerPoint is busy. Wait a moment, then try again.")
    {
        var snapshot = Snapshot with { State = PowerPointDiscoveryState.Busy };
        return new(
            false,
            "powerpoint-busy",
            message,
            snapshot);
    }

    private PowerPointAutomationResult Unavailable(string message) =>
        new(false, "powerpoint-unavailable", message, Snapshot);

    private void WriteOutcome(string outcome, string? detail = null) =>
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "powerpoint_automation",
            Outcome: outcome,
            Detail: detail));

    internal static async Task<BoundedOperationResult<T>> AwaitAuthoritativeOperationAsync<T>(
        Task<T> operationTask,
        TimeSpan slowOperationThreshold,
        TimeSpan maximumResponseWait,
        Action onSlow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumResponseWait,
            slowOperationThreshold);
        var slowTask = Task.Delay(
            slowOperationThreshold,
            CancellationToken.None);
        var completed = await Task.WhenAny(
            operationTask,
            slowTask).ConfigureAwait(false);
        if (completed != operationTask)
        {
            onSlow();
            var responseTimeout = Task.Delay(
                maximumResponseWait - slowOperationThreshold,
                CancellationToken.None);
            completed = await Task.WhenAny(
                operationTask,
                responseTimeout).ConfigureAwait(false);
            if (completed != operationTask)
            {
                // The started COM call cannot be canceled safely. The owning
                // operation gate remains held until it finishes, so later
                // commands receive Busy instead of causing duplicate late
                // effects, while the WebSocket is free to process cleanup and
                // health traffic.
                return new(false, default!);
            }
        }

        return new(true, await operationTask.ConfigureAwait(false));
    }

    internal static async Task<T> QueueAfterOperationAsync<T>(
        Task precedingOperation,
        Func<Task<T>> cleanup)
    {
        try
        {
            await precedingOperation.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Cleanup still has to run after a failed or canceled mutation.
        }

        return await cleanup().ConfigureAwait(false);
    }
}

internal readonly record struct BoundedOperationResult<T>(
    bool Completed,
    T Result);
