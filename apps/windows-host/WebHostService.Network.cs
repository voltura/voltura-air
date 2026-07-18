using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    internal static bool IsPortAvailable(int port)
    {
        return PortSelector.IsPortAvailable(port);
    }

    internal static int FindFreePort()
    {
        return PortSelector.FindFreePort();
    }

    internal static string? GetDnsLanAddressFallback()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .FirstOrDefault(address => !address.StartsWith("127.", StringComparison.Ordinal));
        }
        catch (SocketException)
        {
            return null;
        }
    }

    internal static string BuildServerUrl(string hostAddress, int port)
    {
        return $"http://{hostAddress}:{port}";
    }

    internal static string BuildWebSocketUrl(string hostAddress, int port)
    {
        return $"ws://{hostAddress}:{port}/ws";
    }

    internal static bool IsAllowedWebSocketOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
            originUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var requestHost = request.Host.Host;
        var requestPort = request.Host.Port;
        if (string.Equals(originUri.Host, requestHost, StringComparison.OrdinalIgnoreCase) &&
            (requestPort is null || originUri.Port == requestPort))
        {
            return true;
        }

#if DEBUG
        var configuredClientUrl = Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        if (Uri.TryCreate(configuredClientUrl, UriKind.Absolute, out var configuredClientUri) &&
            SameOrigin(originUri, configuredClientUri))
        {
            return true;
        }
#endif

        return IsLoopbackOrPrivateHost(originUri.Host);
    }

    private static string GetRateLimitKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

#if DEBUG
    private static bool SameOrigin(Uri first, Uri second)
    {
        return string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.Host, second.Host, StringComparison.OrdinalIgnoreCase) &&
            first.Port == second.Port;
    }
#endif

    private static bool IsLoopbackOrPrivateHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 127 ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 169 && bytes[1] == 254);
    }

    private HostStatusMetadata CreateHostStatus(string clientId)
    {
        var pcName = Environment.MachineName;
        var developerMode = AppDeveloperSettings.DeveloperMode();
        var webClientBuildId = WebHostStaticFiles.ReadWebClientBuildId(WebHostStaticFiles.ResolveStaticRoot());
        return new HostStatusMetadata(
            AppVersion.Display,
            webClientBuildId,
            pcName,
            SelectedAdapterName,
            AdvertisedHostAddress,
            Port,
            WebSocketUrl,
            AppRemoteSettings.ToProtocolId(AppRemoteSettings.GetDefaultRemoteMode()),
            CanLaunchRemoteApps(clientId) ? _appLaunchService.GetActions() : [],
            _textDestinationService.GetMetadata() is var textDestination ? new TextTransferTargetMetadata(textDestination.Mode, textDestination.DisplayName, textDestination.Available) : new TextTransferTargetMetadata("focused", "Currently focused application", true),
            _pairingManager.GetDevicePointerSpeed(clientId),
            AppPointerSettings.GetCustomPointer().Enabled,
            developerMode,
            developerMode ? DeveloperSessionId : null,
            Volatile.Read(ref _inputBlockedByElevation) != 0);
    }

    private static string GetSelectedAdapterName(LanAddressCandidate? selectedCandidate)
    {
        return selectedCandidate is null
            ? "DNS fallback"
            : LanAddressSelector.GetAdapterDisplayName(selectedCandidate);
    }
}
