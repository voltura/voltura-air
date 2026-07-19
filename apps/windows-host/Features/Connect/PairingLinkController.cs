using System.Globalization;

namespace VolturaAir.Host.Features.Connect;

internal sealed class PairingLinkController
{
    private const string PairingPath = "/pair";

    private readonly PairingManager _pairingManager;
    private readonly bool _usesServerUrlAsClientUrl;
    private string _serverUrl;
    private string _clientUrl;
    private PairingLinkState _current;

    public PairingLinkController(PairingManager pairingManager, string serverUrl, string? clientUrl)
    {
        _pairingManager = pairingManager;
        _serverUrl = serverUrl;
        _usesServerUrlAsClientUrl = string.IsNullOrWhiteSpace(clientUrl);
        _clientUrl = _usesServerUrlAsClientUrl ? serverUrl : clientUrl!.TrimEnd('/');
        _current = CreatePairingLink();
    }

    public string Url => _current.Url;

    public DateTimeOffset RefreshAt => _current.RefreshAt;

    public string ServerUrl => _serverUrl;

    public void CreateNew(DateTimeOffset? now = null)
    {
        _current = CreatePairingLink(now);
    }

    public bool RefreshIfDue(DateTimeOffset now)
    {
        if (_current.RefreshAt > now)
        {
            return false;
        }

        CreateNew(now);
        return true;
    }

    public bool UpdateServerUrl(string serverUrl)
    {
        if (string.Equals(_serverUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _serverUrl = serverUrl;
        if (_usesServerUrlAsClientUrl)
        {
            _clientUrl = serverUrl;
        }

        CreateNew();
        return true;
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

    private PairingLinkState CreatePairingLink(DateTimeOffset? now = null)
    {
        var pairingCode = _pairingManager.CreatePairingCode(now);
        var query = $"t={Uri.EscapeDataString(pairingCode.Value)}&v={Uri.EscapeDataString(AppVersion.Display)}";
        if (!string.Equals(_clientUrl, _serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            query = $"{query}&h={Uri.EscapeDataString(CreateHostHint(_clientUrl, _serverUrl))}";
        }

        var url = new UriBuilder(_clientUrl)
        {
            Path = PairingPath,
            Query = query
        };
        return new PairingLinkState(url.Uri.ToString(), pairingCode.RefreshAt);
    }

    private sealed record PairingLinkState(string Url, DateTimeOffset RefreshAt);
}
