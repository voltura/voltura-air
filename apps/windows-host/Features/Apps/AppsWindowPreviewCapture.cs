using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VolturaAir.Host.Features.Apps;

internal static class AppsWindowPreviewCapture
{
    private const uint PrintWindowRenderFullContent = 0x00000002;

    internal static AppsPreviewCaptureResult Capture(
        nint windowHandle,
        Rectangle bounds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.Clear(Color.Transparent);
                nint hdc = graphics.GetHdc();
                try
                {
                    bool printed = AppsWindowNativeMethods.PrintWindow(
                        windowHandle,
                        hdc,
                        PrintWindowRenderFullContent);
                    if (!printed)
                    {
                        return new(false, null, 0, 0);
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (LooksBlank(source))
            {
                return new(false, null, 0, 0);
            }

            Size scaledSize = ScaleInside(
                source.Width,
                source.Height,
                AppsProtocol.MaximumPreviewWidth,
                AppsProtocol.MaximumPreviewHeight);
            using var scaled = new Bitmap(scaledSize.Width, scaledSize.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
            }

            byte[] encoded = EncodeJpeg(scaled, quality: 78L);
            if (encoded.Length > AppsProtocol.MaximumPreviewBytes)
            {
                using var smaller = new Bitmap(
                    Math.Max(1, scaled.Width * 3 / 4),
                    Math.Max(1, scaled.Height * 3 / 4),
                    PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(smaller))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(scaled, 0, 0, smaller.Width, smaller.Height);
                }

                encoded = EncodeJpeg(smaller, quality: 65L);
                if (encoded.Length > AppsProtocol.MaximumPreviewBytes)
                {
                    return new(false, null, 0, 0);
                }

                return new(true, encoded, smaller.Width, smaller.Height);
            }

            return new(true, encoded, scaled.Width, scaled.Height);
        }
        catch (Exception exception) when (
            exception is ArgumentException or ExternalException or Win32Exception or OverflowException)
        {
            return new(false, null, 0, 0);
        }
    }

    private static bool LooksBlank(Bitmap bitmap)
    {
        Color first = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        for (int y = 1; y <= 7; y++)
        {
            for (int x = 1; x <= 9; x++)
            {
                Color candidate = bitmap.GetPixel(bitmap.Width * x / 10, bitmap.Height * y / 8);
                if (candidate.ToArgb() != first.ToArgb())
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Size ScaleInside(int width, int height, int maximumWidth, int maximumHeight)
    {
        double scale = Math.Min(1, Math.Min((double)maximumWidth / width, (double)maximumHeight / height));
        return new Size(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders()
            .Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        using var output = new MemoryStream();
        bitmap.Save(output, encoder, parameters);
        return output.ToArray();
    }
}
