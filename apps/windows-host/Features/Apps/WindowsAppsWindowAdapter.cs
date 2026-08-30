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
    private readonly Lock _identityGate = new();
    private readonly Dictionary<nint, nint> _identityTokens = [];
    private readonly string _identityPropertyName = $"VolturaAir.Apps.Identity.{Guid.NewGuid():N}";
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private readonly int _hostProcessId = Environment.ProcessId;
    private IAppsVirtualDesktopManager? _desktopManager;
    private long _nextIdentityToken;
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

    private bool TryGetCurrent(
        AppsWindowSnapshot expected,
        bool includeVolturaAir,
        out AppsWindowSnapshot current)
    {
        if (!EnsureVirtualDesktopManager())
        {
            current = null!;
            return false;
        }

        if (!TryCreateSnapshot(
            expected.Handle,
            WindowNativeMethods.GetForegroundWindow(),
            includeVolturaAir,
            out current))
        {
            return false;
        }

        return expected.IdentityToken == nint.Zero
            ? HasSameProcessIdentity(expected, current)
            : HasSameIdentity(expected, current);
    }

    public AppsWindowActionResult Activate(AppsWindowSnapshot window, bool includeVolturaAir)
    {
        if (!TryGetCurrent(window, includeVolturaAir, out _))
        {
            return new(false, "stale-window", "The application window is no longer available.");
        }

        bool maximize = window.IdentityToken != nint.Zero && SupportsMaximize(window.Handle) &&
            !AppsWindowNativeMethods.IsZoomed(window.Handle) &&
            !IsApplicationFullscreen(window.Handle);
        return _windowActivator.TryActivateWindow(window.Handle, maximize)
            ? new(true, "accepted", "Application activated.")
            : new(false, "activation-rejected", "Windows did not allow the application to take focus.");
    }

    public AppsWindowActionResult Close(AppsWindowSnapshot window, bool includeVolturaAir)
    {
        if (window.IdentityToken == nint.Zero)
        {
            return new(
                false,
                "unavailable",
                "Windows security does not permit Voltura Air to verify this window for closing.");
        }

        if (!TryGetCurrent(window, includeVolturaAir, out _))
        {
            return new(false, "stale-window", "The application window is no longer available.");
        }

        return AppsWindowNativeMethods.PostMessage(window.Handle, WindowMessageClose, nint.Zero, nint.Zero)
            ? new(true, "close-requested", "Close requested.")
            : new(false, "unavailable", "Windows could not request that the application close.");
    }

    public AppsPreviewCaptureResult CapturePreview(
        AppsWindowSnapshot window,
        bool includeVolturaAir,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (window.IdentityToken == nint.Zero ||
            !TryGetCurrent(window, includeVolturaAir, out _) ||
            WindowNativeMethods.IsIconic(window.Handle) ||
            !TryGetCaptureBounds(window.Handle, out var bounds) ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            (long)bounds.Width * bounds.Height > AppsProtocol.MaximumPreviewPixels)
        {
            return new(false, null, 0, 0);
        }

        return AppsWindowPreviewCapture.Capture(window.Handle, bounds, cancellationToken);
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
        uint threadId = 0;
        if (isWindow)
        {
            threadId = WindowNativeMethods.GetWindowThreadProcessId(windowHandle, out processId);
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

        nint identityToken = GetIdentityToken(windowHandle);
        bool identityVerified = identityToken != nint.Zero;

        snapshot = new AppsWindowSnapshot(
            windowHandle,
            processId,
            threadId,
            identityToken,
            ProtocolStringLimits.Limit(title.Trim(), AppsProtocol.MaximumWindowTitleLength),
            ProtocolStringLimits.Limit(applicationName.Trim(), AppsProtocol.MaximumApplicationNameLength),
            WindowsWindowActivator.IsRequestedForegroundWindow(windowHandle, foreground),
            minimized,
            identityVerified && SupportsMaximize(windowHandle),
            identityVerified && !minimized,
            isVolturaAir);
        return true;
    }

    internal static bool HasSameIdentity(AppsWindowSnapshot expected, AppsWindowSnapshot current) =>
        expected.IdentityToken != nint.Zero &&
        expected.Handle == current.Handle &&
        expected.ProcessId == current.ProcessId &&
        expected.ThreadId == current.ThreadId &&
        expected.IdentityToken == current.IdentityToken &&
        expected.IsVolturaAir == current.IsVolturaAir &&
        string.Equals(expected.ApplicationName, current.ApplicationName, StringComparison.Ordinal);

    private static bool HasSameProcessIdentity(AppsWindowSnapshot expected, AppsWindowSnapshot current) =>
        expected.Handle == current.Handle &&
        expected.ProcessId == current.ProcessId &&
        expected.ThreadId == current.ThreadId &&
        expected.IsVolturaAir == current.IsVolturaAir &&
        string.Equals(expected.ApplicationName, current.ApplicationName, StringComparison.Ordinal);

    private nint GetIdentityToken(nint windowHandle)
    {
        lock (_identityGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return nint.Zero;
            }

            nint current = AppsWindowNativeMethods.GetProp(windowHandle, _identityPropertyName);
            if (current == nint.Zero)
            {
                current = new nint(Interlocked.Increment(ref _nextIdentityToken));
                if (!AppsWindowNativeMethods.SetProp(windowHandle, _identityPropertyName, current) ||
                    AppsWindowNativeMethods.GetProp(windowHandle, _identityPropertyName) != current)
                {
                    return nint.Zero;
                }
            }

            _identityTokens[windowHandle] = current;
            return current;
        }
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
        lock (_identityGate)
        {
            foreach (var identity in _identityTokens)
            {
                if (AppsWindowNativeMethods.GetProp(identity.Key, _identityPropertyName) == identity.Value)
                {
                    _ = AppsWindowNativeMethods.RemoveProp(identity.Key, _identityPropertyName);
                }
            }
            _identityTokens.Clear();
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
