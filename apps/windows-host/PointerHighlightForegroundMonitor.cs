using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VolturaAir.Host;

internal sealed partial class PointerHighlightForegroundMonitor : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private static readonly TimeSpan ForegroundSettleDelay = TimeSpan.FromMilliseconds(150);
    private readonly Dispatcher _dispatcher;
    private readonly IAppLogWriter _appLog;
    private readonly uint _hostIntegrityLevel;
    private readonly Func<nint> _getForegroundWindow;
    private readonly Func<nint, uint?> _getWindowIntegrityLevel;
    private readonly WinEventProc _callback;
    private readonly DispatcherTimer _foregroundRecheckTimer;
    private readonly OwnedDispatcherAction _taskbarActivationAction;
    private nint _hook;
    private int _remoteInputBlocked;
    private bool _disposed;

    public PointerHighlightForegroundMonitor(IAppLogWriter appLog)
        : this(appLog, 0, GetForegroundWindow, GetWindowIntegrityLevel)
    {
        if (!WindowsProcessIntegrity.TryGetCurrentProcessIntegrityLevel(out _hostIntegrityLevel))
        {
            WriteDiagnostic("host_integrity_unavailable");
            return;
        }

        // Switching to a host window must also refresh the reported input state.
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext);
        if (_hook == nint.Zero)
        {
            WriteDiagnostic("hook_failed", win32Error: Marshal.GetLastWin32Error());
            return;
        }

        WriteDiagnostic("started");
        UpdateOverlaySuppression(_getForegroundWindow());
    }

    internal PointerHighlightForegroundMonitor(
        IAppLogWriter appLog,
        uint hostIntegrityLevel,
        Func<nint> getForegroundWindow,
        Func<nint, uint?> getWindowIntegrityLevel)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _appLog = appLog;
        _hostIntegrityLevel = hostIntegrityLevel;
        _getForegroundWindow = getForegroundWindow;
        _getWindowIntegrityLevel = getWindowIntegrityLevel;
        _callback = OnForegroundWindowChanged;
        _foregroundRecheckTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = ForegroundSettleDelay
        };
        _foregroundRecheckTimer.Tick += OnForegroundRecheckTimerTick;
        _taskbarActivationAction = new OwnedDispatcherAction(_dispatcher, ScheduleForegroundRecheck);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Dispose);
            return;
        }

        _disposed = true;
        _taskbarActivationAction.Dispose();
        _foregroundRecheckTimer.Stop();
        _foregroundRecheckTimer.Tick -= OnForegroundRecheckTimerTick;
        if (_hook != nint.Zero)
        {
            _ = UnhookWinEvent(_hook);
            _hook = nint.Zero;
        }
    }

    internal bool IsRemoteInputBlocked => Volatile.Read(ref _remoteInputBlocked) != 0;

    internal event EventHandler<RemoteInputBlockedChangedEventArgs>? RemoteInputBlockedChanged;

    internal void NotifyTaskbarActivation()
    {
        if (_disposed)
        {
            return;
        }

        _taskbarActivationAction.Queue(DispatcherPriority.Background);
    }

    private void ScheduleForegroundRecheck()
    {
        if (_disposed)
        {
            return;
        }

        _foregroundRecheckTimer.Stop();
        _foregroundRecheckTimer.Start();
    }

    internal void OnForegroundWindowChanged(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (!_disposed && eventType == EventSystemForeground)
        {
            // Let transient notification/task-switcher focus settle, then query the current
            // foreground window instead of trusting a potentially stale event handle.
            ScheduleForegroundRecheck();
        }
    }

    private void UpdateOverlaySuppression(nint windowHandle)
    {
        var integrityLevel = _getWindowIntegrityLevel(windowHandle);
        var integrityLevelKnown = integrityLevel.HasValue;
        var foregroundIntegrityLevel = integrityLevel.GetValueOrDefault();
        var remoteInputBlocked = integrityLevelKnown && WindowsProcessIntegrity.IsHigherIntegrity(_hostIntegrityLevel, foregroundIntegrityLevel);
        WriteDiagnostic(
            !integrityLevelKnown
                ? "foreground_integrity_unavailable"
                : remoteInputBlocked
                    ? "foreground_higher_integrity"
                    : "foreground_not_higher_integrity",
            integrityLevelKnown ? $"host={_hostIntegrityLevel};foreground={foregroundIntegrityLevel}" : null);
        if (Interlocked.Exchange(ref _remoteInputBlocked, remoteInputBlocked ? 1 : 0) != (remoteInputBlocked ? 1 : 0))
        {
            RemoteInputBlockedChanged?.Invoke(this, new RemoteInputBlockedChangedEventArgs(remoteInputBlocked));
        }
    }

    private void OnForegroundRecheckTimerTick(object? sender, EventArgs e)
    {
        _foregroundRecheckTimer.Stop();
        if (!_disposed)
        {
            UpdateOverlaySuppression(_getForegroundWindow());
        }
    }

    private static uint? GetWindowIntegrityLevel(nint windowHandle) =>
        WindowsProcessIntegrity.TryGetWindowIntegrityLevel(windowHandle, out var level) ? level : null;

    private void WriteDiagnostic(string outcome, string? detail = null, int? win32Error = null)
    {
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "foreground_input_monitor",
            Outcome: outcome,
            Win32Error: win32Error,
            Detail: detail));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    // LibraryImport does not currently support this managed callback signature.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll", EntryPoint = "UnhookWinEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindow();
}

internal sealed class RemoteInputBlockedChangedEventArgs(bool isBlocked) : EventArgs
{
    public bool IsBlocked { get; } = isBlocked;
}
