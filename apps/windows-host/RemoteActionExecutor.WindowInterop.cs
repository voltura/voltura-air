using System.Runtime.InteropServices;

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
        if (windowHandle == IntPtr.Zero || IsIconic(windowHandle) || IsZoomed(windowHandle))
        {
            return false;
        }

        return IsWindowCoveringMonitor(windowHandle);
    }

    private static bool IsWindowHandleAvailable(IntPtr windowHandle)
    {
        return windowHandle != IntPtr.Zero && IsWindow(windowHandle);
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

    private static bool TryActivateWindow(IntPtr windowHandle, bool maximize = false)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (maximize)
        {
            ShowWindow(windowHandle, ShowWindowMaximize);
        }
        else if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, ShowWindowRestore);
        }
        else
        {
            TryBringWindowForwardPreservingState(windowHandle);
            return true;
        }

        return TryBringWindowForwardPreservingState(windowHandle);
    }

    private static bool TryBringWindowForwardPreservingState(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        BringWindowToTop(windowHandle);
        SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
        SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
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
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

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
