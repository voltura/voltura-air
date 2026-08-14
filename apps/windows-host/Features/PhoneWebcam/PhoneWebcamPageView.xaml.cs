using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBrushes = System.Windows.Media.Brushes;
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
        PreviewImage.Visibility = Visibility.Visible;
        PreviewEmptyState.Visibility = Visibility.Collapsed;
    }

    internal void ShowEmptyState(string title, string message)
    {
        _previewBitmap = null;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewSurface.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SurfaceRaisedBrush");
        PreviewEmptyTitle.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextBrush");
        PreviewEmptyMessage.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MutedTextBrush");
        PreviewEmptyTitle.Text = title;
        PreviewEmptyMessage.Text = message;
        PreviewEmptyState.Visibility = Visibility.Visible;
        RetryPreviewButton.Visibility = Visibility.Collapsed;
    }

    internal void ShowOpeningPreview()
    {
        PreviewSurface.Background = WpfBrushes.Black;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewEmptyTitle.Text = "Opening live preview…";
        PreviewEmptyMessage.Text = string.Empty;
        PreviewEmptyTitle.Foreground = WpfBrushes.White;
        PreviewEmptyMessage.Foreground = WpfBrushes.White;
        PreviewEmptyState.Visibility = Visibility.Visible;
    }

    internal void ShowPreviewFailure(string message)
    {
        ShowEmptyState("Preview unavailable", message);
        RetryPreviewButton.Visibility = Visibility.Visible;
    }
}
