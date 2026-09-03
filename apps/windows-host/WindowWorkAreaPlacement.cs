using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host;

internal static partial class WindowWorkAreaPlacement
{
    private const int WindowMessageDisplayChange = 0x007E;
    private const int WindowMessageDpiChanged = 0x02E0;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const double DipsPerInch = 96;
    private const double SizeComparisonTolerance = 1;
    private static readonly ConditionalWeakTable<Window, PlacementState> PlacementStates = [];

    public static void ConstrainAndCenterOnFirstLoad(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var state = PlacementStates.GetValue(
            window,
            static currentWindow => new PlacementState(new WpfSize(currentWindow.Width, currentWindow.Height)));
        window.Loaded += OnLoaded;

        void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            window.Loaded -= OnLoaded;
            var bounds = Apply(window, SystemParameters.WorkArea, state.PreferredSize);
            state.LastAutomaticallyConstrainedSize = SizesMatch(bounds.Size, state.PreferredSize)
                ? null
                : bounds.Size;
        }
    }

    internal static Rect CalculateBounds(Rect workArea, WpfSize requestedSize)
    {
        var width = Math.Min(requestedSize.Width, workArea.Width);
        var height = Math.Min(requestedSize.Height, workArea.Height);
        var left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        return new Rect(left, top, width, height);
    }

    public static void KeepVisibleAfterDisplayChanges(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var state = PlacementStates.GetValue(
            window,
            static currentWindow => new PlacementState(new WpfSize(currentWindow.Width, currentWindow.Height)));
        HwndSource? source = null;
        DispatcherOperation? pendingPlacement = null;

        window.SourceInitialized += OnSourceInitialized;
        window.Closed += OnClosed;

        void OnSourceInitialized(object? sender, EventArgs eventArgs)
        {
            window.SourceInitialized -= OnSourceInitialized;
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(FilterWindowMessage);
        }

        nint FilterWindowMessage(nint windowHandle, int message, nint wordParameter, nint longParameter, ref bool handled)
        {
            if ((message == WindowMessageDisplayChange || message == WindowMessageDpiChanged) &&
                pendingPlacement?.Status is not DispatcherOperationStatus.Pending and not DispatcherOperationStatus.Executing)
            {
                pendingPlacement = window.Dispatcher.InvokeAsync(
                    () => EnsureVisible(window, windowHandle, state),
                    DispatcherPriority.ContextIdle);
            }

            return 0;
        }

        void OnClosed(object? sender, EventArgs eventArgs)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.Closed -= OnClosed;
            source?.RemoveHook(FilterWindowMessage);
            if (pendingPlacement?.Status == DispatcherOperationStatus.Pending)
            {
                pendingPlacement.Abort();
            }
        }
    }

    public static void EnsureVisibleOnCurrentMonitor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle != 0)
        {
            var state = PlacementStates.GetValue(
                window,
                static currentWindow => new PlacementState(new WpfSize(currentWindow.Width, currentWindow.Height)));
            EnsureVisible(window, windowHandle, state);
        }
    }

    internal static WpfSize CalculateSizeAfterWorkAreaChange(
        WpfSize preferredSize,
        WpfSize? lastAutomaticallyConstrainedSize,
        WpfSize currentSize,
        WpfSize workAreaSize)
    {
        if (!IsManagedSize(preferredSize, lastAutomaticallyConstrainedSize, currentSize))
        {
            return currentSize;
        }

        return new WpfSize(
            Math.Min(preferredSize.Width, workAreaSize.Width),
            Math.Min(preferredSize.Height, workAreaSize.Height));
    }

    internal static WpfPoint CalculateVisibleTopLeft(Rect windowBounds, Rect workArea)
    {
        var left = windowBounds.Width <= workArea.Width
            ? Math.Clamp(windowBounds.Left, workArea.Left, workArea.Right - windowBounds.Width)
            : workArea.Left;
        var top = windowBounds.Height <= workArea.Height
            ? Math.Clamp(windowBounds.Top, workArea.Top, workArea.Bottom - windowBounds.Height)
            : workArea.Top;
        return new WpfPoint(left, top);
    }

    private static Rect Apply(Window window, Rect workArea, WpfSize requestedSize)
    {
        var bounds = CalculateBounds(workArea, requestedSize);
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        return bounds;
    }

    private static void EnsureVisible(Window window, nint windowHandle, PlacementState state)
    {
        if (window.WindowState != WindowState.Normal ||
            !GetWindowRect(windowHandle, out var windowRect))
        {
            return;
        }

        var monitor = MonitorFromRect(in windowRect, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        UpdateManagedSizeForWorkArea(window, windowHandle, monitorInfo.WorkArea, state);
        if (!GetWindowRect(windowHandle, out windowRect))
        {
            return;
        }

        var windowBounds = windowRect.ToRect();
        var position = CalculateVisibleTopLeft(windowBounds, monitorInfo.WorkArea.ToRect());
        if (position.X == windowBounds.Left && position.Y == windowBounds.Top)
        {
            return;
        }

        _ = SetWindowPos(
            windowHandle,
            0,
            (int)position.X,
            (int)position.Y,
            0,
            0,
            SetWindowPositionNoSize | SetWindowPositionNoZOrder | SetWindowPositionNoActivate);
    }

    private static void UpdateManagedSizeForWorkArea(
        Window window,
        nint windowHandle,
        NativeRect workArea,
        PlacementState state)
    {
        var dpi = GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            return;
        }

        var currentSize = new WpfSize(window.Width, window.Height);
        var workAreaSize = new WpfSize(
            (workArea.Right - workArea.Left) * DipsPerInch / dpi,
            (workArea.Bottom - workArea.Top) * DipsPerInch / dpi);
        if (!IsManagedSize(state.PreferredSize, state.LastAutomaticallyConstrainedSize, currentSize))
        {
            state.LastAutomaticallyConstrainedSize = null;
            return;
        }

        var recoveredSize = CalculateSizeAfterWorkAreaChange(
            state.PreferredSize,
            state.LastAutomaticallyConstrainedSize,
            currentSize,
            workAreaSize);

        if (!SizesMatch(recoveredSize, currentSize))
        {
            window.Width = recoveredSize.Width;
            window.Height = recoveredSize.Height;
        }

        state.LastAutomaticallyConstrainedSize = SizesMatch(recoveredSize, state.PreferredSize)
            ? null
            : recoveredSize;
    }

    private static bool IsManagedSize(
        WpfSize preferredSize,
        WpfSize? lastAutomaticallyConstrainedSize,
        WpfSize currentSize) =>
        lastAutomaticallyConstrainedSize is WpfSize constrainedSize
            ? SizesMatch(currentSize, constrainedSize)
            : SizesMatch(currentSize, preferredSize);

    private static bool SizesMatch(WpfSize left, WpfSize right) =>
        Math.Abs(left.Width - right.Width) <= SizeComparisonTolerance &&
        Math.Abs(left.Height - right.Height) <= SizeComparisonTolerance;

    private sealed class PlacementState(WpfSize preferredSize)
    {
        public WpfSize PreferredSize { get; } = preferredSize;

        public WpfSize? LastAutomaticallyConstrainedSize { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromRect(in NativeRect rectangle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
