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
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;

    public static void ConstrainAndCenterOnFirstLoad(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Loaded += OnLoaded;

        void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            window.Loaded -= OnLoaded;
            Apply(window, SystemParameters.WorkArea);
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
            if (message == WindowMessageDisplayChange &&
                pendingPlacement?.Status is not DispatcherOperationStatus.Pending and not DispatcherOperationStatus.Executing)
            {
                pendingPlacement = window.Dispatcher.InvokeAsync(
                    () => EnsureVisible(window, windowHandle),
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
            EnsureVisible(window, windowHandle);
        }
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

    private static void Apply(Window window, Rect workArea)
    {
        var bounds = CalculateBounds(workArea, new WpfSize(window.Width, window.Height));
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }

    private static void EnsureVisible(Window window, nint windowHandle)
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
