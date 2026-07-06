using System.Globalization;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void NewPairing()
    {
        _pairingUrl = CreatePairingUrl();
        if (_activePage == HostPage.Connect)
        {
            SelectPage(HostPage.Connect);
        }
    }

    private string CreatePairingUrl()
    {
        var token = _pairingManager.CreatePairingToken();
        var url = new UriBuilder(_clientUrl)
        {
            Query = $"t={Uri.EscapeDataString(token)}&v={Uri.EscapeDataString(AppVersion.Display)}"
        };

        if (!string.Equals(_clientUrl, _serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            url.Query = $"{url.Query.TrimStart('?')}&h={Uri.EscapeDataString(CreateHostHint(_clientUrl, _serverUrl))}";
        }

        return url.Uri.ToString();
    }

    private string GetVisiblePairingUrl()
    {
        return _usePublicScreenshotPairingUrl ? ProductSiteUrl : _pairingUrl;
    }

    internal static string CreateHostHint(string clientUrl, string serverUrl)
    {
        if (Uri.TryCreate(clientUrl, UriKind.Absolute, out var clientUri) &&
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) &&
            string.Equals(clientUri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(clientUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return serverUri.Port.ToString(CultureInfo.InvariantCulture);
        }

        return serverUrl;
    }
}
