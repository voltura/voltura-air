using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WebHostServiceTests
{
    [Fact]
    public async Task WebSocketAcceptsPairingTokenAndStoredSecretReconnect()
    {
        var originalSettings = AppNetworkSettings.Load();
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var manager = new PairingManager(store.Store);
        WebHostService? webHost = null;

        try
        {
            AppNetworkSettings.Save(new NetworkSettingsSnapshot(
                NetworkSelectionMode.Automatic,
                ManualHostAddress: null,
                PortSelectionMode.Automatic,
                ManualPort: null,
                LastAutomaticPort: null,
                LastAutomaticHostAddress: originalSettings.LastAutomaticHostAddress));

            webHost = new WebHostService(manager, new InputDispatcher(inputInjector));
            await webHost.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            var token = manager.CreatePairingToken();

            var paired = await SendHelloAsync(webHost.Port, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = token
            });
            var secret = paired.GetProperty("secret").GetString();

            var reconnected = await SendHelloAsync(webHost.Port, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                secret
            });

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(secret));
            Assert.Equal("pair.accepted", reconnected.GetProperty("type").GetString());
            Assert.Equal(clientId, reconnected.GetProperty("clientId").GetString());
        }
        finally
        {
            if (webHost is not null)
            {
                await webHost.StopAsync();
                await webHost.DisposeAsync();
            }

            AppNetworkSettings.Save(originalSettings);
        }
    }

    private static async Task<JsonElement> SendHelloAsync(int port, object payload)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var response = await ReceiveTextAsync(socket);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket socket)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
