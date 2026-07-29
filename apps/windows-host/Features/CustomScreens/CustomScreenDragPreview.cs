using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed partial class CustomScreenDragPreview : IDisposable
{
    private const int ExtendedStyleIndex = -20;
    private const int TransparentExtendedStyle = 0x00000020;
    private const int ToolWindowExtendedStyle = 0x00000080;
    private const int NoActivateExtendedStyle = 0x08000000;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;
    private const uint NoOwnerZOrder = 0x0200;
    private const int NonClientHitTestMessage = 0x0084;
    private const int MouseActivateMessage = 0x0021;
    private static readonly nint HitTestTransparent = new(-1);
    private static readonly nint MouseActivateNoActivate = new(3);

    private readonly Window _window;
    private readonly DispatcherTimer _positionTimer;
    private readonly Point _anchorRatio;
    private nint _windowHandle;
    private bool _disposed;

    public CustomScreenDragPreview(
        FrameworkElement coordinateSpace,
        FrameworkElement source,
        Point anchorRatio,
        double detachedDisplayScale = 1)
    {
        _anchorRatio = new Point(
            Math.Clamp(anchorRatio.X, 0, 1),
            Math.Clamp(anchorRatio.Y, 0, 1));
        var sourceSize = EnsureArranged(source);
        var displaySize = TransformedSize(
            source,
            coordinateSpace,
            sourceSize,
            detachedDisplayScale);
        var snapshot = Capture(source, sourceSize, coordinateSpace);
        var image = new Image
        {
            Source = snapshot,
            Width = displaySize.Width,
            Height = displaySize.Height,
            Opacity = 0.82,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var preview = new Border
        {
            Background = source.TryFindResource("SurfaceBrush") as Brush,
            BorderBrush = source.TryFindResource("AccentBrush") as Brush ??
                Brushes.DodgerBlue,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Child = image,
            IsHitTestVisible = false
        };

        _window = new Window
        {
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Content = preview,
            Height = displaySize.Height + 3,
            IsHitTestVisible = false,
            Left = Window.GetWindow(coordinateSpace)?.Left ?? 0,
            Opacity = 0,
            Owner = Window.GetWindow(coordinateSpace),
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Top = Window.GetWindow(coordinateSpace)?.Top ?? 0,
            Width = displaySize.Width + 3,
            WindowStyle = WindowStyle.None
        };
        _window.Show();
        _windowHandle = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(_windowHandle)?.AddHook(FilterWindowMessage);
        var extendedStyle = GetWindowLong(_windowHandle, ExtendedStyleIndex);
        _ = SetWindowLong(
            _windowHandle,
            ExtendedStyleIndex,
            extendedStyle |
                TransparentExtendedStyle |
                ToolWindowExtendedStyle |
                NoActivateExtendedStyle);

        _positionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(15),
            DispatcherPriority.Send,
            (_, _) => MoveToCursor(),
            _window.Dispatcher);
        MoveToCursor();
        _window.Opacity = 0.94;
        _positionTimer.Start();
    }

    internal Window PreviewWindow => _window;

    internal Point LastScreenPosition { get; private set; }

    public void MoveToCursor()
    {
        if (_disposed || _windowHandle == nint.Zero)
        {
            return;
        }

        var cursor = System.Windows.Forms.Cursor.Position;
        var dpi = Math.Max(96u, GetDpiForWindow(_windowHandle));
        var dpiScale = dpi / 96d;
        var topLeft = CalculateTopLeft(
            new Point(cursor.X, cursor.Y),
            new Size(_window.Width, _window.Height),
            _anchorRatio,
            dpiScale);
        var x = (int)Math.Round(topLeft.X);
        var y = (int)Math.Round(topLeft.Y);
        _ = SetWindowPos(
            _windowHandle,
            nint.Zero,
            x,
            y,
            0,
            0,
            NoSize | NoZOrder | NoActivate | ShowWindow | NoOwnerZOrder);
        LastScreenPosition = new Point(x, y);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _positionTimer.Stop();
        HwndSource.FromHwnd(_windowHandle)?.RemoveHook(FilterWindowMessage);
        _window.Close();
        _windowHandle = nint.Zero;
    }

    internal static Point CalculateTopLeft(
        Point cursorPixels,
        Size previewSizeDip,
        Point anchorRatio,
        double dpiScale) =>
        new(
            cursorPixels.X -
                (previewSizeDip.Width * anchorRatio.X * dpiScale),
            cursorPixels.Y -
                (previewSizeDip.Height * anchorRatio.Y * dpiScale));

    private nint FilterWindowMessage(
        nint window,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == NonClientHitTestMessage)
        {
            handled = true;
            return HitTestTransparent;
        }

        if (message == MouseActivateMessage)
        {
            handled = true;
            return MouseActivateNoActivate;
        }

        return nint.Zero;
    }

    private static RenderTargetBitmap Capture(
        FrameworkElement source,
        Size sourceSize,
        FrameworkElement coordinateSpace)
    {
        var dpi = VisualTreeHelper.GetDpi(coordinateSpace);
        var snapshot = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(sourceSize.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(sourceSize.Height * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        var isolatedVisual = new DrawingVisual();
        using (var drawing = isolatedVisual.RenderOpen())
        {
            drawing.DrawRectangle(
                new VisualBrush(source)
                {
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    Stretch = Stretch.Fill
                },
                null,
                new Rect(0, 0, sourceSize.Width, sourceSize.Height));
        }
        snapshot.Render(isolatedVisual);
        return snapshot;
    }

    private static Size EnsureArranged(FrameworkElement source)
    {
        if (source.ActualWidth > 0 && source.ActualHeight > 0)
        {
            return new Size(source.ActualWidth, source.ActualHeight);
        }

        source.Measure(new Size(640, 640));
        var desired = new Size(
            Math.Max(1, source.DesiredSize.Width),
            Math.Max(1, source.DesiredSize.Height));
        source.Arrange(new Rect(desired));
        source.UpdateLayout();
        return desired;
    }

    private static Size TransformedSize(
        FrameworkElement source,
        FrameworkElement coordinateSpace,
        Size sourceSize,
        double detachedDisplayScale)
    {
        try
        {
            var bounds = source.TransformToVisual(coordinateSpace)
                .TransformBounds(new Rect(new Point(), sourceSize));
            return new Size(
                Math.Max(1, bounds.Width),
                Math.Max(1, bounds.Height));
        }
        catch (InvalidOperationException)
        {
            return new Size(
                Math.Max(1, sourceSize.Width * detachedDisplayScale),
                Math.Max(1, sourceSize.Height * detachedDisplayScale));
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(nint window, int index, int value);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
