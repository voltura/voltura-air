using System.Collections.Concurrent;
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
    private readonly Lock _gate = new();
    private readonly IAwakeExecutionStateBridge _bridge;
    private readonly Action<AwakeState> _save;
    private readonly IAppLog _appLog;
    private readonly BlockingCollection<ExecutionRequest> _requests = [];
    private readonly Thread _executionThread;
    private System.Threading.Timer? _expirationTimer;
    private AwakeState _state;
    private bool _disposed;

    internal AwakeService(
        IAwakeExecutionStateBridge bridge,
        AwakeState initialState,
        Action<AwakeState>? save = null,
        IAppLog? appLog = null)
    {
        _bridge = bridge;
        _save = save ?? AppAwakeSettings.Save;
        _appLog = appLog ?? NullAppLog.Instance;
        _state = NormalizeInitialState(initialState);
        _executionThread = new Thread(ProcessRequests)
        {
            IsBackground = true,
            Name = "Voltura Air Awake"
        };
        _executionThread.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        if (_state.IsActive && !ApplyExecutionState(_state))
        {
            _state = _state with { Mode = AwakeMode.Off, ExpiresAt = null };
            _save(_state);
            LogState("execution_failed", "Windows rejected the persisted keep-awake request.");
        }

        ScheduleExpiration();
    }

    public static AwakeService CreateWindows(IAppLog? appLog = null) => new(
        new WindowsAwakeExecutionStateBridge(),
        AppAwakeSettings.Load(),
        appLog: appLog);

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

    public AwakeOperationResult SetOff() => TrySetState(State with { Mode = AwakeMode.Off, ExpiresAt = null });

    public AwakeOperationResult SetIndefinite() => TrySetState(State with { Mode = AwakeMode.Indefinite, ExpiresAt = null });

    public AwakeOperationResult SetTimed(TimeSpan duration)
    {
        var minutes = (int)Math.Ceiling(duration.TotalMinutes);
        if (minutes <= 0 || minutes > 525_600)
        {
            return new AwakeOperationResult(false, "Choose an interval between 1 minute and 1 year.");
        }

        return TrySetState(State with
        {
            Mode = AwakeMode.Timed,
            IntervalMinutes = minutes,
            ExpiresAt = DateTimeOffset.Now.AddMinutes(minutes)
        });
    }

    public AwakeOperationResult SetExpiration(DateTimeOffset expiresAt)
    {
        if (expiresAt <= DateTimeOffset.Now)
        {
            return new AwakeOperationResult(false, "Choose a future date and time.");
        }

        return TrySetState(State with { Mode = AwakeMode.Expiration, ExpiresAt = expiresAt });
    }

    public AwakeOperationResult SetKeepScreenOn(bool keepScreenOn) =>
        TrySetState(State with { KeepScreenOn = keepScreenOn });

    private AwakeOperationResult TrySetState(AwakeState next)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return new AwakeOperationResult(false, "Keep awake is shutting down.");
            }

            if (_state == next)
            {
                return AwakeOperationResult.Success;
            }

            if (!ApplyExecutionState(next))
            {
                LogState("execution_failed", "Windows rejected the requested execution state.");
                return new AwakeOperationResult(false, "Windows rejected the keep-awake request.");
            }

            _state = next;
            _save(_state);
            ScheduleExpiration();
            LogState("changed");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return AwakeOperationResult.Success;
    }

    private bool ApplyExecutionState(AwakeState state)
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

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _requests.Add(new ExecutionRequest(executionState, completion));
            return completion.Task.GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ProcessRequests()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            try
            {
                request.Completion.SetResult(_bridge.TrySet(request.State));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                request.Completion.SetResult(false);
            }
        }
    }

    private void ScheduleExpiration()
    {
        _expirationTimer?.Dispose();
        _expirationTimer = null;
        if (_state.Mode is not (AwakeMode.Timed or AwakeMode.Expiration) || _state.ExpiresAt is not { } expiresAt)
        {
            return;
        }

        var due = expiresAt - DateTimeOffset.Now;
        if (due <= TimeSpan.Zero)
        {
            ThreadPool.QueueUserWorkItem(_ => SetOff());
            return;
        }

        _expirationTimer = new System.Threading.Timer(_ => SetOff(), null, due, Timeout.InfiniteTimeSpan);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Resume or PowerModes.StatusChange && State.IsActive)
        {
            _ = ApplyExecutionState(State);
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
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _expirationTimer?.Dispose();
            _expirationTimer = null;
            _ = ApplyExecutionState(_state with { Mode = AwakeMode.Off, ExpiresAt = null });
            _requests.CompleteAdding();
        }

        _executionThread.Join(TimeSpan.FromSeconds(2));
        _requests.Dispose();
    }

    private static AwakeState NormalizeInitialState(AwakeState state)
    {
        var interval = AppAwakeSettings.NormalizeIntervalMinutes(state.IntervalMinutes);
        return state.Mode is AwakeMode.Timed or AwakeMode.Expiration &&
            (state.ExpiresAt is null || state.ExpiresAt <= DateTimeOffset.Now)
            ? state with { Mode = AwakeMode.Off, IntervalMinutes = interval, ExpiresAt = null }
            : state with { IntervalMinutes = interval };
    }

    private void LogState(string outcome, string? detail = null)
    {
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "keep_awake",
            Outcome: outcome,
            Detail: detail ?? $"mode={_state.Mode}; keepScreenOn={_state.KeepScreenOn}"));
    }

    private sealed record ExecutionRequest(AwakeExecutionState State, TaskCompletionSource<bool> Completion);
}
