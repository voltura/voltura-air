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

        TryRequestBrowserFullscreen(windowHandle);
    }

    private static void TryRequestBrowserFullscreen(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || IsBrowserFullscreen(windowHandle))
        {
            return;
        }

        TryFocusWindowForKeyboardInput(windowHandle);
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
        if (windowHandle == IntPtr.Zero || IsIconic(windowHandle) || !IsWindowCoveringMonitor(windowHandle))
        {
            return false;
        }

        return IsBorderlessBrowserWindow(windowHandle);
    }

    private static bool IsBorderlessBrowserWindow(IntPtr windowHandle)
    {
        var style = GetWindowLongPtr(windowHandle, GetWindowLongStyle).ToInt64();
        if (style == 0)
        {
            return false;
        }

        var hasCaption = (style & WindowStyleCaption) == WindowStyleCaption;
        var hasThickFrame = (style & WindowStyleThickFrame) == WindowStyleThickFrame;
        return !hasCaption && !hasThickFrame;
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

        return TryFocusWindowForKeyboardInput(windowHandle);
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

    private static bool TryFocusWindowForKeyboardInput(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = GetWindowThreadProcessId(windowHandle, out _);
        var foregroundWindow = GetForegroundWindow();
        var foregroundThreadId = foregroundWindow == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foregroundWindow, out _);

        var attachedToTarget = targetThreadId != 0 && targetThreadId != currentThreadId && AttachThreadInput(currentThreadId, targetThreadId, true);
        var attachedToForeground = foregroundThreadId != 0 && foregroundThreadId != currentThreadId && foregroundThreadId != targetThreadId && AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            BringWindowToTop(windowHandle);
            SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            SetActiveWindow(windowHandle);
            SetFocus(windowHandle);
            SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        return IsForegroundWindowOrRoot(windowHandle);
    }

    private static bool IsForegroundWindowOrRoot(IntPtr windowHandle)
    {
        var foregroundWindow = GetForegroundWindow();
        return foregroundWindow == windowHandle || GetAncestor(foregroundWindow, GetAncestorRoot) == windowHandle;
    }

    private static void SendBrowserFullscreenShortcut()
    {
        try
        {
            using var inputInjector = new SendInputInjector();
            inputInjector.SpecialKey("F" + "11", []);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private const int BrowserFocusSettleMilliseconds = 250;

    private const int BrowserFullscreenSettleMilliseconds = 850;

    private const int ShowWindowMaximize = 3;

    private const int ShowWindowRestore = 9;

    private const int SetWindowPosNoSize = 0x0001;

    private const int SetWindowPosNoMove = 0x0002;

    private const int GetWindowLongStyle = -16;

    private const long WindowStyleCaption = 0x00C00000L;

    private const long WindowStyleThickFrame = 0x00040000L;

    private const uint MonitorDefaultToNearest = 0x00000002;

    private const uint GetAncestorRoot = 2;

    private static readonly nint TopMostWindow = -1;

    private static readonly nint NoTopMostWindow = -2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
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
        public Win32Rect Monitor;
        public Win32Rect WorkArea;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out Win32Rect lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    private static partial nint SetActiveWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hWnd, uint gaFlags);
}
