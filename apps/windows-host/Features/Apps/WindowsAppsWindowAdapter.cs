using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VolturaAir.Host.Features.Apps;

internal sealed class WindowsAppsWindowAdapter(
    IWindowsWindowActivator? windowActivator = null) : IAppsWindowAdapter
{
    private const int ExtendedStyleIndex = -20;
    private const int StyleIndex = -16;
    private const long ExtendedStyleAppWindow = 0x00040000L;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const long WindowStyleMaximizeBox = 0x00010000L;
    private const uint AncestorRootOwner = 3;
    private const uint DwmWindowAttributeCloaked = 14;
    private const uint DwmWindowAttributeExtendedFrameBounds = 9;
    private const uint MonitorDefaultToNearest = 2;
    private const uint WindowMessageClose = 0x0010;
    private readonly IWindowsWindowActivator _windowActivator = windowActivator ?? new WindowsWindowActivator();
    private readonly Lock _desktopGate = new();
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private readonly int _hostProcessId = Environment.ProcessId;
    private IAppsVirtualDesktopManager? _desktopManager;
    private int _disposed;

    public AppsWindowDiscoveryResult Discover(bool includeVolturaAir)
    {
        if (!EnsureVirtualDesktopManager())
        {
            return new(false, "unavailable", "Windows could not determine the current desktop.", []);
        }

        nint foreground = WindowNativeMethods.GetForegroundWindow();
        var windows = new List<AppsWindowSnapshot>(AppsProtocol.MaximumWindows);
        _ = WindowNativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (windows.Count >= AppsProtocol.MaximumWindows)
            {
                return false;
            }

            if (TryCreateSnapshot(
                    windowHandle,
                    foreground,
                    includeVolturaAir,
                    out var snapshot))
            {
                windows.Add(snapshot);
            }

            return true;
        }, nint.Zero);

        int activeIndex = windows.FindIndex(window => window.Active);
        if (activeIndex > 0)
        {
            var active = windows[activeIndex];
            windows.RemoveAt(activeIndex);
            windows.Insert(0, active);
        }

        return new(true, "accepted", "Open applications loaded.", windows);
    }

    public bool IsUsable(nint windowHandle, bool includeVolturaAir)
    {
        if (!EnsureVirtualDesktopManager())
        {
            return false;
        }

        return TryCreateSnapshot(
            windowHandle,
            WindowNativeMethods.GetForegroundWindow(),
            includeVolturaAir,
            out _);
    }

    public AppsWindowActionResult Activate(nint windowHandle, bool includeVolturaAir)
    {
        if (!IsUsable(windowHandle, includeVolturaAir))
        {
            return new(false, "stale-window", "The application window is no longer available.");
        }

        bool maximize = SupportsMaximize(windowHandle) &&
            !AppsWindowNativeMethods.IsZoomed(windowHandle) &&
            !IsApplicationFullscreen(windowHandle);
        return _windowActivator.TryActivateWindow(windowHandle, maximize)
            ? new(true, "accepted", "Application activated.")
            : new(false, "activation-rejected", "Windows did not allow the application to take focus.");
    }

    public AppsWindowActionResult Close(nint windowHandle, bool includeVolturaAir)
    {
        if (!IsUsable(windowHandle, includeVolturaAir))
        {
            return new(false, "stale-window", "The application window is no longer available.");
        }

        return AppsWindowNativeMethods.PostMessage(windowHandle, WindowMessageClose, nint.Zero, nint.Zero)
            ? new(true, "close-requested", "Close requested.")
            : new(false, "unavailable", "Windows could not request that the application close.");
    }

    public AppsPreviewCaptureResult CapturePreview(
        nint windowHandle,
        bool includeVolturaAir,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsUsable(windowHandle, includeVolturaAir) || WindowNativeMethods.IsIconic(windowHandle) ||
            !TryGetCaptureBounds(windowHandle, out var bounds) ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            (long)bounds.Width * bounds.Height > AppsProtocol.MaximumPreviewPixels)
        {
            return new(false, null, 0, 0);
        }

        return AppsWindowPreviewCapture.Capture(windowHandle, bounds, cancellationToken);
    }

    internal static bool ShouldIncludeCandidate(
        bool isWindow,
        bool isVisible,
        bool isToolWindow,
        bool isAppWindow,
        bool isRootOwnerPopup,
        bool isCloaked,
        bool isCurrentSession,
        bool isCurrentDesktop,
        bool isVolturaAir,
        bool includeVolturaAir,
        string title,
        string applicationName) =>
        isWindow &&
        isVisible &&
        !isToolWindow &&
        (isAppWindow || isRootOwnerPopup) &&
        !isCloaked &&
        isCurrentSession &&
        isCurrentDesktop &&
        (!isVolturaAir || includeVolturaAir) &&
        !string.IsNullOrWhiteSpace(title) &&
        !string.IsNullOrWhiteSpace(applicationName);

    private bool TryCreateSnapshot(
        nint windowHandle,
        nint foreground,
        bool includeVolturaAir,
        out AppsWindowSnapshot snapshot)
    {
        snapshot = null!;
        bool isWindow = WindowNativeMethods.IsWindow(windowHandle);
        bool visible = isWindow && WindowNativeMethods.IsWindowVisible(windowHandle);
        bool minimized = isWindow && WindowNativeMethods.IsIconic(windowHandle);
        long extendedStyle = isWindow
            ? WindowNativeMethods.GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64()
            : 0;
        bool appWindow = (extendedStyle & ExtendedStyleAppWindow) != 0;
        bool toolWindow = (extendedStyle & ExtendedStyleToolWindow) != 0;
        bool rootOwnerPopup = isWindow && IsRootOwnerPopup(windowHandle);
        bool cloaked = isWindow && IsCloaked(windowHandle);
        uint processId = 0;
        if (isWindow)
        {
            _ = WindowNativeMethods.GetWindowThreadProcessId(windowHandle, out processId);
        }
        bool currentSession = false;
        bool isVolturaAir = false;
        string applicationName = string.Empty;
        if (processId != 0)
        {
            try
            {
                using var process = Process.GetProcessById(checked((int)processId));
                currentSession = process.SessionId == _sessionId;
                isVolturaAir = process.Id == _hostProcessId;
                applicationName = GetApplicationName(process);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
            {
            }
        }

        bool currentDesktop = isWindow && IsWindowOnCurrentVirtualDesktop(windowHandle);
        string title = isWindow ? ReadWindowTitle(windowHandle) : string.Empty;
        if (!ShouldIncludeCandidate(
                isWindow,
                visible,
                toolWindow,
                appWindow,
                rootOwnerPopup,
                cloaked,
                currentSession,
                currentDesktop,
                isVolturaAir,
                includeVolturaAir,
                title,
                applicationName))
        {
            return false;
        }

        snapshot = new AppsWindowSnapshot(
            windowHandle,
            ProtocolStringLimits.Limit(title.Trim(), AppsProtocol.MaximumWindowTitleLength),
            ProtocolStringLimits.Limit(applicationName.Trim(), AppsProtocol.MaximumApplicationNameLength),
            WindowsWindowActivator.IsRequestedForegroundWindow(windowHandle, foreground),
            minimized,
            SupportsMaximize(windowHandle),
            !minimized);
        return true;
    }

    private static bool IsRootOwnerPopup(nint windowHandle)
    {
        nint rootOwner = WindowNativeMethods.GetAncestor(windowHandle, AncestorRootOwner);
        if (rootOwner == nint.Zero)
        {
            rootOwner = windowHandle;
        }

        nint popup = ResolveRootOwnerPopup(
            rootOwner,
            AppsWindowNativeMethods.GetLastActivePopup,
            WindowNativeMethods.IsWindowVisible);

        return popup == windowHandle || rootOwner == windowHandle && popup == nint.Zero;
    }

    internal static nint ResolveRootOwnerPopup(
        nint rootOwner,
        Func<nint, nint> getLastActivePopup,
        Func<nint, bool> isWindowVisible)
    {
        const int maximumPopupDepth = 32;
        nint popup = getLastActivePopup(rootOwner);
        for (int depth = 0;
             popup != nint.Zero && popup != rootOwner && !isWindowVisible(popup);
             depth++)
        {
            if (depth >= maximumPopupDepth)
            {
                return nint.Zero;
            }

            nint nextPopup = getLastActivePopup(popup);
            if (nextPopup == popup)
            {
                return nint.Zero;
            }

            popup = nextPopup;
        }

        return popup;
    }

    private static bool IsCloaked(nint windowHandle)
    {
        int cloaked = 0;
        return AppsWindowNativeMethods.DwmGetWindowAttribute(
            windowHandle,
            DwmWindowAttributeCloaked,
            ref cloaked,
            (uint)Marshal.SizeOf<int>()) == 0 && cloaked != 0;
    }

    private static bool SupportsMaximize(nint windowHandle) =>
        (WindowNativeMethods.GetWindowLongPtr(windowHandle, StyleIndex).ToInt64() & WindowStyleMaximizeBox) != 0;

    private static bool IsApplicationFullscreen(nint windowHandle)
    {
        if (!WindowNativeMethods.GetWindowRect(windowHandle, out var rectangle))
        {
            return false;
        }

        nint monitor = WindowNativeMethods.MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return false;
        }

        var info = new WindowsWindowActivator.MonitorInfo { Size = Marshal.SizeOf<WindowsWindowActivator.MonitorInfo>() };
        if (!WindowNativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(rectangle.Left - info.Monitor.Left) <= tolerance &&
            Math.Abs(rectangle.Top - info.Monitor.Top) <= tolerance &&
            Math.Abs(rectangle.Right - info.Monitor.Right) <= tolerance &&
            Math.Abs(rectangle.Bottom - info.Monitor.Bottom) <= tolerance;
    }

    private static bool TryGetCaptureBounds(nint windowHandle, out Rectangle bounds)
    {
        var rectangle = new WindowsWindowActivator.Win32Rect();
        if (AppsWindowNativeMethods.DwmGetWindowAttribute(
                windowHandle,
                DwmWindowAttributeExtendedFrameBounds,
                ref rectangle,
                (uint)Marshal.SizeOf<WindowsWindowActivator.Win32Rect>()) != 0 &&
            !WindowNativeMethods.GetWindowRect(windowHandle, out rectangle))
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        return true;
    }

    private static string GetApplicationName(Process process)
    {
        try
        {
            var version = process.MainModule?.FileVersionInfo;
            return FirstNonEmpty(version?.FileDescription, version?.ProductName, process.ProcessName);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return process.ProcessName;
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static unsafe string ReadWindowTitle(nint windowHandle)
    {
        Span<char> value = stackalloc char[AppsProtocol.MaximumWindowTitleLength + 1];
        fixed (char* pointer = value)
        {
            int length = WindowNativeMethods.GetWindowText(windowHandle, pointer, value.Length);
            return length <= 0 ? string.Empty : new string(pointer, 0, length);
        }
    }

    private static bool TryCreateVirtualDesktopManager(out IAppsVirtualDesktopManager manager)
    {
        manager = null!;
        try
        {
            Type? type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            if (type is null || Activator.CreateInstance(type) is not IAppsVirtualDesktopManager value)
            {
                return false;
            }

            manager = value;
            return true;
        }
        catch (Exception exception) when (
            exception is COMException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private bool EnsureVirtualDesktopManager()
    {
        lock (_desktopGate)
        {
            return Volatile.Read(ref _disposed) == 0 &&
                (_desktopManager is not null || TryCreateVirtualDesktopManager(out _desktopManager));
        }
    }

    private bool IsWindowOnCurrentVirtualDesktop(nint windowHandle)
    {
        lock (_desktopGate)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                (_desktopManager is null && !TryCreateVirtualDesktopManager(out _desktopManager)))
            {
                return false;
            }

            try
            {
                return _desktopManager.IsWindowOnCurrentVirtualDesktop(
                    windowHandle,
                    out bool onDesktop) >= 0 && onDesktop;
            }
            catch (Exception exception) when (exception is COMException or InvalidComObjectException)
            {
                ReleaseVirtualDesktopManager();
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_desktopGate)
        {
            ReleaseVirtualDesktopManager();
        }
    }

    private void ReleaseVirtualDesktopManager()
    {
        if (_desktopManager is not null)
        {
            try
            {
                _ = Marshal.FinalReleaseComObject(_desktopManager);
            }
            catch (InvalidComObjectException)
            {
            }
            _desktopManager = null;
        }
    }

}
