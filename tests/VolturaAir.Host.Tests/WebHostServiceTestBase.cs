using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public abstract class WebHostServiceTestBase
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

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    protected static async Task<WebSocketCloseStatus?> ReceiveCloseStatusAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
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
            IAppLog? appLog = null,
            ITextDestinationService? textDestinationService = null)
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
                textDestinationService: textDestinationService,
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

    protected sealed class FakeRemoteActionExecutor : IRemoteActionExecutor
    {
        public List<string> Actions { get; } = new();

        public bool TryExecute(string action)
        {
            Actions.Add(action);
            return true;
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
