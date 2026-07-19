using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolturaAir.Host.Features.Connect;

namespace VolturaAir.Host.Tests;

public sealed class PairingQrCodeRenderingTests
{
    [Fact]
    public void PairingQrCodeIncludesVolturaAirIconInCenter()
    {
        var source = PairingQrCodeRenderer.Create("http://192.168.1.20:51395/pair?t=redacted&v=0.6.3");
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var insetX = converted.PixelWidth * 2 / 5;
        var insetY = converted.PixelHeight * 2 / 5;
        var chromaticPixelCount = 0;
        for (var y = insetY; y < converted.PixelHeight - insetY; y++)
        {
            for (var x = insetX; x < converted.PixelWidth - insetX; x++)
            {
                var offset = (y * stride) + (x * 4);
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > 24)
                {
                    chromaticPixelCount++;
                }
            }
        }

        Assert.True(source.IsFrozen);
        Assert.True(chromaticPixelCount > 100, $"Expected a colored Voltura Air icon in the QR center, but found {chromaticPixelCount} chromatic pixels.");
    }
}
