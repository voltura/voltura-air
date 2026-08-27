using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostTerminalReconnectTests : WebHostServiceTestBase
{
    [Fact]
    public async Task SameDeviceResumeReplaysExactUnacknowledgedOutput()
    {
        var processes = new ReconnectProcessFactory();
        var peers = new ReconnectPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processes,
            terminalPeerFactory: peers);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key, "terminal-owner");

        (string terminalId, ReconnectPeer firstPeer) = await StartAndAnswerAsync(
            socket, fixture.Manager, key, "terminal-owner", "resume-start", peers);
        await Assert.Single(processes.Processes).WriteOutputAsync([0x61, 0x62, 0x63]);
        await WaitUntilAsync(() => firstPeer.Sent.Count == 1);
        firstPeer.Disconnect();
        await WaitUntilAsync(() => firstPeer.Disposed);

        const string attachOperation = "resume-attach";
        await SendAsync(socket, new
        {
            type = "terminal.attach",
            operationId = attachOperation,
            terminalId,
            acknowledgedOffset = 0,
            columns = 90,
            rows = 30,
            clientSignature = key.SignPayload(TerminalNegotiation.AttachTranscript(
                "terminal-owner", fixture.Manager.HostIdentity.PublicKey, attachOperation,
                terminalId, 0, 90, 30))
        });
        Assert.True((await ReceiveUntilTypeAsync(socket, "terminal.attach.result")).GetProperty("succeeded").GetBoolean());
        JsonElement offer = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        ReconnectPeer resumedPeer = peers.Peers[1];
        await AnswerAsync(socket, fixture.Manager, key, "terminal-owner", attachOperation,
            "resume-answer", terminalId, offer, resumedPeer);

        await WaitUntilAsync(() => resumedPeer.Sent.Count == 1);
        Assert.True(TerminalProtocol.TryParse(resumedPeer.Sent[0], out var replay));
        Assert.Equal(0, replay.Offset);
        Assert.Equal([0x61, 0x62, 0x63], replay.Payload.ToArray());
        Assert.Equal((90, 30), Assert.Single(processes.Processes).LastSize);
    }

    [Fact]
    public async Task WrongDeviceCannotAttachAndOwnerCanStopWhileDetached()
    {
        var processes = new ReconnectProcessFactory();
        var peers = new ReconnectPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processes,
            terminalPeerFactory: peers);
        using var ownerKey = new PairingTestKey();
        using var otherKey = new PairingTestKey();
        using WebSocket owner = await ConnectAsync(fixture.WebHost);
        using WebSocket other = await ConnectAsync(fixture.WebHost);
        await PairAsync(owner, fixture.Manager, ownerKey, "terminal-owner");
        await PairAsync(other, fixture.Manager, otherKey, "terminal-other");
        fixture.Manager.SetDevicePermission("terminal-other", DevicePermissionKind.Terminal, true);

        (string terminalId, ReconnectPeer peer) = await StartAndAnswerAsync(
            owner, fixture.Manager, ownerKey, "terminal-owner", "owner-start", peers);
        peer.Disconnect();
        await WaitUntilAsync(() => peer.Disposed);

        const string attachOperation = "other-attach";
        JsonElement rejected = await SendUntilTypeAsync(other, new
        {
            type = "terminal.attach",
            operationId = attachOperation,
            terminalId,
            acknowledgedOffset = 0,
            columns = 80,
            rows = 24,
            clientSignature = otherKey.SignPayload(TerminalNegotiation.AttachTranscript(
                "terminal-other", fixture.Manager.HostIdentity.PublicKey, attachOperation,
                terminalId, 0, 80, 24))
        }, "terminal.attach.result");
        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("terminal-unavailable", rejected.GetProperty("code").GetString());
        Assert.Single(peers.Peers);

        JsonElement stopped = await SendUntilTypeAsync(owner, new
        {
            type = "terminal.stop",
            operationId = "owner-stop-detached",
            terminalId
        }, "terminal.stop.result");
        Assert.True(stopped.GetProperty("succeeded").GetBoolean());
        Assert.True(Assert.Single(processes.Processes).Terminated);
    }

    [Fact]
    public async Task DetachedTerminalExpiresAfterTheDeterministicReconnectWindow()
    {
        var time = new ManualTerminalTimeProvider();
        var processes = new ReconnectProcessFactory();
        var peers = new ReconnectPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processes,
            terminalPeerFactory: peers,
            terminalTimeProvider: time);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key, "terminal-owner");

        (_, ReconnectPeer peer) = await StartAndAnswerAsync(
            socket, fixture.Manager, key, "terminal-owner", "expiry-start", peers);
        peer.Disconnect();
        await WaitUntilAsync(() => peer.Disposed && time.PendingTimerCount == 1);
        time.Advance(TerminalProtocol.ReconnectLifetime - TimeSpan.FromMilliseconds(1));
        Assert.False(Assert.Single(processes.Processes).Terminated);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntilAsync(() => Assert.Single(processes.Processes).Terminated);
    }

    [Fact]
    public async Task UnansweredOfferEndsAtTheDeterministicSignalingDeadline()
    {
        var time = new ManualTerminalTimeProvider();
        var processes = new ReconnectProcessFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processes,
            terminalPeerFactory: new ReconnectPeerFactory(),
            terminalTimeProvider: time);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key, "terminal-owner");

        const string operationId = "unanswered-start";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "terminal-owner", fixture.Manager.HostIdentity.PublicKey, operationId, 80, 24))
        }, "terminal.start.result");
        Assert.True(started.GetProperty("succeeded").GetBoolean());
        _ = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        await WaitUntilAsync(() => time.PendingTimerCount == 1);

        time.Advance(TerminalProtocol.SignalingLifetime);
        await WaitUntilAsync(() => Assert.Single(processes.Processes).Terminated);
        JsonElement ended = await ReceiveUntilTypeAsync(socket, "terminal.ended");
        Assert.Equal("negotiation-timeout", ended.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ConcurrentAttachesSerializeAndOnlyTheLatestOfferCanBeAnswered()
    {
        var processes = new ReconnectProcessFactory();
        var peers = new ReconnectPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processes,
            terminalPeerFactory: peers);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key, "terminal-owner");
        (string terminalId, ReconnectPeer peer) = await StartAndAnswerAsync(
            socket, fixture.Manager, key, "terminal-owner", "concurrent-start", peers);
        peer.Disconnect();
        await WaitUntilAsync(() => peer.Disposed);

        foreach (string operationId in new[] { "concurrent-attach-1", "concurrent-attach-2" })
        {
            await SendAsync(socket, new
            {
                type = "terminal.attach",
                operationId,
                terminalId,
                acknowledgedOffset = 0,
                columns = 80,
                rows = 24,
                clientSignature = key.SignPayload(TerminalNegotiation.AttachTranscript(
                    "terminal-owner", fixture.Manager.HostIdentity.PublicKey, operationId,
                    terminalId, 0, 80, 24))
            });
        }

        var offers = new List<JsonElement>();
        int successfulResults = 0;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            while (offers.Count < 2 || successfulResults < 2)
            {
                using var document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
                string? type = document.RootElement.GetProperty("type").GetString();
                if (type == "terminal.offer") offers.Add(document.RootElement.Clone());
                else if (type == "terminal.attach.result" && document.RootElement.GetProperty("succeeded").GetBoolean()) successfulResults++;
            }
        }
        Assert.Equal(3, peers.Peers.Count);
        Assert.True(peers.Peers[1].Disposed);
        JsonElement staleOffer = offers[0];
        JsonElement latestOffer = offers[1];

        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=terminal answer\r\nt=0 0\r\n";
        const string staleAnswerOperation = "concurrent-stale-answer";
        string staleOfferOperation = staleOffer.GetProperty("operationId").GetString()!;
        JsonElement staleResult = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.answer",
            operationId = staleAnswerOperation,
            offerOperationId = staleOfferOperation,
            terminalId,
            answerSdp,
            clientSignature = key.SignPayload(TerminalNegotiation.AnswerTranscript(
                "terminal-owner", fixture.Manager.HostIdentity.PublicKey, staleOfferOperation,
                staleAnswerOperation, terminalId,
                TerminalNegotiation.HashSdp(staleOffer.GetProperty("offerSdp").GetString()!),
                TerminalNegotiation.HashSdp(answerSdp)))
        }, "terminal.answer.result");
        Assert.False(staleResult.GetProperty("succeeded").GetBoolean());
        Assert.Equal("offer-expired", staleResult.GetProperty("code").GetString());

        await AnswerAsync(socket, fixture.Manager, key, "terminal-owner",
            latestOffer.GetProperty("operationId").GetString()!, "concurrent-latest-answer",
            terminalId, latestOffer, peers.Peers[2]);
        Assert.False(Assert.Single(processes.Processes).Terminated);
    }

    private static async Task<(string TerminalId, ReconnectPeer Peer)> StartAndAnswerAsync(
        WebSocket socket,
        PairingManager manager,
        PairingTestKey key,
        string clientId,
        string operationId,
        ReconnectPeerFactory peers)
    {
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                clientId, manager.HostIdentity.PublicKey, operationId, 80, 24))
        }, "terminal.start.result");
        string terminalId = started.GetProperty("terminalId").GetString()!;
        JsonElement offer = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        ReconnectPeer peer = Assert.Single(peers.Peers);
        await AnswerAsync(socket, manager, key, clientId, operationId,
            $"{operationId}-answer", terminalId, offer, peer);
        return (terminalId, peer);
    }

    private static async Task AnswerAsync(
        WebSocket socket,
        PairingManager manager,
        PairingTestKey key,
        string clientId,
        string offerOperationId,
        string answerOperationId,
        string terminalId,
        JsonElement offer,
        ReconnectPeer peer)
    {
        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=terminal answer\r\nt=0 0\r\n";
        string proof = key.SignPayload(TerminalNegotiation.AnswerTranscript(
            clientId, manager.HostIdentity.PublicKey, offerOperationId, answerOperationId,
            terminalId, TerminalNegotiation.HashSdp(offer.GetProperty("offerSdp").GetString()!),
            TerminalNegotiation.HashSdp(answerSdp)));
        JsonElement answered = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.answer",
            operationId = answerOperationId,
            offerOperationId,
            terminalId,
            answerSdp,
            clientSignature = proof
        }, "terminal.answer.result");
        Assert.True(answered.GetProperty("succeeded").GetBoolean());
        Assert.True(peer.Opened.IsCompletedSuccessfully);
    }

    private static async Task PairAsync(
        WebSocket socket,
        PairingManager manager,
        PairingTestKey key,
        string clientId)
    {
        JsonElement accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = clientId,
            pairToken = manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
    }

    private static async Task<JsonElement> SendUntilTypeAsync(WebSocket socket, object payload, string type)
    {
        await SendAsync(socket, payload);
        return await ReceiveUntilTypeAsync(socket, type);
    }

    private static async Task<JsonElement> ReceiveUntilTypeAsync(WebSocket socket, string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        for (int attempt = 0; attempt < 12; attempt++)
        {
            using var document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            if (document.RootElement.GetProperty("type").GetString() == type)
                return document.RootElement.Clone();
        }
        throw new InvalidOperationException($"The host did not send {type}.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ReconnectProcessFactory : ITerminalProcessFactory
    {
        internal List<ReconnectProcess> Processes { get; } = [];
        public ITerminalProcess Start(ushort columns, ushort rows)
        {
            var process = new ReconnectProcess(columns, rows);
            Processes.Add(process);
            return process;
        }
    }

    private sealed class ReconnectProcess(ushort columns, ushort rows) : ITerminalProcess
    {
        private readonly Pipe _input = new();
        private readonly Pipe _output = new();
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Terminated { get; private set; }
        internal (int Columns, int Rows) LastSize { get; private set; } = (columns, rows);
        public Stream Input => _input.Writer.AsStream();
        public Stream Output => _output.Reader.AsStream();
        public Task<int> ExitCode => _exit.Task;
        public void Resize(ushort nextColumns, ushort nextRows) => LastSize = (nextColumns, nextRows);
        internal async Task WriteOutputAsync(byte[] bytes) => await _output.Writer.WriteAsync(bytes);
        public void Terminate()
        {
            if (Terminated) return;
            Terminated = true;
            _exit.TrySetResult(1);
            _input.Writer.Complete();
            _output.Writer.Complete();
        }
        public async ValueTask DisposeAsync()
        {
            Terminate();
            await _input.Reader.CompleteAsync();
            await _output.Reader.CompleteAsync();
        }
    }

    private sealed class ReconnectPeerFactory : ITerminalWebRtcPeerFactory
    {
        internal List<ReconnectPeer> Peers { get; } = [];
        public ITerminalWebRtcPeer Create(FileTransferPeerConfiguration? configuration)
        {
            var peer = new ReconnectPeer();
            Peers.Add(peer);
            return peer;
        }
    }

    private sealed class ReconnectPeer : ITerminalWebRtcPeer
    {
        private readonly Channel<byte[]> _received = Channel.CreateUnbounded<byte[]>();
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<byte[]> Sent { get; } = [];
        internal bool Disposed { get; private set; }
        public Task Opened => _opened.Task;
        public Task Closed => _closed.Task;
        public ChannelReader<byte[]> Messages => _received.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) =>
            Task.FromResult("v=0\r\no=host 1 1 IN IP4 127.0.0.1\r\ns=terminal offer\r\nt=0 0\r\n");
        public void ApplyAnswer(string answerSdp) => _opened.TrySetResult();
        public bool TrySend(byte[] record) { Sent.Add(record); return true; }
        internal void Disconnect() => _closed.TrySetResult();
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _received.Writer.TryComplete();
            _closed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTerminalTimeProvider : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);
        internal int PendingTimerCount { get { lock (_gate) return _timers.Count(timer => !timer.Disposed); } }
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, _now + dueTime);
            lock (_gate) _timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan duration)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _now += duration;
                due = _timers.Where(timer => !timer.Disposed && timer.DueAt <= _now).ToList();
            }
            foreach (ManualTimer timer in due) timer.Fire();
        }

        private sealed class ManualTimer(
            ManualTerminalTimeProvider owner,
            TimerCallback callback,
            object? state,
            DateTimeOffset dueAt) : ITimer
        {
            internal DateTimeOffset DueAt { get; private set; } = dueAt;
            internal bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Disposed) return false;
                DueAt = owner._now + dueTime;
                return true;
            }
            internal void Fire()
            {
                if (Disposed) return;
                Disposed = true;
                callback(state);
            }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
