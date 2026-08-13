using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.PhoneWebcam;

public partial class PhoneWebcamPageView : WpfUserControl
{
    private WriteableBitmap? _previewBitmap;

    internal PhoneWebcamPageView()
    {
        InitializeComponent();
    }

    internal void SetPreviewFrame(PhoneWebcamPreviewFrame frame)
    {
        if (_previewBitmap is null)
        {
            _previewBitmap = new WriteableBitmap(
                PhoneWebcamPreviewSession.PreviewWidth,
                PhoneWebcamPreviewSession.PreviewHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null);
            PreviewImage.Source = _previewBitmap;
        }

        _previewBitmap.WritePixels(
            new Int32Rect(0, 0, PhoneWebcamPreviewSession.PreviewWidth, PhoneWebcamPreviewSession.PreviewHeight),
            frame.Buffer,
            PhoneWebcamPreviewSession.PreviewStride,
            0);
        PreviewOverlayText.Visibility = Visibility.Collapsed;
    }
}
