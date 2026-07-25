using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VolturaAir.Host;

public static class SystemPowerActions
{
    public const string Lock = "lock";
    public const string BlackoutDisplay = "blackoutDisplay";
    public const string DisplayOff = "displayOff";
    public const string ScreenSaver = "screenSaver";
    public const string SignOut = "signOut";
    public const string Restart = "restart";
    public const string Shutdown = "shutdown";

    public static bool IsSupported(string action)
    {
        return action is Lock or BlackoutDisplay or DisplayOff or ScreenSaver or SignOut or Restart or Shutdown;
    }
}

public interface ISystemPowerController
{
    SystemPowerExecutionResult TryExecute(string action);

    bool IsActionAvailable(string action);

    bool DismissBlackoutIfActive();
}

internal interface IPresentationBreakOverlay
{
    SystemPowerExecutionResult TryShowPresentationBreak(Func<TimeSpan> getElapsed);

    bool DismissPresentationBreakIfActive();
}

internal interface IPresentationBlankOverlay
{
    event EventHandler? StateChanged;

    PresentationBlankOverlaySnapshot? Snapshot { get; }

    SystemPowerExecutionResult TryShowPresentationBlank(
        string runtimePresentationId,
        bool white);

    bool DismissPresentationBlankIfActive();
}

internal sealed record PresentationBlankOverlaySnapshot(
    string RuntimePresentationId,
    string SlideShowState);

internal sealed class NoOpPresentationBlankOverlay : IPresentationBlankOverlay
{
    internal static NoOpPresentationBlankOverlay Instance { get; } = new();

    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public PresentationBlankOverlaySnapshot? Snapshot => null;

    public SystemPowerExecutionResult TryShowPresentationBlank(
        string runtimePresentationId,
        bool white) =>
        SystemPowerExecutionResult.Success;

    public bool DismissPresentationBlankIfActive() => false;
}

internal sealed class NoOpPresentationBreakOverlay : IPresentationBreakOverlay
{
    internal static NoOpPresentationBreakOverlay Instance { get; } = new();

    public SystemPowerExecutionResult TryShowPresentationBreak(Func<TimeSpan> getElapsed) =>
        SystemPowerExecutionResult.Success;

    public bool DismissPresentationBreakIfActive() => false;
}

public sealed record SystemPowerExecutionResult(bool Succeeded, int? Win32Error = null)
{
    public static SystemPowerExecutionResult Success { get; } = new(true);
}

public sealed class NoOpSystemPowerController :
    ISystemPowerController,
    IPresentationBreakOverlay,
    IPresentationBlankOverlay
{
    event EventHandler? IPresentationBlankOverlay.StateChanged
    {
        add { }
        remove { }
    }

    PresentationBlankOverlaySnapshot? IPresentationBlankOverlay.Snapshot => null;

    public SystemPowerExecutionResult TryExecute(string action)
    {
        return SystemPowerActions.IsSupported(action) ? SystemPowerExecutionResult.Success : new(false);
    }

    public bool IsActionAvailable(string action) => SystemPowerActions.IsSupported(action);

    public bool DismissBlackoutIfActive() => false;

    SystemPowerExecutionResult IPresentationBreakOverlay.TryShowPresentationBreak(
        Func<TimeSpan> getElapsed) => SystemPowerExecutionResult.Success;

    bool IPresentationBreakOverlay.DismissPresentationBreakIfActive() => false;

    SystemPowerExecutionResult IPresentationBlankOverlay.TryShowPresentationBlank(
        string runtimePresentationId,
        bool white) =>
        SystemPowerExecutionResult.Success;

    bool IPresentationBlankOverlay.DismissPresentationBlankIfActive() => false;
}

public sealed partial class SystemPowerController :
    ISystemPowerController,
    IPresentationBreakOverlay,
    IPresentationBlankOverlay,
    IDisposable
{
    private const uint WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;
    private const int MonitorPowerOff = 2;
    private static readonly nint HwndBroadcast = new(0xffff);
    private readonly Func<bool> _lockWorkStation;
    private readonly Func<bool> _turnOffDisplay;
    private readonly Func<int> _getLastWin32Error;
    private readonly IWindowsDisplayActionController _displayActions;
    private readonly Lock _presentationBlankGate = new();
    private PresentationBlankOverlaySnapshot? _presentationBlank;
    private long _presentationBlankGeneration;
    private event EventHandler? PresentationBlankStateChanged;

    public SystemPowerController()
        : this(
            LockWorkStation,
            TurnOffDisplay,
            Marshal.GetLastWin32Error,
            new WindowsDisplayActionController(Dispatcher.CurrentDispatcher, NullAppLog.Instance))
    {
    }

    internal SystemPowerController(IWindowsDisplayActionController displayActions)
        : this(LockWorkStation, TurnOffDisplay, Marshal.GetLastWin32Error, displayActions)
    {
    }

    internal SystemPowerController(
        Func<bool> lockWorkStation,
        Func<bool> turnOffDisplay,
        Func<int> getLastWin32Error)
        : this(lockWorkStation, turnOffDisplay, getLastWin32Error, new NoOpWindowsDisplayActionController())
    {
    }

    internal SystemPowerController(
        Func<bool> lockWorkStation,
        Func<bool> turnOffDisplay,
        Func<int> getLastWin32Error,
        IWindowsDisplayActionController displayActions)
    {
        _lockWorkStation = lockWorkStation;
        _turnOffDisplay = turnOffDisplay;
        _getLastWin32Error = getLastWin32Error;
        _displayActions = displayActions;
        _displayActions.BlankOverlayChanged += OnBlankOverlayChanged;
    }

    public SystemPowerExecutionResult TryExecute(string action)
    {
        try
        {
            return action switch
            {
                SystemPowerActions.Lock => GetNativeResult(_lockWorkStation()),
                SystemPowerActions.BlackoutDisplay => _displayActions.TryShowBlackout(),
                SystemPowerActions.DisplayOff => GetNativeResult(_turnOffDisplay()),
                SystemPowerActions.ScreenSaver => _displayActions.TryStartScreenSaver(),
                SystemPowerActions.SignOut => StartShutdownCommand("/l"),
                SystemPowerActions.Restart => StartShutdownCommand("/r", "/t", "0"),
                SystemPowerActions.Shutdown => StartShutdownCommand("/s", "/t", "0"),
                _ => new SystemPowerExecutionResult(false)
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("Voltura Air power action failed: action={0}, error={1}", action, ex.Message);
            return new SystemPowerExecutionResult(false, (ex as System.ComponentModel.Win32Exception)?.NativeErrorCode);
        }
    }

    public bool IsActionAvailable(string action)
    {
        return SystemPowerActions.IsSupported(action) &&
            (action != SystemPowerActions.ScreenSaver || _displayActions.IsScreenSaverAvailable);
    }

    public bool DismissBlackoutIfActive()
    {
        return _displayActions.DismissBlackoutIfActive();
    }

    SystemPowerExecutionResult IPresentationBreakOverlay.TryShowPresentationBreak(
        Func<TimeSpan> getElapsed)
    {
        return _displayActions.TryShowPresentationBreak(getElapsed);
    }

    bool IPresentationBreakOverlay.DismissPresentationBreakIfActive()
    {
        return _displayActions.DismissPresentationBreakIfActive();
    }

    event EventHandler? IPresentationBlankOverlay.StateChanged
    {
        add => PresentationBlankStateChanged += value;
        remove => PresentationBlankStateChanged -= value;
    }

    PresentationBlankOverlaySnapshot? IPresentationBlankOverlay.Snapshot
    {
        get
        {
            lock (_presentationBlankGate)
            {
                return _presentationBlank;
            }
        }
    }

    SystemPowerExecutionResult IPresentationBlankOverlay.TryShowPresentationBlank(
        string runtimePresentationId,
        bool white)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePresentationId);
        var requested = new PresentationBlankOverlaySnapshot(
            runtimePresentationId,
            white ? "white" : "black");
        var result = white
            ? _displayActions.TryShowWhiteout()
            : _displayActions.TryShowBlackout();
        var generation = Volatile.Read(ref _presentationBlankGeneration);
        SetPresentationBlank(
            result.Succeeded && _displayActions.IsBlankOverlayActive
                ? requested
                : null,
            generation);
        return result;
    }

    bool IPresentationBlankOverlay.DismissPresentationBlankIfActive()
    {
        return _displayActions.DismissBlackoutIfActive();
    }

    public void Dispose()
    {
        _displayActions.BlankOverlayChanged -= OnBlankOverlayChanged;
        _displayActions.Dispose();
    }

    private void OnBlankOverlayChanged(object? sender, EventArgs eventArgs)
    {
        _ = Interlocked.Increment(ref _presentationBlankGeneration);
        if (!_displayActions.IsBlankOverlayActive)
        {
            SetPresentationBlank(null);
        }
    }

    private void SetPresentationBlank(
        PresentationBlankOverlaySnapshot? value,
        long? expectedGeneration = null)
    {
        lock (_presentationBlankGate)
        {
            if (expectedGeneration is { } expected &&
                Volatile.Read(ref _presentationBlankGeneration) != expected)
            {
                return;
            }

            if (_presentationBlank == value)
            {
                return;
            }

            _presentationBlank = value;
        }

        PresentationBlankStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private SystemPowerExecutionResult GetNativeResult(bool succeeded)
    {
        return succeeded
            ? SystemPowerExecutionResult.Success
            : new SystemPowerExecutionResult(false, _getLastWin32Error());
    }

    private static SystemPowerExecutionResult StartShutdownCommand(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        return process is not null ? SystemPowerExecutionResult.Success : new SystemPowerExecutionResult(false);
    }

    private static bool TurnOffDisplay()
    {
        return SendNotifyMessage(HwndBroadcast, WmSysCommand, new nint(ScMonitorPower), new nint(MonitorPowerOff));
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SendNotifyMessage(nint hWnd, uint message, nint wParam, nint lParam);
}
