using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public abstract class WebHostServiceTestBase : IsolatedHostSettingsTest
{
    protected static async Task<JsonElement> SendHelloAsync(WebHostService webHost, object payload)
    {
        using var socket = await ConnectAsync(webHost);

        var response = await SendAndReceiveAsync(socket, payload);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        return response;
    }

    protected static async Task<JsonElement> SendAndReceiveAsync(WebSocket socket, object payload)
    {
        await SendAsync(socket, payload);
        var response = await ReceiveTextAsync(socket);
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    protected static async Task<JsonElement> SendReconnectAsync(WebHostService webHost, string clientId, string deviceName, PairingTestKey key)
    {
        using var socket = await ConnectAsync(webHost);
        var challenge = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName
        });
        Assert.Equal("pair.challenge", challenge.GetProperty("type").GetString());

        var accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.proof",
            clientId,
            signature = key.SignReconnectChallenge(clientId, challenge.GetProperty("challenge").GetString()!)
        });
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        return accepted;
    }

    protected static Task SendAsync(WebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    protected static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(text);
        ProtocolFrameAssert.Conforms(document.RootElement);
        return text;
    }

    protected static async Task<WebSocketCloseStatus?> ReceiveCloseStatusAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return result.CloseStatus;
            }
        }

        return socket.CloseStatus;
    }

    protected static Task<WebSocket> ConnectAsync(WebHostService webHost)
    {
        var app = webHost.Application ?? throw new InvalidOperationException("The in-memory web host has not started.");
        return app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
    }

    protected static HttpRequest CreateOriginRequest(string? origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("192.168.68.51", 51395);
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context.Request;
    }

    protected sealed class WebHostFixture : IAsyncDisposable
    {
        private WebHostFixture(
            TempPairingStore store,
            FakeInputInjector inputInjector,
            PairingManager manager,
            WebHostService webHost)
        {
            Store = store;
            InputInjector = inputInjector;
            Manager = manager;
            WebHost = webHost;
        }

        public TempPairingStore Store { get; }

        public FakeInputInjector InputInjector { get; }

        public PairingManager Manager { get; }

        public WebHostService WebHost { get; }

        public static async Task<WebHostFixture> StartAsync(
            ISystemAudioController? audioController = null,
            IRemoteActionExecutor? remoteActionExecutor = null,
            IAppLaunchService? appLaunchService = null,
            IUrlOpenService? urlOpenService = null,
            IAppLog? appLog = null,
            ITextDestinationService? textDestinationService = null,
            IClipboardTextReader? clipboardTextReader = null)
        {
            var store = new TempPairingStore();
            var inputInjector = new FakeInputInjector();
            var manager = new PairingManager(store.Store);

            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                audioController,
                remoteActionExecutor,
                appLog: appLog,
                appLaunchService: appLaunchService,
                urlOpenService: urlOpenService,
                textDestinationService: textDestinationService,
                clipboardTextReader: clipboardTextReader,
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            await webHost.StartAsync();
            return new WebHostFixture(store, inputInjector, manager, webHost);
        }

        public async ValueTask DisposeAsync()
        {
            await WebHost.StopAsync();
            await WebHost.DisposeAsync();
            Store.Dispose();
            InputInjector.Dispose();
        }
    }

    protected sealed class FakeTextDestinationService(TextDestinationMetadata metadata, TextDeliveryResult result) : ITextDestinationService
    {
        public List<(string Text, bool SendEnter)> Deliveries { get; } = [];
        public TextDestinationMetadata GetMetadata() => metadata;
        public Task<TextDeliveryResult> DeliverAsync(string text, bool sendEnter, CancellationToken cancellationToken)
        {
            Deliveries.Add((text, sendEnter));
            return Task.FromResult(result);
        }
    }

    protected sealed class FakeClipboardTextReader(ClipboardTextReadResult result) : IClipboardTextReader
    {
        public int ReadCount { get; private set; }

        public ClipboardTextReadResult ReadText()
        {
            ReadCount++;
            return result;
        }
    }

    protected sealed class FakeRemoteActionExecutor : IRemoteActionExecutor
    {
        public List<string> Actions { get; } = new();

        public Task<bool> TryExecuteAsync(string action, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(true);
        }
    }

    protected sealed class FakeAppLaunchService(
        IReadOnlyList<AppLaunchActionSummary> actions,
        AppLaunchExecutionResult result) : IAppLaunchService
    {
        public List<string> ActionIds { get; } = new();

        public IReadOnlyList<AppLaunchActionSummary> GetActions() => actions;

        public AppLaunchExecutionResult Execute(string actionId)
        {
            ActionIds.Add(actionId);
            return result;
        }
    }

    protected sealed class FakeAudioController : ISystemAudioController
    {
        private AudioState _state;

        public FakeAudioController(AudioState state)
        {
            _state = state;
        }

        public int GetStateCalls { get; private set; }

        public AudioState GetState()
        {
            GetStateCalls++;
            return _state;
        }

        public AudioState ToggleMute()
        {
            _state = _state with { Muted = !_state.Muted };
            return _state;
        }

        public AudioState SetVolume(int volume)
        {
            _state = new AudioState(volume, false);
            return _state;
        }
    }
}
