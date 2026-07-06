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
        if (windowHandle == IntPtr.Zero || IsWindowFullscreen(windowHandle))
        {
            return;
        }

        Thread.Sleep(150);
        if (GetForegroundWindow() != windowHandle)
        {
            TryActivateWindow(windowHandle);
            Thread.Sleep(100);
        }

        if (GetForegroundWindow() == windowHandle && !IsWindowFullscreen(windowHandle))
        {
            PressVirtualKey(VirtualKeyF11);
        }
    }

    private static bool IsWindowFullscreen(IntPtr windowHandle)
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

    private static void PressVirtualKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
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
        return SetForegroundWindow(windowHandle) || GetForegroundWindow() == windowHandle;
    }

    private const int ShowWindowMaximize = 3;

    private const int ShowWindowRestore = 9;

    private const int SetWindowPosNoSize = 0x0001;

    private const int SetWindowPosNoMove = 0x0002;

    private const int SetWindowPosShowWindow = 0x0040;

    private const uint MonitorDefaultToNearest = 0x00000002;

    private const uint KeyEventKeyUp = 0x0002;

    private const byte VirtualKeyF11 = 0x7A;

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
    private static extern IntPtr GetForegroundWindow();

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

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
