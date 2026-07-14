using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using WpfColor = System.Windows.Media.Color;

namespace VolturaAir.Host;

public sealed class PointerHighlightService : IPointerHighlightService, IDisposable
{
    private const int VisibleDurationMilliseconds = 700;
    private const double GlowPaddingDips = 32;
    private const double PointerHotspotXDips = 18 + GlowPaddingDips;
    private const double PointerHotspotYDips = 14 + GlowPaddingDips;
    private static readonly long VisibleDurationTicks = (long)(Stopwatch.Frequency * (VisibleDurationMilliseconds / 1000d));
    private static readonly uint[] SystemCursorIds =
    [
        32512, // Normal select
        32513, // Text select
        32514, // Busy
        32515, // Precision select
        32516, // Alternate select
        32642, // Diagonal resize 1
        32643, // Diagonal resize 2
        32644, // Horizontal resize
        32645, // Vertical resize
        32646, // Move
        32648, // Unavailable
        32649, // Link select
        32650  // Working in background
    ];
    private readonly IAppLog _appLog;
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;
    private readonly System.Threading.Timer _timer;
    private Dispatcher? _dispatcher;
    private Window? _window;
    private nint _windowHandle;
    private Exception? _initializationException;
    private long _lastMovementTimestamp;
    private int _active;
    private int _disposeRequested;
    private bool _systemCursorsTransparent;
    private int _updatePending;

    public PointerHighlightService(IAppLog? appLog = null)
    {
        _appLog = appLog ?? NullAppLog.Instance;
        _thread = new Thread(RunOverlayThread)
        {
            IsBackground = true,
            Name = "Voltura Air pointer highlight"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait();
        if (_initializationException is not null)
        {
            _thread.Join();
            _started.Dispose();
            throw new InvalidOperationException("Could not initialize the pointer highlight overlay.", _initializationException);
        }

        _timer = new System.Threading.Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void NotifyPointerActivity()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        Volatile.Write(ref _lastMovementTimestamp, Stopwatch.GetTimestamp());
        Interlocked.Exchange(ref _active, 1);
        QueueOverlayUpdate();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _dispatcher?.BeginInvoke(() =>
        {
            RestoreSystemCursors();
            _window?.Close();
            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        });
        _thread.Join(TimeSpan.FromSeconds(2));
        _timer.Dispose();
        _started.Dispose();
    }

    private void RunOverlayThread()
    {
        try
        {
            RecoverSystemCursors();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _window = CreateOverlayWindow();
            _window.SourceInitialized += (_, _) => InitializeNativeWindow(_window);
            _window.Show();
            _window.Hide();
        }
        catch (Exception ex)
        {
            _initializationException = ex;
        }
        finally
        {
            _started.Set();
        }

        if (_initializationException is not null)
        {
            return;
        }

        try
        {
            Dispatcher.Run();
        }
        finally
        {
            RestoreSystemCursors();
        }
    }

    private void OnTimerTick(object? state)
    {
        QueueOverlayUpdate();
    }

    private void QueueOverlayUpdate()
    {
        var dispatcher = _dispatcher;
        if (Volatile.Read(ref _disposeRequested) != 0 ||
            dispatcher is null ||
            dispatcher.HasShutdownStarted ||
            Interlocked.CompareExchange(ref _updatePending, 1, 0) != 0)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(DispatcherPriority.Render, UpdateOverlay);
    }

    private void UpdateOverlay()
    {
        try
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return;
            }

            var observedMovement = Volatile.Read(ref _lastMovementTimestamp);
            if (Stopwatch.GetTimestamp() - observedMovement > VisibleDurationTicks)
            {
                _window?.Hide();
                RestoreSystemCursors();
                Interlocked.Exchange(ref _active, 0);
                _timer.Change(Timeout.Infinite, Timeout.Infinite);

                if (Volatile.Read(ref _lastMovementTimestamp) != observedMovement)
                {
                    Interlocked.Exchange(ref _active, 1);
                    QueueOverlayUpdate();
                }

                return;
            }

            ScheduleExpiryCheck(observedMovement);
            if (_window is null || !GetCursorPos(out var cursorPosition))
            {
                return;
            }

            if (!_window.IsVisible)
            {
                MakeSystemCursorsTransparent();
                _window.Show();
            }

            PositionOverlay(cursorPosition);
        }
        finally
        {
            Interlocked.Exchange(ref _updatePending, 0);
        }
    }

    private void ScheduleExpiryCheck(long observedMovement)
    {
        var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - observedMovement);
        var remainingTicks = Math.Max(1, VisibleDurationTicks - elapsedTicks);
        var remainingMilliseconds = Math.Max(
            1,
            (int)Math.Ceiling(remainingTicks * 1000d / Stopwatch.Frequency));
        _timer.Change(remainingMilliseconds, Timeout.Infinite);
    }

    private void PositionOverlay(Point cursorPosition)
    {
        var dpi = GetDpiForWindow(_windowHandle);
        if (dpi == 0)
        {
            dpi = 96;
        }

        PositionOverlay(cursorPosition, dpi);
        var updatedDpi = GetDpiForWindow(_windowHandle);
        if (updatedDpi != 0 && updatedDpi != dpi)
        {
            PositionOverlay(cursorPosition, updatedDpi);
        }
    }

    private void PositionOverlay(Point cursorPosition, uint dpi)
    {
        _ = SetWindowPos(
            _windowHandle,
            HwndTopmost,
            cursorPosition.X - ScaleForDpi(PointerHotspotXDips, dpi),
            cursorPosition.Y - ScaleForDpi(PointerHotspotYDips, dpi),
            0,
            0,
            SwpNoActivate | SwpNoSize | SwpShowWindow);
    }

    internal static int ScaleForDpi(double deviceIndependentPixels, uint dpi)
    {
        return (int)Math.Round(deviceIndependentPixels * dpi / 96d);
    }

    private void MakeSystemCursorsTransparent()
    {
        if (_systemCursorsTransparent)
        {
            return;
        }

        foreach (var cursorId in SystemCursorIds)
        {
            var cursor = CreateTransparentCursor();
            if (cursor == 0 || !SetSystemCursor(cursor, cursorId))
            {
                var error = Marshal.GetLastWin32Error();
                if (cursor != 0)
                {
                    _ = DestroyCursor(cursor);
                }

                _appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "pointer_highlight_cursor_substitution",
                    Outcome: "failed",
                    Win32Error: error));
                RecoverSystemCursors();
                return;
            }
        }

        _systemCursorsTransparent = true;
    }

    private static nint CreateTransparentCursor()
    {
        var width = Math.Max(1, GetSystemMetrics(SmCxCursor));
        var height = Math.Max(1, GetSystemMetrics(SmCyCursor));
        var bytesPerRow = ((width + 15) / 16) * 2;
        var planeLength = bytesPerRow * height;
        var andPlane = new byte[planeLength];
        Array.Fill(andPlane, byte.MaxValue);
        var xorPlane = new byte[planeLength];
        return CreateCursor(0, 0, 0, width, height, andPlane, xorPlane);
    }

    private void RestoreSystemCursors()
    {
        if (!_systemCursorsTransparent)
        {
            return;
        }

        RecoverSystemCursors();
        _systemCursorsTransparent = false;
    }

    private void RecoverSystemCursors()
    {
        if (!SystemParametersInfo(SpiSetCursors, 0, 0, 0))
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "pointer_highlight_cursor_restore",
                Outcome: "failed",
                Win32Error: Marshal.GetLastWin32Error()));
        }
    }

    private static Window CreateOverlayWindow()
    {
        var accent = WindowsTheme.DarkAccent;
        var glowColor = WpfColor.FromArgb(accent.A, accent.R, accent.G, accent.B);
        var pointer = new Path
        {
            Data = Geometry.Parse("M 18,14 L 18,61 L 30,49 L 41,71 L 52,65 L 41,44 L 58,42 Z"),
            Fill = System.Windows.Media.Brushes.White,
            Stroke = new SolidColorBrush(WpfColor.FromRgb(17, 24, 27)),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                Color = glowColor,
                Opacity = 1,
                ShadowDepth = 0
            },
            RenderTransform = new TranslateTransform(GlowPaddingDips, GlowPaddingDips)
        };

        return new Window
        {
            Width = 128,
            Height = 136,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Content = new Grid { Children = { pointer } },
            Focusable = false,
            IsHitTestVisible = false,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Manual,
            Topmost = true,
            WindowStyle = WindowStyle.None
        };
    }

    private void InitializeNativeWindow(Window window)
    {
        var source = (HwndSource)PresentationSource.FromVisual(window);
        _windowHandle = source.Handle;
        var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle);
        _ = SetWindowLongPtr(
            _windowHandle,
            GwlExStyle,
            extendedStyle | (nint)(WsExNoActivate | WsExToolWindow | WsExTransparent));
        source.AddHook(WindowMessageHook);
    }

    private static nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            handled = true;
            return HtTransparent;
        }

        if (message == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }

        return 0;
    }

    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const int SmCxCursor = 13;
    private const int SmCyCursor = 14;
    private const uint SpiSetCursors = 0x0057;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateCursor(nint instance, int hotSpotX, int hotSpotY, int width, int height, byte[] andPlane, byte[] xorPlane);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(nint cursor);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemCursor(nint cursor, uint cursorId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height, uint flags);
}
