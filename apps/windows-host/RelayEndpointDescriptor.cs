namespace VolturaAir.Host;

using System.Text.Json;

internal sealed record RelayEndpointDescriptor(
    string ServiceId,
    Uri HttpsBase,
    Uri WebSocketBase,
    bool SupportsTurn)
{
    public const string OfficialServiceId = "voltura-cloud-v1";
    private const string FallbackOfficialHttpsBase = "https://voltura-air-relay.voltura-air.workers.dev";
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public bool IsOfficial => string.Equals(ServiceId, OfficialServiceId, StringComparison.Ordinal);

    public static RelayEndpointDescriptor FromSettings(NetworkSettingsSnapshot settings)
    {
        var configured = AppNetworkSettings.NormalizeRelayEndpoint(settings.CustomRelayEndpoint);
        var official = Official();
        var https = new Uri(configured ?? official.HttpsBase.ToString(), UriKind.Absolute);
        var websocket = new UriBuilder(https)
        {
            Scheme = https.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = https.IsDefaultPort ? -1 : https.Port
        }.Uri;
        return new(configured is null ? official.ServiceId : "custom-v1", https, websocket, configured is null && official.SupportsTurn);
    }

    internal static RelayEndpointDescriptor Official()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "relay-service.json");
            var json = JsonSerializer.Deserialize<RelayServiceConfiguration>(
                File.ReadAllText(path),
                SerializerOptions);
            if (json is not null && json.ServiceId == OfficialServiceId && Uri.TryCreate(json.HttpsBase, UriKind.Absolute, out var https) &&
                https.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(https.UserInfo) && string.IsNullOrEmpty(https.Query) && string.IsNullOrEmpty(https.Fragment))
            {
                return new(json.ServiceId, https, ToWebSocketBase(https), json.SupportsTurn);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        var fallback = new Uri(FallbackOfficialHttpsBase, UriKind.Absolute);
        return new(OfficialServiceId, fallback, ToWebSocketBase(fallback), true);
    }

    private static Uri ToWebSocketBase(Uri https) => new UriBuilder(https)
    {
        Scheme = "wss",
        Port = https.IsDefaultPort ? -1 : https.Port
    }.Uri;

    private sealed record RelayServiceConfiguration(string ServiceId, string HttpsBase, bool SupportsTurn);
}
