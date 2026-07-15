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

public sealed record SystemPowerExecutionResult(bool Succeeded, int? Win32Error = null)
{
    public static SystemPowerExecutionResult Success { get; } = new(true);
}

public sealed class NoOpSystemPowerController : ISystemPowerController
{
    public SystemPowerExecutionResult TryExecute(string action)
    {
        return SystemPowerActions.IsSupported(action) ? SystemPowerExecutionResult.Success : new(false);
    }

    public bool IsActionAvailable(string action) => SystemPowerActions.IsSupported(action);

    public bool DismissBlackoutIfActive() => false;
}

public sealed partial class SystemPowerController : ISystemPowerController, IDisposable
{
    private const uint WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;
    private const int MonitorPowerOff = 2;
    private static readonly nint HwndBroadcast = new(0xffff);
    private readonly Func<bool> _lockWorkStation;
    private readonly Func<bool> _turnOffDisplay;
    private readonly Func<int> _getLastWin32Error;
    private readonly IWindowsDisplayActionController _displayActions;

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

    public void Dispose()
    {
        _displayActions.Dispose();
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
