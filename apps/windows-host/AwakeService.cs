using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VolturaAir.Host;

[Flags]
internal enum AwakeExecutionState : uint
{
    Continuous = 0x80000000,
    SystemRequired = 0x00000001,
    DisplayRequired = 0x00000002
}

internal interface IAwakeExecutionStateBridge
{
    bool TrySet(AwakeExecutionState state);
}

internal sealed partial class WindowsAwakeExecutionStateBridge : IAwakeExecutionStateBridge
{
    public bool TrySet(AwakeExecutionState state) => SetThreadExecutionState(state) != 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial AwakeExecutionState SetThreadExecutionState(AwakeExecutionState executionState);
}

internal sealed class NoOpAwakeExecutionStateBridge : IAwakeExecutionStateBridge
{
    public bool TrySet(AwakeExecutionState state) => true;
}

public sealed class AwakeService : IAwakeService
{
    private const int RequestQueueCapacity = 8;
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly Lock _gate = new();
    private readonly IAwakeExecutionStateBridge _bridge;
    private readonly Action<AwakeState> _save;
    private readonly IAppLogWriter _appLog;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The background execution thread disposes the queue after a bounded DisposeAsync may return while an uninterruptible native call is still running.")]
    private readonly BlockingCollection<ExecutionRequest> _requests = new(RequestQueueCapacity);
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The background execution thread disposes cancellation state after a bounded DisposeAsync may return while an uninterruptible native call is still running.")]
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly TaskCompletionSource _workerCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _executionThread;
    private readonly TimeSpan _operationTimeout;
    private readonly TimeSpan _shutdownTimeout;
    private System.Threading.Timer? _expirationTimer;
    private AwakeState _state;
    private bool _unavailable;
    private int _disposeState;

    internal AwakeService(
        IAwakeExecutionStateBridge bridge,
        AwakeState initialState,
        Action<AwakeState>? save = null,
        IAppLogWriter? appLog = null,
        TimeSpan? operationTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        _bridge = bridge;
        _save = save ?? AppAwakeSettings.Save;
        _appLog = appLog ?? NullAppLog.Instance;
        _operationTimeout = operationTimeout ?? DefaultOperationTimeout;
        _shutdownTimeout = shutdownTimeout ?? ShutdownTimeout;
        _state = NormalizeInitialState(initialState);
        _executionThread = new Thread(ProcessRequests)
        {
            IsBackground = true,
            Name = "Voltura Air Awake"
        };
        _executionThread.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ReplaceExpirationTimer(_state);
    }

    public static async Task<AwakeService> CreateWindowsAsync(IAppLogWriter? appLog = null)
    {
        var service = new AwakeService(
            new WindowsAwakeExecutionStateBridge(),
            AppAwakeSettings.Load(),
            appLog: appLog);
        await service.InitializeAsync().ConfigureAwait(false);
        return service;
    }

    public AwakeState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler? StateChanged;

    public Task<AwakeOperationResult> SetOffAsync(CancellationToken cancellationToken = default) =>
        SubmitAsync(AwakeMutation.Off, cancellationToken);

    public Task<AwakeOperationResult> SetIndefiniteAsync(CancellationToken cancellationToken = default) =>
        SubmitAsync(AwakeMutation.Indefinite, cancellationToken);

    public Task<AwakeOperationResult> SetTimedAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var minutes = (int)Math.Ceiling(duration.TotalMinutes);
        return minutes is < 1 or > 525_600
            ? Task.FromResult(Failed(AwakeOperationFailure.InvalidRequest, "Choose an interval between 1 minute and 1 year."))
            : SubmitAsync(AwakeMutation.Timed(minutes), cancellationToken);
    }

    public Task<AwakeOperationResult> SetExpirationAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        expiresAt <= DateTimeOffset.Now
            ? Task.FromResult(Failed(AwakeOperationFailure.InvalidRequest, "Choose a future date and time."))
            : SubmitAsync(AwakeMutation.Expiration(expiresAt), cancellationToken);

    public Task<AwakeOperationResult> SetKeepScreenOnAsync(bool keepScreenOn, CancellationToken cancellationToken = default) =>
        SubmitAsync(AwakeMutation.KeepScreenOn(keepScreenOn), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            await _shutdownCancellation.CancelAsync().ConfigureAwait(false);
            ReplaceExpirationTimer(null);
            _requests.CompleteAdding();
        }

        try
        {
            await _workerCompletion.Task.WaitAsync(_shutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogState(State, "shutdown_timeout", "The keep-awake execution thread did not stop before the shutdown deadline.");
        }
    }

    private async Task InitializeAsync()
    {
        var initialState = State;
        if (!initialState.IsActive)
        {
            return;
        }

        var result = await SubmitAsync(AwakeMutation.Reapply, CancellationToken.None).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return;
        }

        var offState = initialState with { Mode = AwakeMode.Off, ExpiresAt = null };
        SetCommittedState(offState);
        SaveState(offState);
        ReplaceExpirationTimer(offState);
        LogState(offState, "execution_failed", result.Error);
        var restoreResult = await SubmitAsync(AwakeMutation.Reapply, CancellationToken.None).ConfigureAwait(false);
        if (!restoreResult.Succeeded)
        {
            LogState(offState, "initial_state_restore_failed", restoreResult.Error);
        }
    }

    private Task<AwakeOperationResult> SubmitAsync(AwakeMutation mutation, CancellationToken cancellationToken)
    {
        AwakeOperationResult? rejection = null;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                rejection = Failed(AwakeOperationFailure.ShuttingDown, "Keep awake is shutting down.");
            }
            else if (_unavailable)
            {
                rejection = Failed(AwakeOperationFailure.Unavailable, "Keep awake is unavailable until Voltura Air restarts.");
            }
        }

        if (rejection is not null)
        {
            return Task.FromResult(rejection);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Failed(AwakeOperationFailure.Cancelled, "The keep-awake request was cancelled."));
        }

        var request = new ExecutionRequest(mutation);
        try
        {
            if (!_requests.TryAdd(request))
            {
                return Task.FromResult(Failed(AwakeOperationFailure.Busy, "Keep awake is busy. Try again."));
            }
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(Failed(AwakeOperationFailure.ShuttingDown, "Keep awake is shutting down."));
        }

        return AwaitRequestAsync(request, cancellationToken);
    }

    private async Task<AwakeOperationResult> AwaitRequestAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCancellation.Token);
        try
        {
            return await request.Completion.Task.WaitAsync(_operationTimeout, cancellation.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var result = Failed(AwakeOperationFailure.TimedOut, "Windows did not respond to the keep-awake request in time.");
            return request.TryAbandon(result)
                ? result
                : await request.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var result = Volatile.Read(ref _disposeState) != 0
                ? Failed(AwakeOperationFailure.ShuttingDown, "Keep awake is shutting down.")
                : Failed(AwakeOperationFailure.Cancelled, "The keep-awake request was cancelled.");
            return request.TryAbandon(result)
                ? result
                : await request.Completion.Task.ConfigureAwait(false);
        }
    }

    private void ProcessRequests()
    {
        try
        {
            foreach (var request in _requests.GetConsumingEnumerable())
            {
                ProcessRequest(request);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogState(State, "worker_failed", ex.Message);
        }
        finally
        {
            TryRestoreNormalExecutionState();
            _workerCompletion.TrySetResult();
            _requests.Dispose();
            _shutdownCancellation.Dispose();
        }
    }

    private void ProcessRequest(ExecutionRequest request)
    {
        if (!request.TryStart())
        {
            return;
        }

        var currentState = State;
        var nextState = request.Mutation.Apply(currentState);
        if (!request.Mutation.ForceApply && nextState == currentState)
        {
            if (request.TryClaimCompletion())
            {
                request.PublishCompletion(AwakeOperationResult.Success);
            }

            return;
        }

        var accepted = TryApplyExecutionState(nextState);
        var result = accepted
            ? AwakeOperationResult.Success
            : Failed(AwakeOperationFailure.Rejected, "Windows rejected the keep-awake request.");
        if (!request.TryClaimCompletion())
        {
            if (accepted)
            {
                ReconcileLateCompletion(nextState);
            }

            return;
        }

        if (!accepted || !request.Mutation.CommitState)
        {
            if (!accepted)
            {
                LogState(currentState, "execution_failed", result.Error);
            }

            request.PublishCompletion(result);
            return;
        }

        SetCommittedState(nextState);
        SaveState(nextState);
        ReplaceExpirationTimer(nextState);
        LogState(nextState, "changed");
        PublishStateChanged();
        request.PublishCompletion(result);
    }

    private void ReconcileLateCompletion(AwakeState appliedState)
    {
        var committedState = Volatile.Read(ref _disposeState) != 0
            ? State with { Mode = AwakeMode.Off, ExpiresAt = null }
            : State;
        if (ToExecutionState(appliedState) == ToExecutionState(committedState))
        {
            return;
        }

        if (TryApplyExecutionState(committedState))
        {
            LogState(committedState, "late_request_restored");
            return;
        }

        lock (_gate)
        {
            _unavailable = true;
        }

        LogState(committedState, "late_request_restore_failed", "Windows rejected restoration after a late keep-awake request.");
    }

    private bool TryApplyExecutionState(AwakeState state)
    {
        try
        {
            return _bridge.TrySet(ToExecutionState(state));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogState(State, "execution_failed", ex.Message);
            return false;
        }
    }

    private static AwakeExecutionState ToExecutionState(AwakeState state)
    {
        var executionState = AwakeExecutionState.Continuous;
        if (state.IsActive)
        {
            executionState |= AwakeExecutionState.SystemRequired;
            if (state.KeepScreenOn)
            {
                executionState |= AwakeExecutionState.DisplayRequired;
            }
        }

        return executionState;
    }

    private void SetCommittedState(AwakeState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }

    private void SaveState(AwakeState state)
    {
        try
        {
            _save(state);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogState(state, "persistence_failed", ex.Message);
        }
    }

    private void ReplaceExpirationTimer(AwakeState? state)
    {
        System.Threading.Timer? previous;
        lock (_gate)
        {
            previous = _expirationTimer;
            _expirationTimer = null;
        }

        previous?.Dispose();
        if (state is not { Mode: AwakeMode.Timed or AwakeMode.Expiration, ExpiresAt: { } expiresAt } ||
            Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var due = expiresAt - DateTimeOffset.Now;
        System.Threading.Timer? timer = new(OnExpiration, null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposeState) == 0 && _state == state)
            {
                _expirationTimer = timer;
                timer = null;
            }
        }

        timer?.Dispose();
    }

    private void OnExpiration(object? state) => _ = CompleteExpirationAsync();

    private async Task CompleteExpirationAsync()
    {
        try
        {
            var result = await SetOffAsync().ConfigureAwait(false);
            if (!result.Succeeded && result.Failure is not AwakeOperationFailure.ShuttingDown)
            {
                LogState(State, "expiration_failed", result.Error);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogState(State, "expiration_failed", ex.Message);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Resume or PowerModes.StatusChange && State.IsActive)
        {
            _ = ReapplyAfterPowerChangeAsync();
        }
    }

    private async Task ReapplyAfterPowerChangeAsync()
    {
        try
        {
            var result = await SubmitAsync(AwakeMutation.Reapply, CancellationToken.None).ConfigureAwait(false);
            if (!result.Succeeded && result.Failure is not AwakeOperationFailure.ShuttingDown)
            {
                LogState(State, "resume_reapply_failed", result.Error);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogState(State, "resume_reapply_failed", ex.Message);
        }
    }

    private void TryRestoreNormalExecutionState()
    {
        try
        {
            _ = _bridge.TrySet(AwakeExecutionState.Continuous);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogState(State, "shutdown_restore_failed", ex.Message);
        }
    }

    private static AwakeState NormalizeInitialState(AwakeState state)
    {
        var interval = AppAwakeSettings.NormalizeIntervalMinutes(state.IntervalMinutes);
        return state.Mode is AwakeMode.Timed or AwakeMode.Expiration &&
            (state.ExpiresAt is null || state.ExpiresAt <= DateTimeOffset.Now)
            ? state with { Mode = AwakeMode.Off, IntervalMinutes = interval, ExpiresAt = null }
            : state with { IntervalMinutes = interval };
    }

    private void LogState(AwakeState state, string outcome, string? detail = null)
    {
        try
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "keep_awake",
                Outcome: outcome,
                Detail: detail ?? $"mode={state.Mode}; keepScreenOn={state.KeepScreenOn}"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Diagnostics must not terminate the execution-state owner.
        }
    }

    private void PublishStateChanged()
    {
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogState(State, "state_notification_failed", ex.Message);
            }
        }
    }

    private static AwakeOperationResult Failed(AwakeOperationFailure failure, string error) => new(false, error, failure);

    private enum AwakeMutationKind
    {
        Off,
        Indefinite,
        Timed,
        Expiration,
        KeepScreenOn,
        Reapply
    }

    private readonly record struct AwakeMutation(
        AwakeMutationKind Kind,
        int IntervalMinutes = 0,
        DateTimeOffset? ExpiresAt = null,
        bool KeepScreenOnValue = false)
    {
        public static AwakeMutation Off { get; } = new(AwakeMutationKind.Off);
        public static AwakeMutation Indefinite { get; } = new(AwakeMutationKind.Indefinite);
        public static AwakeMutation Reapply { get; } = new(AwakeMutationKind.Reapply);
        public static AwakeMutation Timed(int minutes) => new(AwakeMutationKind.Timed, IntervalMinutes: minutes);
        public static AwakeMutation Expiration(DateTimeOffset expiresAt) => new(AwakeMutationKind.Expiration, ExpiresAt: expiresAt);
        public static AwakeMutation KeepScreenOn(bool value) => new(AwakeMutationKind.KeepScreenOn, KeepScreenOnValue: value);

        public bool CommitState => Kind != AwakeMutationKind.Reapply;
        public bool ForceApply => Kind == AwakeMutationKind.Reapply;

        public AwakeState Apply(AwakeState state) => Kind switch
        {
            AwakeMutationKind.Off => state with { Mode = AwakeMode.Off, ExpiresAt = null },
            AwakeMutationKind.Indefinite => state with { Mode = AwakeMode.Indefinite, ExpiresAt = null },
            AwakeMutationKind.Timed => state with
            {
                Mode = AwakeMode.Timed,
                IntervalMinutes = IntervalMinutes,
                ExpiresAt = DateTimeOffset.Now.AddMinutes(IntervalMinutes)
            },
            AwakeMutationKind.Expiration => state with { Mode = AwakeMode.Expiration, ExpiresAt = ExpiresAt },
            AwakeMutationKind.KeepScreenOn => state with { KeepScreenOn = KeepScreenOnValue },
            _ => state
        };
    }

    private sealed class ExecutionRequest(AwakeMutation mutation)
    {
        private int _state;

        public AwakeMutation Mutation { get; } = mutation;
        public TaskCompletionSource<AwakeOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        public bool TryClaimCompletion() => Interlocked.CompareExchange(ref _state, 3, 1) == 1;

        public void PublishCompletion(AwakeOperationResult result) => Completion.TrySetResult(result);

        public bool TryAbandon(AwakeOperationResult result)
        {
            while (true)
            {
                var state = Volatile.Read(ref _state);
                if (state is 2 or 3)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _state, 2, state) == state)
                {
                    Completion.TrySetResult(result);
                    return true;
                }
            }
        }
    }
}
