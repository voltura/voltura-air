using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace VolturaAir.Host.Features.Connect;

internal static partial class PairingQrCodeRenderer
{
    public static BitmapSource Create(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var code = new QRCode(data);
        using var bitmap = code.GetGraphic(18, System.Drawing.Color.Black, System.Drawing.Color.White, drawQuietZones: true);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DeleteObject(handle);
        }
    }

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint objectHandle);
}
