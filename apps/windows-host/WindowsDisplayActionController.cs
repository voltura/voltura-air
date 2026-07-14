using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;

namespace VolturaAir.Host;

internal interface IWindowsDisplayActionController : IDisposable
{
    bool IsScreenSaverAvailable { get; }

    SystemPowerExecutionResult TryShowBlackout();

    SystemPowerExecutionResult TryStartScreenSaver();

    bool DismissBlackoutIfActive();
}

internal sealed class NoOpWindowsDisplayActionController : IWindowsDisplayActionController
{
    public bool IsScreenSaverAvailable => false;

    public SystemPowerExecutionResult TryShowBlackout() => SystemPowerExecutionResult.Success;

    public SystemPowerExecutionResult TryStartScreenSaver() => new(false);

    public bool DismissBlackoutIfActive() => false;

    public void Dispose()
    {
    }
}

internal sealed class WindowsDisplayActionController : IWindowsDisplayActionController
{
    private const uint SpiGetScreenSaveActive = 0x0010;
    private const uint WmSysCommand = 0x0112;
    private const int ScScreenSave = 0xF140;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndBroadcast = new(0xffff);
    private static readonly nint HwndTopmost = new(-1);
    private readonly Dispatcher _dispatcher;
    private readonly IAppLog _appLog;
    private readonly List<Window> _blackoutWindows = [];
    private int _blackoutActive;
    private DateTimeOffset _inputArmedAt;

    public WindowsDisplayActionController(Dispatcher dispatcher, IAppLog appLog)
    {
        _dispatcher = dispatcher;
        _appLog = appLog;
    }

    public bool IsScreenSaverAvailable
    {
        get
        {
            try
            {
                if (!SystemParametersInfo(SpiGetScreenSaveActive, 0, out var enabled, 0) || !enabled)
                {
                    return false;
                }

                var configuredPath = ReadConfiguredScreenSaverPath();
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    return false;
                }

                var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
                return File.Exists(expandedPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return false;
            }
        }
    }

    public SystemPowerExecutionResult TryShowBlackout()
    {
        try
        {
            return _dispatcher.CheckAccess()
                ? ShowBlackoutCore()
                : _dispatcher.Invoke(ShowBlackoutCore);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "blackout_display",
                Outcome: "failed",
                Detail: ex.Message));
            return new SystemPowerExecutionResult(false, (ex as System.ComponentModel.Win32Exception)?.NativeErrorCode);
        }
    }

    public SystemPowerExecutionResult TryStartScreenSaver()
    {
        if (!IsScreenSaverAvailable)
        {
            return new SystemPowerExecutionResult(false);
        }

        var succeeded = SendNotifyMessage(HwndBroadcast, WmSysCommand, new nint(ScScreenSave), nint.Zero);
        return succeeded
            ? SystemPowerExecutionResult.Success
            : new SystemPowerExecutionResult(false, Marshal.GetLastWin32Error());
    }

    public bool DismissBlackoutIfActive()
    {
        // Remote pointer movement is a hot path. Avoid synchronously entering the
        // WPF dispatcher unless an overlay actually exists.
        if (Volatile.Read(ref _blackoutActive) == 0)
        {
            return false;
        }

        if (_dispatcher.CheckAccess())
        {
            return DismissBlackoutCore("remote_input");
        }

        return _dispatcher.Invoke(() => DismissBlackoutCore("remote_input"));
    }

    public void Dispose()
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            _ = DismissBlackoutCore("host_shutdown");
        }
        else
        {
            _dispatcher.Invoke(() => DismissBlackoutCore("host_shutdown"));
        }
    }

    private SystemPowerExecutionResult ShowBlackoutCore()
    {
        if (_blackoutWindows.Count > 0)
        {
            return SystemPowerExecutionResult.Success;
        }

        var monitors = GetMonitorRects();
        if (monitors.Count == 0)
        {
            return new SystemPowerExecutionResult(false, Marshal.GetLastWin32Error());
        }

        _inputArmedAt = DateTimeOffset.UtcNow.AddMilliseconds(350);
        Volatile.Write(ref _blackoutActive, 1);
        foreach (var monitor in monitors)
        {
            var window = CreateBlackoutWindow();
            _blackoutWindows.Add(window);
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == nint.Zero || !SetWindowPos(
                    handle,
                    HwndTopmost,
                    monitor.Left,
                    monitor.Top,
                    monitor.Width,
                    monitor.Height,
                    SwpNoActivate | SwpShowWindow))
            {
                var error = Marshal.GetLastWin32Error();
                CloseBlackoutWindows();
                return new SystemPowerExecutionResult(false, error);
            }
        }

        _blackoutWindows[0].Activate();
        _blackoutWindows[0].Focus();
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "blackout_display",
            Outcome: "shown",
            Detail: $"monitors={_blackoutWindows.Count}"));
        return SystemPowerExecutionResult.Success;
    }

    private Window CreateBlackoutWindow()
    {
        var window = new Window
        {
            AllowsTransparency = false,
            Background = WpfBrushes.Black,
            Cursor = WpfCursors.None,
            Left = 0,
            Top = 0,
            Width = 1,
            Height = 1,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None
        };
        window.PreviewKeyDown += (_, _) => DismissFromLocalInput("keyboard");
        window.PreviewMouseDown += (_, _) => DismissFromLocalInput("pointer");
        window.PreviewMouseWheel += (_, _) => DismissFromLocalInput("pointer");
        window.PreviewStylusDown += (_, _) => DismissFromLocalInput("stylus");
        window.PreviewTouchDown += (_, _) => DismissFromLocalInput("touch");
        window.PreviewMouseMove += (_, _) => DismissFromLocalInput("pointer");
        return window;
    }

    private void DismissFromLocalInput(string source)
    {
        if (DateTimeOffset.UtcNow < _inputArmedAt)
        {
            return;
        }

        _ = DismissBlackoutCore(source);
    }

    private bool DismissBlackoutCore(string source)
    {
        if (_blackoutWindows.Count == 0)
        {
            return false;
        }

        CloseBlackoutWindows();

        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "blackout_display",
            Outcome: "dismissed",
            Detail: source));
        return true;
    }

    private void CloseBlackoutWindows()
    {
        var windows = _blackoutWindows.ToArray();
        _blackoutWindows.Clear();
        Volatile.Write(ref _blackoutActive, 0);
        foreach (var window in windows)
        {
            window.Close();
        }
    }

    private static string? ReadConfiguredScreenSaverPath()
    {
        const string valueName = "SCRNSAVE.EXE";
        using var policyDesktop = Registry.CurrentUser.OpenSubKey(
            @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
            writable: false);
        if (policyDesktop?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string policyPath &&
            !string.IsNullOrWhiteSpace(policyPath))
        {
            return policyPath;
        }

        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
        return desktop?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static IReadOnlyList<MonitorRect> GetMonitorRects()
    {
        var monitors = new List<MonitorRect>();
        _ = EnumDisplayMonitors(nint.Zero, nint.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                monitors.Add(new MonitorRect(
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top));
            }

            return true;
        }, nint.Zero);
        return monitors;
    }

    private readonly record struct MonitorRect(int Left, int Top, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint deviceContext, nint rect, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clipRect, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint update);
}
