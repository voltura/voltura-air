using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WpfPngRendererTests
{
    [Fact]
    public void SavesPngWithoutShowingAWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = RenderAsync(dispatcher);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Off-screen WPF rendering timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        async Task RenderAsync(Dispatcher dispatcher)
        {
            var outputPath = Path.Combine(
                Path.GetTempPath(),
                $"voltura-air-wpf-render-{Guid.NewGuid():N}.png");
            try
            {
                var visual = new Grid();
                visual.Children.Add(new TextBlock
                {
                    Text = "Voltura Air",
                    Foreground = Brushes.Black,
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                await WpfPngRenderer.SaveAsync(
                    visual,
                    Brushes.White,
                    outputPath,
                    new Size(320, 180));

                await using var stream = File.OpenRead(outputPath);
                var decoder = new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                var frame = Assert.Single(decoder.Frames);
                Assert.Equal(320, frame.PixelWidth);
                Assert.Equal(180, frame.PixelHeight);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                File.Delete(outputPath);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
        }
    }
}
