using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VolturaAir.Host;

internal sealed partial class PointerHighlightForegroundMonitor : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private static readonly TimeSpan TaskbarActivationDelay = TimeSpan.FromMilliseconds(150);
    private readonly Dispatcher _dispatcher;
    private readonly IAppLogWriter _appLog;
    private readonly uint _hostIntegrityLevel;
    private readonly WinEventProc _callback;
    private readonly DispatcherTimer _taskbarActivationTimer;
    private readonly OwnedDispatcherAction _taskbarActivationAction;
    private nint _hook;
    private int _remoteInputBlocked;
    private bool _disposed;

    public PointerHighlightForegroundMonitor(IAppLogWriter appLog)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _appLog = appLog;
        _callback = OnForegroundWindowChanged;
        _taskbarActivationTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TaskbarActivationDelay
        };
        _taskbarActivationTimer.Tick += OnTaskbarActivationTimerTick;
        _taskbarActivationAction = new OwnedDispatcherAction(_dispatcher, ScheduleTaskbarActivationRecheck);

        if (!WindowsProcessIntegrity.TryGetCurrentProcessIntegrityLevel(out _hostIntegrityLevel))
        {
            WriteDiagnostic("host_integrity_unavailable");
            return;
        }

        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
        if (_hook == nint.Zero)
        {
            WriteDiagnostic("hook_failed", win32Error: Marshal.GetLastWin32Error());
            return;
        }

        WriteDiagnostic("started");
        UpdateOverlaySuppression(GetForegroundWindow());
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
        _taskbarActivationTimer.Stop();
        _taskbarActivationTimer.Tick -= OnTaskbarActivationTimerTick;
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

    private void ScheduleTaskbarActivationRecheck()
    {
        if (_disposed)
        {
            return;
        }

        _taskbarActivationTimer.Stop();
        _taskbarActivationTimer.Start();
        WriteDiagnostic("taskbar_activation_recheck_scheduled");
    }

    private void OnForegroundWindowChanged(
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
            UpdateOverlaySuppression(windowHandle);
        }
    }

    private void UpdateOverlaySuppression(nint windowHandle)
    {
        var integrityLevelKnown = WindowsProcessIntegrity.TryGetWindowIntegrityLevel(windowHandle, out var foregroundIntegrityLevel);
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

    private void OnTaskbarActivationTimerTick(object? sender, EventArgs e)
    {
        _taskbarActivationTimer.Stop();
        if (!_disposed)
        {
            UpdateOverlaySuppression(GetForegroundWindow());
        }
    }

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
