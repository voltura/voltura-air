using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connect;

public partial class ConnectPageView : WpfUserControl
{
    public ConnectPageView(
        BitmapSource qrCode,
        string status,
        string pairingLink,
        string hostUrl,
        string selectedIp,
        string selectedPort,
        string? addressWarning,
        string? portWarning,
        Action createNewCode,
        Action copyLink)
    {
        InitializeComponent();
        QrCodeImage.Source = qrCode;
        StatusCard.Value = status;
        PairingLinkCard.Value = pairingLink;
        HostUrlCard.Value = hostUrl;
        SelectedIpCard.Value = selectedIp;
        SelectedPortCard.Value = selectedPort;
        SetNotice(AddressWarningNotice, AddressWarningText, addressWarning);
        SetNotice(PortWarningNotice, PortWarningText, portWarning);
        NewCodeButton.Click += (_, _) => createNewCode();
        CopyLinkButton.Click += (_, _) => copyLink();
    }

    private static void SetNotice(FrameworkElement notice, TextBlock textBlock, string? message)
    {
        textBlock.Text = message ?? string.Empty;
        notice.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
