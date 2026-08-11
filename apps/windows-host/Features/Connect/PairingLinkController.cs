using System.Globalization;

namespace VolturaAir.Host.Features.Connect;

internal sealed class PairingLinkController
{
    private const string PairingPath = "/pair";

    private readonly PairingManager _pairingManager;
    private readonly bool _usesServerUrlAsClientUrl;
    private readonly ConnectionTransportMode _transportMode;
    private readonly string? _relayRouteId;
    private readonly RelayEndpointDescriptor? _relayEndpoint;
    private readonly bool _enhancedCapabilitiesEnabled;
    private readonly string? _secureDirectRouteId;
    private string _serverUrl;
    private string _clientUrl;
    private PairingLinkState _current;

    public PairingLinkController(
        PairingManager pairingManager,
        string serverUrl,
        string? clientUrl,
        ConnectionTransportMode transportMode = ConnectionTransportMode.DirectLan,
        string? relayRouteId = null,
        RelayEndpointDescriptor? relayEndpoint = null,
        bool enhancedCapabilitiesEnabled = false,
        string? secureDirectRouteId = null)
    {
        _pairingManager = pairingManager;
        _serverUrl = serverUrl;
        _transportMode = transportMode;
        _relayRouteId = relayRouteId;
        _relayEndpoint = relayEndpoint;
        _enhancedCapabilitiesEnabled = enhancedCapabilitiesEnabled;
        _secureDirectRouteId = secureDirectRouteId;
        _usesServerUrlAsClientUrl = string.IsNullOrWhiteSpace(clientUrl);
        _clientUrl = _usesServerUrlAsClientUrl ? serverUrl : clientUrl!.TrimEnd('/');
        _current = CreatePairingLink();
    }

    public string Url => _current.Url;
    public string? StandardLocalUrl => _current.StandardLocalUrl;

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
        if (_transportMode == ConnectionTransportMode.Relay)
        {
            if (string.IsNullOrWhiteSpace(_relayRouteId))
            {
                throw new InvalidOperationException("Relay pairing requires a routing identity.");
            }

            var relayUrl = _relayEndpoint?.IsOfficial != false
                ? $"https://voltura.se/a/{_relayRouteId}?v={Uri.EscapeDataString(AppVersion.Display)}#{Uri.EscapeDataString(pairingCode.Value)}"
                : CreateCustomRelayLink(_relayEndpoint.HttpsBase, _relayRouteId, pairingCode.Value);
            return new PairingLinkState(relayUrl, pairingCode.RefreshAt, null);
        }

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
        var standardLocalUrl = url.Uri.ToString();
        if (!_enhancedCapabilitiesEnabled)
        {
            return new PairingLinkState(standardLocalUrl, pairingCode.RefreshAt, null);
        }
        if (string.IsNullOrWhiteSpace(_secureDirectRouteId))
        {
            return new PairingLinkState(standardLocalUrl, pairingCode.RefreshAt, null);
        }
        var secureUrl = $"https://voltura.se/s/{_secureDirectRouteId}?v={Uri.EscapeDataString(AppVersion.Display)}#{Uri.EscapeDataString(pairingCode.Value)}";
        return new PairingLinkState(secureUrl, pairingCode.RefreshAt, standardLocalUrl);
    }

    private static string CreateCustomRelayLink(Uri endpoint, string routeId, string token)
    {
        var endpointBytes = System.Text.Encoding.UTF8.GetBytes(endpoint.ToString().TrimEnd('/'));
        var encodedEndpoint = Convert.ToBase64String(endpointBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"https://voltura.se/air/app/?r={routeId}&v={Uri.EscapeDataString(AppVersion.Display)}&e={encodedEndpoint}#{Uri.EscapeDataString(token)}";
    }

    private sealed record PairingLinkState(string Url, DateTimeOffset RefreshAt, string? StandardLocalUrl);
}
