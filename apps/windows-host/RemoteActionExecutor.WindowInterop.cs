using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VolturaAir.Host;

public sealed partial class RemoteActionExecutor
{
    private static bool TryActivateBrowserWindow(IntPtr windowHandle, bool ensureFullscreen = false)
    {
        var activated = TryActivateWindow(windowHandle);
        if (activated && ensureFullscreen)
        {
            EnsureBrowserFullscreen(windowHandle);
        }

        return activated;
    }

    private static void EnsureBrowserFullscreen(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        TryActivateWindow(windowHandle);
        Thread.Sleep(BrowserFocusSettleMilliseconds);
        if (IsBrowserFullscreen(windowHandle))
        {
            return;
        }

        SendBrowserFullscreenShortcut();
        Thread.Sleep(BrowserFullscreenSettleMilliseconds);
    }

    private static bool IsBrowserFullscreen(IntPtr windowHandle)
    {
        return IsWindowCoveringMonitor(windowHandle) && !HasVisibleBrowserChrome(windowHandle);
    }

    private static bool IsWindowCoveringMonitor(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var windowRect))
        {
            return false;
        }

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance
            && Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance
            && Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance
            && Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }

    private static bool HasVisibleBrowserChrome(IntPtr windowHandle)
    {
        try
        {
            var browserWindow = AutomationElement.FromHandle(windowHandle);
            if (browserWindow is null)
            {
                return true;
            }

            return HasVisibleControlType(browserWindow, ControlType.TabItem) || HasVisibleControlType(browserWindow, ControlType.Edit);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return true;
        }
    }

    private static bool HasVisibleControlType(AutomationElement root, ControlType controlType)
    {
        var elements = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));

        foreach (AutomationElement element in elements)
        {
            if (!element.Current.IsOffscreen)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryActivateWindow(IntPtr windowHandle, bool maximize = false)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        ShowWindow(windowHandle, maximize ? ShowWindowMaximize : ShowWindowRestore);
        BringWindowToTop(windowHandle);
        SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
        SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
        SetForegroundWindow(windowHandle);
        return true;
    }

    private static void SendBrowserFullscreenShortcut()
    {
        try
        {
            using var inputInjector = new SendInputInjector();
            inputInjector.SpecialKey("F" + "11", Array.Empty<string>());
        }
        catch (InvalidOperationException)
        {
        }
    }

    private const int BrowserFocusSettleMilliseconds = 500;

    private const int BrowserFullscreenSettleMilliseconds = 800;

    private const int ShowWindowMaximize = 3;

    private const int ShowWindowRestore = 9;

    private const int SetWindowPosNoSize = 0x0001;

    private const int SetWindowPosNoMove = 0x0002;

    private const int SetWindowPosShowWindow = 0x0040;

    private const uint MonitorDefaultToNearest = 0x00000002;

    private static readonly nint TopMostWindow = -1;

    private static readonly nint NoTopMostWindow = -2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
