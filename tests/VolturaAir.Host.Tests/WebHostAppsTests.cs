using VolturaAir.Host;
using VolturaAir.Host.Features.Apps;
using System.Text;
using System.Threading.Channels;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostAppsTests : WebHostServiceTestBase
{
    [Fact]
    public async Task AuthorizedListUsesOpaqueIdentifiersAndRevalidatesActions()
    {
        var adapter = new FakeAppsWindowAdapter();
        await using var fixture = await WebHostFixture.StartAsync(appsWindowAdapter: adapter);
        const string clientId = "apps-phone";
        using var socket = await PairAsync(fixture, clientId);
        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.RemoteControls));
        using var pushedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        var capability = pushedStatus.RootElement.GetProperty("capabilities").GetProperty("apps");
        Assert.True(capability.GetProperty("permissionGranted").GetBoolean());
        Assert.True(capability.GetProperty("canUse").GetBoolean());
        Assert.False(capability.GetProperty("previewAvailable").GetBoolean());

        var list = await SendAndReceiveAsync(socket, new { type = "apps.list", operationId = "apps-list-1" });

        Assert.True(list.GetProperty("succeeded").GetBoolean());
        string revision = list.GetProperty("revision").GetString()!;
        var window = Assert.Single(list.GetProperty("windows").EnumerateArray());
        string windowId = window.GetProperty("windowId").GetString()!;
        Assert.Matches("^[a-f0-9]{32}$", revision);
        Assert.Matches("^[a-f0-9]{32}$", windowId);
        string json = list.GetRawText();
        Assert.DoesNotContain("handle", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(adapter.LastIncludeVolturaAir);

        var activated = await SendAndReceiveAsync(socket, new
        {
            type = "apps.activate",
            operationId = "apps-activate-1",
            revision,
            windowId
        });

        Assert.True(activated.GetProperty("succeeded").GetBoolean());
        Assert.Equal(new nint(1234), adapter.ActivatedHandle);

        var refreshed = await SendAndReceiveAsync(socket, new
        {
            type = "apps.list",
            operationId = "apps-list-2"
        });
        Assert.NotEqual(revision, refreshed.GetProperty("revision").GetString());
        Assert.Equal(
            windowId,
            Assert.Single(refreshed.GetProperty("windows").EnumerateArray())
                .GetProperty("windowId")
                .GetString());

        var stale = await SendAndReceiveAsync(socket, new
        {
            type = "apps.close",
            operationId = "apps-close-stale",
            revision = "00000000000000000000000000000000",
            windowId
        });
        Assert.False(stale.GetProperty("succeeded").GetBoolean());
        Assert.Equal("stale-window", stale.GetProperty("code").GetString());
        Assert.Null(adapter.ClosedHandle);
    }

    [Fact]
    public async Task BlockedListDoesNotEnumerateWindows()
    {
        var adapter = new FakeAppsWindowAdapter();
        await using var fixture = await WebHostFixture.StartAsync(appsWindowAdapter: adapter);
        const string clientId = "blocked-apps-phone";
        using var socket = await PairAsync(fixture, clientId);
        Assert.True(fixture.Manager.SetDevicePermission(clientId, DevicePermissionKind.AppsControl, false));
        using var pushedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.False(pushedStatus.RootElement.GetProperty("capabilities").GetProperty("apps").GetProperty("canUse").GetBoolean());

        var result = await SendAndReceiveAsync(socket, new { type = "apps.list", operationId = "apps-list-blocked" });

        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("permission-denied", result.GetProperty("code").GetString());
        Assert.Equal(0, adapter.DiscoveryCount);
    }

    [Fact]
    public async Task NativeActionFailureReturnsRecoverableResultWithoutClosingTheSession()
    {
        var adapter = new FakeAppsWindowAdapter { ThrowOnClose = true };
        await using var fixture = await WebHostFixture.StartAsync(appsWindowAdapter: adapter);
        const string clientId = "apps-action-failure-phone";
        using var socket = await PairAsync(fixture, clientId);
        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.RemoteControls));
        _ = await ReceiveTextAsync(socket);

        JsonElement list = await SendAndReceiveAsync(socket, new
        {
            type = "apps.list",
            operationId = "apps-list-before-action-failure"
        });
        string revision = list.GetProperty("revision").GetString()!;
        string windowId = list.GetProperty("windows")[0].GetProperty("windowId").GetString()!;
        JsonElement close = await SendAndReceiveAsync(socket, new
        {
            type = "apps.close",
            operationId = "apps-close-native-failure",
            revision,
            windowId
        });

        Assert.False(close.GetProperty("succeeded").GetBoolean());
        Assert.Equal("unavailable", close.GetProperty("code").GetString());
        JsonElement refreshed = await SendAndReceiveAsync(socket, new
        {
            type = "apps.list",
            operationId = "apps-list-after-action-failure"
        });
        Assert.True(refreshed.GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public async Task AuthorizedPreviewUsesItsOwnBoundedChannelAndStopsOnRequest()
    {
        var adapter = new FakeAppsWindowAdapter { PreviewContent = [0xff, 0xd8, 0xff, 0xd9] };
        var peerFactory = new FakeAppsPreviewPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            appsWindowAdapter: adapter,
            fileTransferPeerFactory: peerFactory);
        using var key = new PairingTestKey();
        const string clientId = "apps-preview-phone";
        using var socket = await PairAsync(fixture, clientId, key.PublicKey);
        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.RemoteControls));
        _ = await ReceiveTextAsync(socket);
        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.MyDevice));
        using var pushedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.True(pushedStatus.RootElement
            .GetProperty("capabilities")
            .GetProperty("apps")
            .GetProperty("previewAvailable")
            .GetBoolean());

        const string listOperationId = "apps-list-preview";
        JsonElement list = await SendAndReceiveAsync(socket, new
        {
            type = "apps.list",
            operationId = listOperationId
        });
        string revision = list.GetProperty("revision").GetString()!;
        string windowId = list.GetProperty("windows")[0].GetProperty("windowId").GetString()!;

        using var offerDocument = JsonDocument.Parse(await ReceiveTextAsync(socket));
        JsonElement offer = offerDocument.RootElement;
        Assert.Equal("apps.preview.offer", offer.GetProperty("type").GetString());
        string previewId = offer.GetProperty("previewId").GetString()!;
        string offerSdp = offer.GetProperty("offerSdp").GetString()!;
        const string answerOperationId = "apps-answer-preview";
        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=apps answer\r\nt=0 0\r\n";
        string answerTranscript =
            $"VolturaAir apps-preview:answer:v1\n{clientId}\n{fixture.Manager.HostIdentity.PublicKey}\n{listOperationId}\n{answerOperationId}\n{previewId}\n{FileTransferNegotiation.HashSdp(offerSdp)}\n{FileTransferNegotiation.HashSdp(answerSdp)}";
        JsonElement answer = await SendAndReceiveAsync(socket, new
        {
            type = "apps.preview.answer",
            operationId = answerOperationId,
            offerOperationId = listOperationId,
            previewId,
            answerSdp,
            clientSignature = key.SignPayload(answerTranscript)
        });
        Assert.True(answer.GetProperty("succeeded").GetBoolean());

        FileTransferPeerConfiguration configuration = Assert.IsType<FileTransferPeerConfiguration>(
            peerFactory.Configuration);
        Assert.Equal(AppsProtocol.DataChannelLabel, configuration.DataChannelLabel);
        Assert.Equal(AppsProtocol.MinimumRecordBytes, configuration.MinimumRecordBytes);
        Assert.Equal(AppsProtocol.MaximumRecordBytes, configuration.MaximumRecordBytes);
        Assert.False(configuration.RelayOnly);

        await peerFactory.Peer.ReceiveAsync(CreatePreviewRequest(revision, windowId));
        await peerFactory.Peer.TwoRecordsSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(2, peerFactory.Peer.Sent.Count);

        await SendAsync(socket, new
        {
            type = "apps.preview.stop",
            operationId = "apps-stop-preview",
            previewId
        });
        await peerFactory.Peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task<WebSocket> PairAsync(
        WebHostFixture fixture,
        string clientId,
        string? reconnectPublicKey = null)
    {
        var socket = await ConnectAsync(fixture.WebHost);
        await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Private Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = reconnectPublicKey ?? PairingTestKey.PublicKeyForFreshPairing
        });
        return socket;
    }

    private static byte[] CreatePreviewRequest(string revision, string windowId)
    {
        var request = new byte[66];
        request[0] = 0x11;
        Encoding.ASCII.GetBytes(revision, request.AsSpan(1, 32));
        request[33] = 1;
        Encoding.ASCII.GetBytes(windowId, request.AsSpan(34, 32));
        return request;
    }

    private sealed class FakeAppsWindowAdapter : IAppsWindowAdapter
    {
        public int DiscoveryCount { get; private set; }
        public bool LastIncludeVolturaAir { get; private set; }
        public nint? ActivatedHandle { get; private set; }
        public nint? ClosedHandle { get; private set; }
        public byte[]? PreviewContent { get; init; }
        public bool ThrowOnClose { get; init; }
        public int CaptureCount { get; private set; }

        public AppsWindowDiscoveryResult Discover(bool includeVolturaAir)
        {
            DiscoveryCount++;
            LastIncludeVolturaAir = includeVolturaAir;
            return new(true, "accepted", "Open applications loaded.", [
                new AppsWindowSnapshot(new nint(1234), "Draft", "Notepad", true, false, true, true)
            ]);
        }

        public bool IsUsable(nint windowHandle, bool includeVolturaAir) => windowHandle == new nint(1234);

        public AppsWindowActionResult Activate(nint windowHandle, bool includeVolturaAir)
        {
            ActivatedHandle = windowHandle;
            return new(true, "accepted", "Application activated.");
        }

        public AppsWindowActionResult Close(nint windowHandle, bool includeVolturaAir)
        {
            if (ThrowOnClose)
            {
                throw new InvalidOperationException("Controlled native action failure.");
            }
            ClosedHandle = windowHandle;
            return new(true, "close-requested", "Close requested.");
        }

        public AppsPreviewCaptureResult CapturePreview(
            nint windowHandle,
            bool includeVolturaAir,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return PreviewContent is null
                ? new(false, null, 0, 0)
                : new(true, PreviewContent, 2, 2);
        }
    }

    private sealed class FakeAppsPreviewPeerFactory : IFileTransferWebRtcPeerFactory
    {
        public FakeAppsPreviewPeer Peer { get; } = new();
        public FileTransferPeerConfiguration? Configuration { get; private set; }

        public IFileTransferWebRtcPeer Create(FileTransferPeerConfiguration? configuration)
        {
            Configuration = configuration;
            return Peer;
        }
    }

    private sealed class FakeAppsPreviewPeer : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _received = Channel.CreateUnbounded<byte[]>();
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Opened => _opened.Task;
        public ChannelReader<byte[]> Messages => _received.Reader;
        public List<byte[]> Sent { get; } = [];
        public TaskCompletionSource TwoRecordsSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) =>
            Task.FromResult("v=0\r\no=host 1 1 IN IP4 127.0.0.1\r\ns=apps offer\r\nt=0 0\r\n");

        public void ApplyAnswer(string answerSdp) => _opened.TrySetResult();

        public bool TrySend(byte[] record)
        {
            Sent.Add(record);
            if (Sent.Count >= 2)
            {
                TwoRecordsSent.TrySetResult();
            }
            return true;
        }

        public ValueTask ReceiveAsync(byte[] record) => _received.Writer.WriteAsync(record);

        public ValueTask DisposeAsync()
        {
            _received.Writer.TryComplete();
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
