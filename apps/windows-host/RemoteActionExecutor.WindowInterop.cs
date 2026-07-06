using System.Runtime.InteropServices;
using System.Windows;
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

        TryRequestBrowserFullscreen(windowHandle);
        if (!IsBrowserFullscreen(windowHandle))
        {
            ScheduleBrowserFullscreenRetries(windowHandle);
        }
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

    private static void ScheduleBrowserFullscreenRetries(IntPtr windowHandle)
    {
        _ = Task.Run(async () =>
        {
            foreach (var delay in BrowserFullscreenRetryDelays)
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (!IsWindowHandleAvailable(windowHandle) || IsBrowserFullscreen(windowHandle))
                {
                    return;
                }

                TryRequestBrowserFullscreen(windowHandle);
                if (IsBrowserFullscreen(windowHandle))
                {
                    return;
                }
            }
        });
    }

    private static bool IsBrowserFullscreen(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || IsIconic(windowHandle) || IsZoomed(windowHandle) || !IsWindowCoveringMonitor(windowHandle))
        {
            return false;
        }

        return TryGetVisibleBrowserShell(windowHandle, out var hasVisibleBrowserShell) && !hasVisibleBrowserShell;
    }

    private static bool TryGetVisibleBrowserShell(IntPtr windowHandle, out bool hasVisibleBrowserShell)
    {
        hasVisibleBrowserShell = false;

        try
        {
            var browserWindow = AutomationElement.FromHandle(windowHandle);
            if (browserWindow is null)
            {
                return false;
            }

            var rootBounds = browserWindow.Current.BoundingRectangle;
            hasVisibleBrowserShell = HasVisibleBrowserShellElement(browserWindow, ControlType.TabItem, rootBounds)
                || HasVisibleBrowserShellElement(browserWindow, ControlType.ToolBar, rootBounds)
                || HasVisibleBrowserAddressControl(browserWindow, rootBounds);
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return false;
        }
    }

    private static bool HasVisibleBrowserShellElement(AutomationElement root, ControlType controlType, Rect rootBounds)
    {
        var elements = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));

        foreach (AutomationElement element in elements)
        {
            if (IsVisibleBrowserShellElement(element, rootBounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasVisibleBrowserAddressControl(AutomationElement root, Rect rootBounds)
    {
        var editFields = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

        foreach (AutomationElement editField in editFields)
        {
            if (!IsVisibleBrowserShellElement(editField, rootBounds) || !LooksLikeBrowserAddressControl(editField))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsVisibleBrowserShellElement(AutomationElement element, Rect rootBounds)
    {
        if (element.Current.IsOffscreen)
        {
            return false;
        }

        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width <= 1 || bounds.Height <= 1)
        {
            return false;
        }

        return bounds.Top >= rootBounds.Top - BrowserShellTopTolerance
            && bounds.Top <= rootBounds.Top + BrowserShellMaxTopOffset;
    }

    private static bool LooksLikeBrowserAddressControl(AutomationElement editField)
    {
        var name = editField.Current.Name;
        return !string.IsNullOrWhiteSpace(name)
            && (name.Contains("address", StringComparison.OrdinalIgnoreCase)
                || name.Contains("adress", StringComparison.OrdinalIgnoreCase)
                || name.Contains("url", StringComparison.OrdinalIgnoreCase)
                || name.Contains("omnibox", StringComparison.OrdinalIgnoreCase));
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
            inputInjector.SpecialKey("F" + "11", Array.Empty<string>());
        }
        catch (InvalidOperationException)
        {
        }
    }

    private const int BrowserFocusSettleMilliseconds = 250;

    private const int BrowserFullscreenSettleMilliseconds = 850;

    private const int BrowserShellTopTolerance = 8;

    private const int BrowserShellMaxTopOffset = 180;

    private const int ShowWindowMaximize = 3;

    private const int ShowWindowRestore = 9;

    private const int SetWindowPosNoSize = 0x0001;

    private const int SetWindowPosNoMove = 0x0002;

    private const uint MonitorDefaultToNearest = 0x00000002;

    private const uint GetAncestorRoot = 2;

    private static readonly TimeSpan[] BrowserFullscreenRetryDelays =
    [
        TimeSpan.FromMilliseconds(700),
        TimeSpan.FromMilliseconds(1400)
    ];

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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
}
