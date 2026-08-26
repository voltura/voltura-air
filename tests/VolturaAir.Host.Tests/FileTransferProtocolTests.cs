using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Threading.Channels;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class FileTransferProtocolTests
{
    [Fact]
    public void DataAndAcknowledgementRecordsUseBoundedBigEndianOffsets()
    {
        var payload = new byte[] { 1, 2, 3 };
        var data = FileTransferProtocol.CreateData(0x01020304050607, payload);
        var acknowledgement = FileTransferProtocol.CreateAcknowledgement(3);

        Assert.Equal(0x11, data[0]);
        Assert.Equal(0x01020304050607UL, BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(1, 8)));
        Assert.True(FileTransferProtocol.TryParse(data, out var parsedData));
        Assert.Equal(FileTransferRecordKind.Data, parsedData.Kind);
        Assert.Equal(payload, parsedData.Payload.ToArray());
        Assert.True(FileTransferProtocol.TryParse(acknowledgement, out var parsedAcknowledgement));
        Assert.Equal(FileTransferRecordKind.Acknowledgement, parsedAcknowledgement.Kind);
        Assert.Equal(3, parsedAcknowledgement.Offset);
        Assert.Empty(parsedAcknowledgement.Payload.ToArray());
    }

    [Fact]
    public void RecordParserRejectsInvalidVersionShapeAndUnsafeOffset()
    {
        var emptyData = new byte[FileTransferProtocol.HeaderBytes];
        emptyData[0] = 0x11;
        var acknowledgementWithPayload = new byte[FileTransferProtocol.HeaderBytes + 1];
        acknowledgementWithPayload[0] = 0x12;
        var wrongVersion = FileTransferProtocol.CreateAcknowledgement(0);
        wrongVersion[0] = 0x22;
        var unsafeOffset = FileTransferProtocol.CreateAcknowledgement(0);
        BinaryPrimitives.WriteUInt64BigEndian(unsafeOffset.AsSpan(1, 8), ulong.MaxValue);

        Assert.False(FileTransferProtocol.TryParse(emptyData, out _));
        Assert.False(FileTransferProtocol.TryParse(acknowledgementWithPayload, out _));
        Assert.False(FileTransferProtocol.TryParse(wrongVersion, out _));
        Assert.False(FileTransferProtocol.TryParse(unsafeOffset, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileTransferProtocol.CreateData(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileTransferProtocol.CreateData(0, new byte[FileTransferProtocol.MaximumPayloadBytes + 1]));
    }

    [Fact]
    public void SignedTranscriptsBindDirectionMetadataAndBothSdpHashesExactly()
    {
        Assert.Equal(
            "VolturaAir file-transfer:start:v1\nclient\nhost-key\nrequest\nupload\nsession\nleft\nrevision\n\nreport.txt\n0",
            FileTransferNegotiation.StartTranscript("client", "host-key", "request", "upload", "session", "left", "revision", "", "report.txt", 0));
        Assert.Equal(
            "VolturaAir screen-capture-transfer:start:v1\nclient\nhost-key\nrequest\nscreen-request\ndisplay-1-1",
            FileTransferNegotiation.ScreenCaptureStartTranscript("client", "host-key", "request", "screen-request", "display-1-1"));
        Assert.Equal(
            "VolturaAir file-transfer:offer:v1\nclient\nhost-key\nrequest\ntransfer\ndownload\nreport.txt\n12\noffer-hash",
            FileTransferNegotiation.OfferTranscript("client", "host-key", "request", "transfer", "download", "report.txt", 12, "offer-hash"));
        Assert.Equal(
            "VolturaAir file-transfer:answer:v1\nclient\nhost-key\nrequest\ntransfer\ndownload\nreport.txt\n12\noffer-hash\nanswer-hash",
            FileTransferNegotiation.AnswerTranscript("client", "host-key", "request", "transfer", "download", "report.txt", 12, "offer-hash", "answer-hash"));
        Assert.Equal(TimeSpan.FromSeconds(20), FileTransferProtocol.SignalingLifetime);
        Assert.Equal(TimeSpan.FromSeconds(60), FileTransferProtocol.InactivityTimeout);
        Assert.Equal(64 * 1024, FileTransferProtocol.MaximumPayloadBytes);
        Assert.Equal(1024 * 1024, FileTransferProtocol.MaximumUnacknowledgedBytes);
    }

    [Theory]
    [InlineData(849_000_000_000, 850_000_000_000, 1000, false)]
    [InlineData(850_000_000_000, 850_000_000_000, 1, true)]
    [InlineData(849_999_000_000, 850_000_000_000, 1, true)]
    [InlineData(0, 850_000_000_000, long.MaxValue, true)]
    public void ScreenCaptureRelayAdmissionUsesBoundedProjectedUsage(long usage, long cutoff, long payload, bool rejected)
    {
        Assert.Equal(rejected, FileTransferNegotiation.WouldReachRelayCutoff(usage, cutoff, payload));
        Assert.False(FileTransferNegotiation.WouldReachRelayCutoff(usage, null, payload));
    }

    [Fact]
    public async Task OfficialRelayQuotaRejectionUsesTheScreenshotFailureBeforeCreatingAPeer()
    {
        await using var peer = new TrackingDisposePeer();
        var transfer = new FileTransferSession(
            "transfer", "client", "request", null!, "download", "capture.png", 12,
            FileTransferSourceKind.ScreenCapture);

        await Assert.ThrowsAsync<FileTransferWebRtcException>(() => FileTransferNegotiation.RunAsync(
            transfer,
            true,
            _ => Task.FromException<RelayTurnConfiguration?>(new RelayQuotaReachedException()),
            new SinglePeerFactory(peer),
            null!,
            null!,
            _ => Task.CompletedTask,
            CancellationToken.None));

        Assert.Equal("relay-quota-projected", transfer.FailureCode);
        Assert.Equal("Screenshot not sent because it would reach the monthly Relay usage limit.", transfer.FailureMessage);
        Assert.Null(transfer.Peer);
        transfer.Cancellation.Dispose();
    }

    [Fact]
    public async Task OfficialRelayQuotaRejectionDoesNotApplyScreenshotCopyToFilesTransfers()
    {
        await using var peer = new TrackingDisposePeer();
        var transfer = new FileTransferSession(
            "transfer", "client", "request", null!, "download", "report.txt", 12,
            FileTransferSourceKind.FileEntry);

        await Assert.ThrowsAsync<FileTransferWebRtcException>(() => FileTransferNegotiation.RunAsync(
            transfer,
            true,
            _ => Task.FromException<RelayTurnConfiguration?>(new RelayQuotaReachedException()),
            new SinglePeerFactory(peer),
            null!,
            null!,
            _ => Task.CompletedTask,
            CancellationToken.None));

        Assert.Null(transfer.FailureCode);
        Assert.Null(transfer.FailureMessage);
        Assert.Null(transfer.Peer);
        transfer.Cancellation.Dispose();
    }

    [Fact]
    public async Task NegotiationDeadlineIsReportedAsOfferExpired()
    {
        await using var peer = new ExpiringPeer();
        var transfer = new FileTransferSession("transfer", "client", "request", null!, "download", "report.txt", 12);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileTransferNegotiation.RunAsync(
            transfer, false, _ => Task.FromResult<RelayTurnConfiguration?>(null), new SinglePeerFactory(peer), null!, null!,
            _ => Task.CompletedTask, CancellationToken.None, TimeSpan.FromMilliseconds(10)));

        Assert.Equal("offer-expired", transfer.FailureCode);
        Assert.Equal("The file-transfer offer expired.", transfer.FailureMessage);
        transfer.Cancellation.Dispose();
    }

    [Fact]
    public async Task EmptyFilesCompleteWithoutADataRecord()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var source = new FileTransferDownloadSource("empty.txt", 0, File.OpenRead(path));
            await using var peer = new PumpPeer(0);

            await FileTransferDataPump.SendAsync(source, 0, peer, () => { }, (_, _, _) => Task.CompletedTask, CancellationToken.None);
            await FileTransferDataPump.ReceiveAsync(Stream.Null, 0, peer, _ => { }, () => { }, (_, _, _) => Task.CompletedTask, CancellationToken.None);

            var sent = Assert.Single(peer.Sent);
            Assert.True(FileTransferProtocol.TryParse(sent, out var acknowledgement));
            Assert.Equal(FileTransferRecordKind.Acknowledgement, acknowledgement.Kind);
            Assert.Equal(0, acknowledgement.Offset);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DownloadHonorsBackpressureAndTheOneMegabyteUnacknowledgedWindow()
    {
        var path = Path.GetTempFileName();
        var bytes = new byte[FileTransferProtocol.MaximumUnacknowledgedBytes + 1];
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            await using var source = new FileTransferDownloadSource("large.bin", bytes.Length, File.OpenRead(path));
            await using var peer = new PumpPeer(bytes.Length, rejectFirstSend: true);

            await FileTransferDataPump.SendAsync(source, bytes.Length, peer, () => { }, (_, _, _) => Task.CompletedTask, CancellationToken.None);

            Assert.True(peer.RejectedSendWasRetried);
            Assert.Equal(17, peer.Sent.Count);
            Assert.Equal(16, peer.RecordsBeforeFirstAcknowledgement);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DownloadRejectsANonAdvancingAcknowledgement()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            await using var source = new FileTransferDownloadSource("one.bin", 1, File.OpenRead(path));
            await using var peer = new PumpPeer(1, duplicateAcknowledgement: true);

            await Assert.ThrowsAsync<IOException>(() => FileTransferDataPump.SendAsync(
                source, 1, peer, () => { }, (_, _, _) => Task.CompletedTask, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DataChannelLossStopsBothSendDirectionsImmediately()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            await using var source = new FileTransferDownloadSource("one.bin", 1, File.OpenRead(path));
            await using var downloadPeer = new FailingSendPeer();
            await Assert.ThrowsAsync<FileTransferWebRtcException>(() => FileTransferDataPump.SendAsync(
                source, 1, downloadPeer, () => { }, (_, _, _) => Task.CompletedTask, CancellationToken.None));

            await using var uploadPeer = new FailingSendPeer();
            await Assert.ThrowsAsync<FileTransferWebRtcException>(() => FileTransferDataPump.ReceiveAsync(
                Stream.Null, 0, uploadPeer, _ => { }, () => { }, (_, _, _) => Task.CompletedTask, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PendingAndEstablishedCancellationStayIdempotentAcrossDisposal()
    {
        var pending = new FileTransferPendingStart("client", "upload", null!, new CancellationTokenSource());
        Assert.Equal("client", pending.ClientId);
        Assert.Equal("upload", pending.Direction);
        Assert.True(pending.TryCancel("canceled", "File transfer canceled."));
        Assert.Equal(("canceled", "File transfer canceled."), pending.Failure);
        pending.Dispose();
        Assert.False(pending.TryCancel("connection-lost", "Connection lost."));

        var transfer = new FileTransferSession("transfer", "client", "request", null!, "download", "report.txt", 1);
        Assert.True(transfer.TryCancel("canceled", "File transfer canceled."));
        lock (transfer.Gate)
        {
            Volatile.Write(ref transfer.DisposeStarted, 1);
            transfer.Cancellation.Dispose();
        }
        Assert.False(transfer.TryCancel("connection-lost", "Connection lost."));
        Assert.Equal("canceled", transfer.FailureCode);
    }

    [Fact]
    public async Task UploadWrapsControlSocketFailureAndShutdownAlwaysDisposesFaultedTransfers()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaAir.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var pairingStore = new TempPairingStore();
        var pairing = new PairingManager(pairingStore.Store);
        await using var files = new FileManagerService(initialLeftPath: root, initialRightPath: root);
        using var transport = new WebSocketTransport();
        using var socket = new ThrowingSendWebSocket(static () => new WebSocketException());
        transport.Register("client", socket);
        await using var peer = new PumpPeer(0);
        await using var runner = new FileTransferRunner(
            files, pairing, transport, false, _ => Task.FromResult<RelayTurnConfiguration?>(null),
            new SinglePeerFactory(peer), _ => { });
        var upload = new FileTransferSession("upload", "client", "request", socket, "upload", "empty.txt", 0);
        upload.StartPublished.SetResult();

        await Assert.ThrowsAsync<IOException>(() => runner.ReceiveUploadAsync(
            upload, Stream.Null, _ => { }, CancellationToken.None));

        var faulted = new FileTransferSession("faulted", "client", "request-2", socket, "download", "file.txt", 1)
        {
            RunTask = Task.FromException(new InvalidOperationException("Injected task fault."))
        };
        await runner.ShutdownAsync([faulted]);
        Assert.Equal(1, Volatile.Read(ref faulted.DisposeStarted));
        pairing.DisposeHostIdentity();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task UploadWrapsClosedSocketStateAndPeerDisposeFailureReleasesEveryOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaAir.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "source");
        using var pairingStore = new TempPairingStore();
        var pairing = new PairingManager(pairingStore.Store);
        await using var files = new FileManagerService(initialLeftPath: root, initialRightPath: root);
        using var transport = new WebSocketTransport();
        using var socket = new ThrowingSendWebSocket(static () => new InvalidOperationException("The WebSocket is closed."));
        transport.Register("client", socket);
        await using var peer = new PumpPeer(0);
        await using var runner = new FileTransferRunner(
            files, pairing, transport, false, _ => Task.FromResult<RelayTurnConfiguration?>(null),
            new SinglePeerFactory(peer), _ => { });
        var upload = new FileTransferSession("upload", "client", "request", socket, "upload", "empty.txt", 0);
        upload.StartPublished.SetResult();

        await Assert.ThrowsAsync<IOException>(() => runner.ReceiveUploadAsync(
            upload, Stream.Null, _ => { }, CancellationToken.None));

        Assert.True(runner.TryAcquireSlot());
        var downloadSource = new FileTransferDownloadSource("source.txt", 6, File.OpenRead(sourcePath));
        var cleanup = new FileTransferSession("cleanup", "client", "request-2", socket, "download", "source.txt", 6)
        {
            Peer = new ThrowingDisposePeer(),
            SlotHeld = true,
            DownloadSource = downloadSource,
            RunTask = Task.CompletedTask
        };
        await runner.ShutdownAsync([cleanup]);

        Assert.True(Assert.IsType<FileStream>(downloadSource.Stream).SafeFileHandle.IsClosed);
        Assert.Null(cleanup.DownloadSource);
        Assert.True(runner.TryAcquireSlot());
        runner.ReleaseSlot();

        var late = new FileTransferSession("late", "client", "request-late", socket, "upload", "late.txt", 0)
        {
            RunTask = Task.CompletedTask
        };
        await runner.ShutdownAsync([late]);
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.ReceiveUploadAsync(
            late, Stream.Null, _ => { }, CancellationToken.None));
        pairing.DisposeHostIdentity();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ActiveUploadWrapsPeerCleanupFailureAndLateNegotiationDisposesItsPeer()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaAir.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var pairingStore = new TempPairingStore();
        var pairing = new PairingManager(pairingStore.Store);
        await using var files = new FileManagerService(initialLeftPath: root, initialRightPath: root);
        using var transport = new WebSocketTransport();
        using var socket = new PassiveWebSocket();
        transport.Register("client", socket);
        var throwingPeer = new ThrowingDisposePeer(throwOnSend: true);
        await using var runner = new FileTransferRunner(
            files, pairing, transport, false, _ => Task.FromResult<RelayTurnConfiguration?>(null),
            new SinglePeerFactory(throwingPeer), _ => { });
        var upload = new FileTransferSession("upload", "client", "request", socket, "upload", "empty.txt", 0);
        upload.StartPublished.SetResult();
        upload.AnswerApplied.SetResult();

        var combinedFailure = await Assert.ThrowsAsync<IOException>(() => runner.ReceiveUploadAsync(
            upload, Stream.Null, _ => { }, CancellationToken.None));
        Assert.IsType<AggregateException>(combinedFailure.InnerException);
        Assert.True(runner.TryAcquireSlot());
        runner.ReleaseSlot();

        var trackingPeer = new TrackingDisposePeer();
        var late = new FileTransferSession("late", "client", "late-request", socket, "download", "late.txt", 0);
        Volatile.Write(ref late.DisposeStarted, 1);
        await Assert.ThrowsAsync<OperationCanceledException>(() => FileTransferNegotiation.RunAsync(
            late, false, _ => Task.FromResult<RelayTurnConfiguration?>(null), new SinglePeerFactory(trackingPeer),
            pairing, transport, _ => Task.CompletedTask, CancellationToken.None));
        Assert.True(trackingPeer.Disposed);
        late.Cancellation.Dispose();
        pairing.DisposeHostIdentity();
        Directory.Delete(root, recursive: true);
    }

    private sealed class PumpPeer(long declaredSize, bool rejectFirstSend = false, bool duplicateAcknowledgement = false) : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        private bool _rejectNext = rejectFirstSend;
        private long _accepted;
        private bool _firstAcknowledgementWritten;
        public List<byte[]> Sent { get; } = [];
        public bool RejectedSendWasRetried { get; private set; }
        public int RecordsBeforeFirstAcknowledgement { get; private set; }
        public Task Opened => Task.CompletedTask;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("offer");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySend(byte[] record)
        {
            if (_rejectNext)
            {
                _rejectNext = false;
                RejectedSendWasRetried = true;
                return false;
            }
            Sent.Add(record);
            if (!FileTransferProtocol.TryParse(record, out var parsed) || parsed.Kind != FileTransferRecordKind.Data) return true;
            if (duplicateAcknowledgement)
            {
                _messages.Writer.TryWrite(FileTransferProtocol.CreateAcknowledgement(0));
                return true;
            }
            _accepted += parsed.Payload.Length;
            if (!_firstAcknowledgementWritten && _accepted == FileTransferProtocol.MaximumUnacknowledgedBytes)
            {
                RecordsBeforeFirstAcknowledgement = Sent.Count;
                _firstAcknowledgementWritten = true;
                _messages.Writer.TryWrite(FileTransferProtocol.CreateAcknowledgement(_accepted));
            }
            else if (_accepted == declaredSize)
            {
                _messages.Writer.TryWrite(FileTransferProtocol.CreateAcknowledgement(_accepted));
            }
            return true;
        }
        public ValueTask DisposeAsync()
        {
            _messages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSendPeer : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        public Task Opened => Task.CompletedTask;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("offer");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySend(byte[] record) => throw new FileTransferWebRtcException("The file-transfer connection stopped.");
        public ValueTask DisposeAsync() { _messages.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private sealed class ExpiringPeer : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        public Task Opened => Task.CompletedTask;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "offer";
        }
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySend(byte[] record) => true;
        public ValueTask DisposeAsync() { _messages.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private sealed class ThrowingDisposePeer(bool throwOnSend = false) : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        public Task Opened => Task.CompletedTask;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("offer");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySend(byte[] record) => throwOnSend
            ? throw new FileTransferWebRtcException("Injected data-channel failure.")
            : true;
        public ValueTask DisposeAsync() => ValueTask.FromException(new IOException("Injected peer dispose failure."));
    }

    private sealed class TrackingDisposePeer : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        public bool Disposed { get; private set; }
        public Task Opened => Task.CompletedTask;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("offer");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySend(byte[] record) => true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class SinglePeerFactory(IFileTransferWebRtcPeer peer) : IFileTransferWebRtcPeerFactory
    {
        public IFileTransferWebRtcPeer Create(FileTransferPeerConfiguration? configuration) => peer;
    }

    private sealed class ThrowingSendWebSocket(Func<Exception> exception) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromException<WebSocketReceiveResult>(exception());
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.FromException(exception());
    }

    private sealed class PassiveWebSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromException<WebSocketReceiveResult>(new NotSupportedException());
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
