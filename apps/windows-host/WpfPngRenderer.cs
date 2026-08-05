using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VolturaAir.Host;

internal static class WpfPngRenderer
{
    internal static async Task SaveAsync(
        FrameworkElement visual,
        Brush background,
        string outputPath,
        Size size)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!visual.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("WPF rendering must run on the visual's dispatcher thread.");
        }
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "WPF render dimensions must be positive.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Layout(visual, size);
        await visual.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        await visual.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
        Layout(visual, size);

        var pixelWidth = checked((int)Math.Ceiling(size.Width));
        var pixelHeight = checked((int)Math.Ceiling(size.Height));
        var target = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, size.Width, size.Height);
            context.DrawRectangle(background, null, bounds);
            context.DrawRectangle(
                new VisualBrush(visual)
                {
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    Stretch = Stretch.Fill
                },
                null,
                bounds);
        }

        target.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        await using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        encoder.Save(stream);
        await stream.FlushAsync();
    }

    private static void Layout(FrameworkElement visual, Size size)
    {
        visual.Measure(size);
        visual.Arrange(new Rect(new Point(0, 0), size));
        visual.UpdateLayout();
    }
}
