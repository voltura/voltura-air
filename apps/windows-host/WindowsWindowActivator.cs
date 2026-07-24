using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal interface IWindowsWindowActivator
{
    bool TryActivateWindow(IntPtr windowHandle, bool maximize = false);

    bool TryBringWindowForwardPreservingState(IntPtr windowHandle);

    bool IsWindowHandleAvailable(IntPtr windowHandle);

    bool IsBrowserFullscreen(IntPtr windowHandle);

    Task EnsureBrowserFullscreenAsync(IntPtr windowHandle, CancellationToken cancellationToken);
}

internal sealed class WindowsWindowActivator : IWindowsWindowActivator
{
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

    public async Task EnsureBrowserFullscreenAsync(IntPtr windowHandle, CancellationToken cancellationToken)
    {
        if (windowHandle == IntPtr.Zero || IsBrowserFullscreen(windowHandle))
        {
            return;
        }

        TryFocusWindowForKeyboardInput(windowHandle);
        await Task.Delay(BrowserFocusSettleMilliseconds, cancellationToken);
        if (IsBrowserFullscreen(windowHandle))
        {
            return;
        }

        SendBrowserFullscreenShortcut();
        await Task.Delay(BrowserFullscreenSettleMilliseconds, cancellationToken);
    }

    public bool IsBrowserFullscreen(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || WindowNativeMethods.IsIconic(windowHandle) || !IsWindowCoveringMonitor(windowHandle))
        {
            return false;
        }

        var style = WindowNativeMethods.GetWindowLongPtr(windowHandle, GetWindowLongStyle).ToInt64();
        if (style == 0)
        {
            return false;
        }

        var hasCaption = (style & WindowStyleCaption) == WindowStyleCaption;
        var hasThickFrame = (style & WindowStyleThickFrame) == WindowStyleThickFrame;
        return !hasCaption && !hasThickFrame;
    }

    public bool IsWindowHandleAvailable(IntPtr windowHandle) =>
        windowHandle != IntPtr.Zero && WindowNativeMethods.IsWindow(windowHandle);

    public bool TryActivateWindow(IntPtr windowHandle, bool maximize = false)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (maximize)
        {
            WindowNativeMethods.ShowWindow(windowHandle, ShowWindowMaximize);
        }
        else if (WindowNativeMethods.IsIconic(windowHandle))
        {
            WindowNativeMethods.ShowWindow(windowHandle, ShowWindowRestore);
        }

        return TryFocusWindowForKeyboardInput(windowHandle);
    }

    public bool TryBringWindowForwardPreservingState(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        WindowNativeMethods.BringWindowToTop(windowHandle);
        WindowNativeMethods.SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
        WindowNativeMethods.SetForegroundWindow(windowHandle);
        WindowNativeMethods.SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
        return true;
    }

    private static bool IsWindowCoveringMonitor(IntPtr windowHandle)
    {
        if (!WindowNativeMethods.GetWindowRect(windowHandle, out var windowRect))
        {
            return false;
        }

        var monitorHandle = WindowNativeMethods.MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!WindowNativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance &&
               Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance &&
               Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance &&
               Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }

    private static bool TryFocusWindowForKeyboardInput(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var currentThreadId = WindowNativeMethods.GetCurrentThreadId();
        var targetThreadId = WindowNativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        var foregroundWindow = WindowNativeMethods.GetForegroundWindow();
        var foregroundThreadId = foregroundWindow == IntPtr.Zero
            ? 0
            : WindowNativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);

        var attachedToTarget = targetThreadId != 0 && targetThreadId != currentThreadId &&
            WindowNativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
        var attachedToForeground = foregroundThreadId != 0 && foregroundThreadId != currentThreadId &&
            foregroundThreadId != targetThreadId &&
            WindowNativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            WindowNativeMethods.BringWindowToTop(windowHandle);
            WindowNativeMethods.SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            WindowNativeMethods.SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            WindowNativeMethods.SetActiveWindow(windowHandle);
            WindowNativeMethods.SetFocus(windowHandle);
            WindowNativeMethods.SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedToForeground)
            {
                WindowNativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                WindowNativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        var currentForeground = WindowNativeMethods.GetForegroundWindow();
        return currentForeground == windowHandle ||
            WindowNativeMethods.GetAncestor(currentForeground, GetAncestorRoot) == windowHandle;
    }

    private static void SendBrowserFullscreenShortcut()
    {
        try
        {
            using var inputInjector = new SendInputInjector();
            inputInjector.SpecialKey("F11", []);
        }
        catch (InvalidOperationException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Win32Rect Monitor;
        public Win32Rect WorkArea;
        public uint Flags;
    }
}

internal static partial class WindowNativeMethods
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint windowHandle, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BringWindowToTop(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint windowHandle, out WindowsWindowActivator.Win32Rect rectangle);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint windowHandle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitorHandle, ref WindowsWindowActivator.MonitorInfo monitorInfo);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint attachId, uint attachToId, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    internal static partial nint SetActiveWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint windowHandle, uint flags);
}
