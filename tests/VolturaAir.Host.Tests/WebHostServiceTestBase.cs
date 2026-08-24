using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using System.Text.Json.Nodes;
using VolturaAir.Host;
using VolturaAir.Host.Features.PhoneWebcam;
using VolturaAir.Host.Features.UsageTelemetry;

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
        var node = JsonSerializer.SerializeToNode(payload, JsonOptions.Default) as JsonObject;
        if (node is not null &&
            node["type"]?.GetValue<string>() == "pair.hello" &&
            node["pairToken"] is JsonValue tokenNode && tokenNode.TryGetValue<string>(out var token) &&
            node["clientId"] is JsonValue clientIdNode && clientIdNode.TryGetValue<string>(out var clientId) &&
            node["reconnectPublicKey"] is JsonValue keyNode && keyNode.TryGetValue<string>(out var reconnectPublicKey))
        {
            // Test-only setup adapter. The socket receives only the current bootstrap protocol.
            var clientNonce = PairingBootstrapCrypto.CreateNonce();
            node.Remove("pairToken");
            node.Remove("hostIdentityFingerprint");
            node["pairTokenId"] = PairingTokenAuthority.CreateTokenId(token);
            node["clientNonce"] = clientNonce;
            var challenge = await SendAndReceiveRawAsync(socket, node);
            if (challenge.GetProperty("type").GetString() != "pair.bootstrap.challenge")
            {
                return challenge;
            }

            var serverNonce = challenge.GetProperty("serverNonce").GetString()!;
            var identity = challenge.GetProperty("hostIdentity");
            return await SendAndReceiveRawAsync(socket, new
            {
                type = "pair.bootstrap.proof",
                clientId,
                proof = PairingBootstrapCrypto.CreateClientProof(
                    token,
                    clientId,
                    clientNonce,
                    serverNonce,
                    reconnectPublicKey,
                    identity.GetProperty("publicKey").GetString()!,
                    identity.GetProperty("fingerprint").GetString()!)
            });
        }

        return await SendAndReceiveRawAsync(socket, payload);
    }

    private static async Task<JsonElement> SendAndReceiveRawAsync(WebSocket socket, object payload)
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

        internal static async Task<WebHostFixture> StartAsync(
            ISystemAudioController? audioController = null,
            IRemoteActionExecutor? remoteActionExecutor = null,
            IAppLaunchService? appLaunchService = null,
            IUrlOpenService? urlOpenService = null,
            IAppLog? appLog = null,
            ITextDestinationService? textDestinationService = null,
            IClipboardTextReader? clipboardTextReader = null,
            IPowerPointAutomationService? powerPointAutomation = null,
            ISystemPowerController? powerController = null,
            IScreenViewCaptureSource? screenViewCapture = null,
            IPhoneWebcamFeature? phoneWebcamFeature = null,
            IPhoneWebcamWebRtcPeerFactory? phoneWebcamPeerFactory = null,
            IScreenViewWebRtcPeerFactory? screenViewPeerFactory = null,
            IUsageTelemetryRecorder? usageTelemetry = null)
        {
            var store = new TempPairingStore();
            var inputInjector = new FakeInputInjector();
            var manager = new PairingManager(store.Store);

            var webHost = new WebHostService(
                pairingManager: manager,
                inputDispatcher: new InputDispatcher(inputInjector),
                audioController: audioController,
                remoteActionExecutor: remoteActionExecutor,
                powerController: powerController,
                awakeService: null,
                workstationLockPolicy: null,
                appLog: appLog,
                appLaunchService: appLaunchService,
                customScreenService: null,
                urlOpenService: urlOpenService,
                textDestinationService: textDestinationService,
                clipboardTextReader: clipboardTextReader,
                applyCustomPointer: null,
                applyPresentationLaserPointer: null,
                powerPointAutomation: powerPointAutomation,
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer(),
                screenViewCapture: screenViewCapture,
                phoneWebcamFeature: phoneWebcamFeature,
                phoneWebcamPeerFactory: phoneWebcamPeerFactory,
                screenViewPeerFactory: screenViewPeerFactory,
                usageTelemetry: usageTelemetry);
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

    protected sealed class FakePowerPointAutomationService(
        PowerPointAutomationSnapshot snapshot) : IPowerPointAutomationService
    {
        public List<PowerPointCommand> Commands { get; } = [];
        public Func<PowerPointCommand, PowerPointAutomationResult>? ExecuteHandler { get; set; }
        public Func<PowerPointCommand, CancellationToken, Task<PowerPointAutomationResult>>? ExecuteAsyncHandler { get; set; }
        public PowerPointAutomationSnapshot Snapshot { get; private set; } = snapshot;
        private event EventHandler? SnapshotChangedCore;
        public int SnapshotSubscriberCount { get; private set; }
        public event EventHandler? SnapshotChanged
        {
            add
            {
                SnapshotChangedCore += value;
                SnapshotSubscriberCount++;
            }
            remove
            {
                SnapshotChangedCore -= value;
                SnapshotSubscriberCount--;
            }
        }

        public Task<PowerPointAutomationResult> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PowerPointAutomationResult(
                true,
                null,
                "Refreshed.",
                Snapshot));

        public Task<PowerPointAutomationResult> ExecuteAsync(
            PowerPointCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (ExecuteAsyncHandler is not null)
            {
                return ExecuteAsyncHandler(command, cancellationToken);
            }

            if (ExecuteHandler is not null)
            {
                return Task.FromResult(ExecuteHandler(command));
            }

            var presentation = Snapshot.Presentations.FirstOrDefault(item =>
                command.RuntimePresentationId is null ||
                item.RuntimePresentationId == command.RuntimePresentationId);
            return Task.FromResult(presentation is null
                ? new PowerPointAutomationResult(
                    false,
                    "powerpoint-target-stale",
                    "Stale.",
                    Snapshot)
                : new PowerPointAutomationResult(
                    true,
                    null,
                    "Done.",
                    Snapshot,
                    presentation));
        }

        public void Publish(PowerPointAutomationSnapshot next)
        {
            Snapshot = next;
            SnapshotChangedCore?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    protected sealed class FakeTextDestinationService(TextDestinationMetadata metadata, TextDeliveryResult result) : ITextDestinationService
    {
        public List<(string Text, bool SendEnter, bool AllowHostApplicationControl)> Deliveries { get; } = [];
        public TextDestinationMetadata GetMetadata() => metadata;
        public Task<TextDeliveryResult> DeliverAsync(
            string text,
            bool sendEnter,
            bool allowHostApplicationControl,
            CancellationToken cancellationToken)
        {
            Deliveries.Add((text, sendEnter, allowHostApplicationControl));
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
        AppLaunchExecutionResult result,
        IReadOnlyList<KnownAppProfileSummary>? knownApplications = null) : IAppLaunchService
    {
        public List<string> ActionIds { get; } = new();

        public IReadOnlyList<AppLaunchActionSummary> GetActions() => actions;

        public AppLaunchExecutionResult Execute(string actionId)
        {
            ActionIds.Add(actionId);
            return result;
        }

        public AppLaunchExecutionResult ExecuteKnown(string profileId)
        {
            ActionIds.Add(profileId);
            return result;
        }

        public AppLaunchExecutionResult ExecutePowerPointFile(string path) => result;

        public IReadOnlyList<KnownAppProfileSummary> GetKnownApplications() =>
            knownApplications ??
            [.. KnownAppProfiles.All.Select(profile =>
                new KnownAppProfileSummary(profile.Id, profile.Label, true))];
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
