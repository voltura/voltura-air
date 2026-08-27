using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostTerminalTests : WebHostServiceTestBase
{
    [Fact]
    public async Task AuthenticatedTerminalStreamsBytesAndStopsTheWholeProcessLifetime()
    {
        var processFactory = new FakeTerminalProcessFactory();
        var peerFactory = new FakeTerminalPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processFactory,
            terminalPeerFactory: peerFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string startOperation = "terminal-start-1";
        string startTranscript = TerminalNegotiation.StartTranscript(
            "client-terminal", fixture.Manager.HostIdentity.PublicKey, startOperation, 80, 24);
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId = startOperation,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(startTranscript)
        }, "terminal.start.result");
        Assert.True(started.GetProperty("succeeded").GetBoolean());
        string terminalId = started.GetProperty("terminalId").GetString()!;

        JsonElement offer = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        Assert.Equal(terminalId, offer.GetProperty("terminalId").GetString());
        Assert.Equal(80, offer.GetProperty("columns").GetInt32());
        Assert.NotEmpty(offer.GetProperty("hostSignature").GetString()!);

        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=terminal answer\r\nt=0 0\r\n";
        string answerTranscript = TerminalNegotiation.AnswerTranscript(
            "client-terminal",
            fixture.Manager.HostIdentity.PublicKey,
            startOperation,
            "terminal-answer-1",
            terminalId,
            TerminalNegotiation.HashSdp(offer.GetProperty("offerSdp").GetString()!),
            TerminalNegotiation.HashSdp(answerSdp));
        JsonElement answered = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.answer",
            operationId = "terminal-answer-1",
            offerOperationId = startOperation,
            terminalId,
            answerSdp,
            clientSignature = key.SignPayload(answerTranscript)
        }, "terminal.answer.result");
        Assert.True(answered.GetProperty("succeeded").GetBoolean());

        FakeTerminalProcess process = Assert.Single(processFactory.Processes);
        FakeTerminalPeer peer = Assert.Single(peerFactory.Peers);
        await peer.ReceiveAsync(TerminalProtocol.CreateInput([0xff, 0x00, 0x61]));
        await WaitUntilAsync(() => process.InputBytes.Count == 3);
        Assert.Equal([0xff, 0x00, 0x61], process.InputBytes.ToArray());

        await process.WriteOutputAsync([0xfe, 0x62]);
        await WaitUntilAsync(() => peer.Sent.Count > 0);
        Assert.True(TerminalProtocol.TryParse(peer.Sent[0], out var output));
        Assert.Equal(TerminalRecordKind.Output, output.Kind);
        Assert.Equal([0xfe, 0x62], output.Payload.ToArray());
        await peer.ReceiveAsync(TerminalProtocol.CreateAcknowledgement(output.Offset + output.Payload.Length));

        JsonElement stopped = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.stop",
            operationId = "terminal-stop-1",
            terminalId
        }, "terminal.stop.result");
        Assert.True(stopped.GetProperty("succeeded").GetBoolean());
        Assert.True(process.Terminated);
        Assert.True(peer.Disposed);
    }

    [Fact]
    public async Task PendingTerminalConnectionDoesNotBlockHealthCommands()
    {
        var peerFactory = new FakeTerminalPeerFactory { OpenOnAnswer = false };
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: new FakeTerminalProcessFactory(),
            terminalPeerFactory: peerFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string startOperation = "terminal-health-start";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId = startOperation,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, startOperation, 80, 24))
        }, "terminal.start.result");
        string terminalId = started.GetProperty("terminalId").GetString()!;
        JsonElement offer = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=terminal answer\r\nt=0 0\r\n";
        string answerTranscript = TerminalNegotiation.AnswerTranscript(
            "client-terminal",
            fixture.Manager.HostIdentity.PublicKey,
            startOperation,
            "terminal-health-answer",
            terminalId,
            TerminalNegotiation.HashSdp(offer.GetProperty("offerSdp").GetString()!),
            TerminalNegotiation.HashSdp(answerSdp));

        await SendAsync(socket, new
        {
            type = "terminal.answer",
            operationId = "terminal-health-answer",
            offerOperationId = startOperation,
            terminalId,
            answerSdp,
            clientSignature = key.SignPayload(answerTranscript)
        });
        await SendAsync(socket, new { type = "health.ping" });

        JsonElement pong = await ReceiveUntilTypeAsync(socket, "health.pong");
        Assert.Equal("health.pong", pong.GetProperty("type").GetString());
        FakeTerminalPeer peer = Assert.Single(peerFactory.Peers);
        Assert.False(peer.Opened.IsCompleted);

        peer.CompleteOpen();
        JsonElement answered = await ReceiveUntilTypeAsync(socket, "terminal.answer.result");
        Assert.True(answered.GetProperty("succeeded").GetBoolean());

        JsonElement stopped = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.stop",
            operationId = "terminal-health-stop",
            terminalId
        }, "terminal.stop.result");
        Assert.True(stopped.GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public async Task InvalidStartProofNeverCreatesAProcess()
    {
        var processFactory = new FakeTerminalProcessFactory();
        await using var fixture = await WebHostFixture.StartAsync(terminalProcessFactory: processFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        JsonElement rejected = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId = "terminal-invalid-proof",
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload("wrong transcript")
        }, "terminal.start.result");

        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("invalid-proof", rejected.GetProperty("code").GetString());
        Assert.Empty(processFactory.Processes);
    }

    [Fact]
    public async Task ConcurrentStartsCreateOnlyOneShell()
    {
        var processFactory = new FakeTerminalProcessFactory();
        await using var fixture = await WebHostFixture.StartAsync(terminalProcessFactory: processFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        await SendAsync(socket, new
        {
            type = "terminal.start",
            operationId = "terminal-concurrent-start-1",
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, "terminal-concurrent-start-1", 80, 24))
        });
        await SendAsync(socket, new
        {
            type = "terminal.start",
            operationId = "terminal-concurrent-start-2",
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, "terminal-concurrent-start-2", 80, 24))
        });

        JsonElement first = await ReceiveUntilTypeAsync(socket, "terminal.start.result");
        JsonElement second = await ReceiveUntilTypeAsync(socket, "terminal.start.result");
        JsonElement succeeded = first.GetProperty("succeeded").GetBoolean() ? first : second;
        JsonElement rejected = first.GetProperty("succeeded").GetBoolean() ? second : first;
        Assert.True(succeeded.GetProperty("succeeded").GetBoolean());
        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("busy", rejected.GetProperty("code").GetString());
        Assert.Single(processFactory.Processes);

        JsonElement stopped = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.stop",
            operationId = "terminal-concurrent-stop",
            terminalId = succeeded.GetProperty("terminalId").GetString()
        }, "terminal.stop.result");
        Assert.True(stopped.GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public async Task ReplayedAnswerDoesNotReapplyTheSessionDescription()
    {
        var peerFactory = new FakeTerminalPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: new FakeTerminalProcessFactory(),
            terminalPeerFactory: peerFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string startOperation = "terminal-replay-start";
        const string answerOperation = "terminal-replay-answer";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId = startOperation,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, startOperation, 80, 24))
        }, "terminal.start.result");
        string terminalId = started.GetProperty("terminalId").GetString()!;
        JsonElement offer = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=terminal answer\r\nt=0 0\r\n";
        string proof = key.SignPayload(TerminalNegotiation.AnswerTranscript(
            "client-terminal", fixture.Manager.HostIdentity.PublicKey, startOperation, answerOperation,
            terminalId, TerminalNegotiation.HashSdp(offer.GetProperty("offerSdp").GetString()!),
            TerminalNegotiation.HashSdp(answerSdp)));
        var answer = new
        {
            type = "terminal.answer",
            operationId = answerOperation,
            offerOperationId = startOperation,
            terminalId,
            answerSdp,
            clientSignature = proof
        };

        Assert.True((await SendUntilTypeAsync(socket, answer, "terminal.answer.result")).GetProperty("succeeded").GetBoolean());
        JsonElement replayed = await SendUntilTypeAsync(socket, answer, "terminal.answer.result");
        Assert.False(replayed.GetProperty("succeeded").GetBoolean());
        Assert.Equal("replayed-operation", replayed.GetProperty("code").GetString());
        Assert.Equal(1, Assert.Single(peerFactory.Peers).ApplyAnswerCount);
    }

    [Fact]
    public async Task ManySmallInputRecordsUseTheByteLimitInsteadOfARecordLimit()
    {
        var processFactory = new FakeTerminalProcessFactory { BlockInputWrites = true };
        var peerFactory = new FakeTerminalPeerFactory();
        await using var fixture = await WebHostFixture.StartAsync(
            terminalProcessFactory: processFactory,
            terminalPeerFactory: peerFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string startOperation = "terminal-small-input-start";
        const string answerOperation = "terminal-small-input-answer";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId = startOperation,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, startOperation, 80, 24))
        }, "terminal.start.result");
        string terminalId = started.GetProperty("terminalId").GetString()!;
        JsonElement offer = await ReceiveUntilTypeAsync(socket, "terminal.offer");
        const string answerSdp = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=terminal answer\r\nt=0 0\r\n";
        string proof = key.SignPayload(TerminalNegotiation.AnswerTranscript(
            "client-terminal", fixture.Manager.HostIdentity.PublicKey, startOperation, answerOperation,
            terminalId, TerminalNegotiation.HashSdp(offer.GetProperty("offerSdp").GetString()!),
            TerminalNegotiation.HashSdp(answerSdp)));
        Assert.True((await SendUntilTypeAsync(socket, new
        {
            type = "terminal.answer",
            operationId = answerOperation,
            offerOperationId = startOperation,
            terminalId,
            answerSdp,
            clientSignature = proof
        }, "terminal.answer.result")).GetProperty("succeeded").GetBoolean());

        FakeTerminalPeer peer = Assert.Single(peerFactory.Peers);
        for (int index = 0; index < 18; index++)
            await peer.ReceiveAsync(TerminalProtocol.CreateInput([(byte)index]));
        await Task.Delay(100);
        Assert.False(Assert.Single(processFactory.Processes).Terminated);
    }

    [Fact]
    public async Task LivePermissionRevocationTerminatesTheTerminal()
    {
        var processFactory = new FakeTerminalProcessFactory();
        await using var fixture = await WebHostFixture.StartAsync(terminalProcessFactory: processFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string operationId = "terminal-revoke-start";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, operationId, 80, 24))
        }, "terminal.start.result");
        Assert.True(started.GetProperty("succeeded").GetBoolean());

        FakeTerminalProcess process = Assert.Single(processFactory.Processes);
        fixture.Manager.SetDevicePermission("client-terminal", DevicePermissionKind.Terminal, false);
        await WaitUntilAsync(() => process.Terminated);

        JsonElement ended = await ReceiveUntilTypeAsync(socket, "terminal.ended");
        Assert.Equal("permission-revoked", ended.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ProcessCreationFailureReturnsABoundedResultWithoutLeavingOwnership()
    {
        var processFactory = new FakeTerminalProcessFactory { Failure = new IOException("injected") };
        await using var fixture = await WebHostFixture.StartAsync(terminalProcessFactory: processFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string operationId = "terminal-process-failure";
        JsonElement failed = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, operationId, 80, 24))
        }, "terminal.start.result");

        Assert.False(failed.GetProperty("succeeded").GetBoolean());
        Assert.Equal("process-start-failed", failed.GetProperty("code").GetString());
        Assert.Empty(processFactory.Processes);
        JsonElement status = await SendUntilTypeAsync(socket, new { type = "status.get" }, "status");
        Assert.False(status.GetProperty("capabilities").GetProperty("terminal").GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task OutputPipeEofTerminatesTheProcessAndClearsOwnership()
    {
        var processFactory = new FakeTerminalProcessFactory();
        await using var fixture = await WebHostFixture.StartAsync(terminalProcessFactory: processFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string operationId = "terminal-output-eof";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, operationId, 80, 24))
        }, "terminal.start.result");
        Assert.True(started.GetProperty("succeeded").GetBoolean());

        FakeTerminalProcess process = Assert.Single(processFactory.Processes);
        process.CompleteOutput();
        await WaitUntilAsync(() => process.Terminated);
        JsonElement ended = await ReceiveUntilTypeAsync(socket, "terminal.ended");
        Assert.Equal("pipe-failed", ended.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task NaturalProcessExitWinsTheOutputEofRace()
    {
        var processFactory = new FakeTerminalProcessFactory();
        await using var fixture = await WebHostFixture.StartAsync(terminalProcessFactory: processFactory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        const string operationId = "terminal-natural-exit";
        JsonElement started = await SendUntilTypeAsync(socket, new
        {
            type = "terminal.start",
            operationId,
            columns = 80,
            rows = 24,
            clientSignature = key.SignPayload(TerminalNegotiation.StartTranscript(
                "client-terminal", fixture.Manager.HostIdentity.PublicKey, operationId, 80, 24))
        }, "terminal.start.result");
        Assert.True(started.GetProperty("succeeded").GetBoolean());

        Assert.Single(processFactory.Processes).ExitNaturally();
        JsonElement ended = await ReceiveUntilTypeAsync(socket, "terminal.ended");
        Assert.Equal("shell-exited", ended.GetProperty("reason").GetString());
    }

    private static async Task PairAsync(WebSocket socket, PairingManager manager, PairingTestKey key)
    {
        JsonElement accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = "client-terminal",
            deviceName = "Terminal test phone",
            pairToken = manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
    }

    private static async Task<JsonElement> SendUntilTypeAsync(WebSocket socket, object payload, string expectedType)
    {
        await SendAsync(socket, payload);
        return await ReceiveUntilTypeAsync(socket, expectedType);
    }

    private static async Task<JsonElement> ReceiveUntilTypeAsync(WebSocket socket, string expectedType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string text = await ReceiveTextAsync(socket, timeout.Token);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.GetProperty("type").GetString() == expectedType)
                return document.RootElement.Clone();
        }
        throw new InvalidOperationException($"The host did not send {expectedType}.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeTerminalProcessFactory : ITerminalProcessFactory
    {
        public List<FakeTerminalProcess> Processes { get; } = [];
        public Exception? Failure { get; init; }
        public bool BlockInputWrites { get; init; }
        public ITerminalProcess Start(ushort columns, ushort rows)
        {
            if (Failure is not null) throw Failure;
            var process = new FakeTerminalProcess(BlockInputWrites);
            Processes.Add(process);
            return process;
        }
    }

    private sealed class FakeTerminalProcess : ITerminalProcess
    {
        private readonly Pipe _input = new();
        private readonly Pipe _output = new();
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _captureInput;

        public FakeTerminalProcess(bool blockInputWrites)
        {
            Input = blockInputWrites ? new BlockingWriteStream() : _input.Writer.AsStream();
            _captureInput = CaptureInputAsync();
        }

        public List<byte> InputBytes { get; } = [];
        public Stream Input { get; }
        public Stream Output => _output.Reader.AsStream();
        public Task<int> ExitCode => _exit.Task;
        public bool Terminated { get; private set; }
        public void Resize(ushort columns, ushort rows) { }
        public async Task WriteOutputAsync(byte[] bytes) => await _output.Writer.WriteAsync(bytes);
        public void CompleteOutput() => _output.Writer.Complete();
        public void ExitNaturally()
        {
            _output.Writer.Complete();
            _exit.TrySetResult(0);
        }
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
            await _captureInput;
            await _input.Reader.CompleteAsync();
            await _output.Reader.CompleteAsync();
        }
        private async Task CaptureInputAsync()
        {
            await foreach (ReadOnlyMemory<byte> chunk in _input.Reader.AsStream().ReadChunksAsync())
                InputBytes.AddRange(chunk.ToArray());
        }
    }

    private sealed class BlockingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FakeTerminalPeerFactory : ITerminalWebRtcPeerFactory
    {
        public List<FakeTerminalPeer> Peers { get; } = [];
        public bool OpenOnAnswer { get; init; } = true;
        public ITerminalWebRtcPeer Create(FileTransferPeerConfiguration? configuration)
        {
            var peer = new FakeTerminalPeer(OpenOnAnswer);
            Peers.Add(peer);
            return peer;
        }
    }

    private sealed class FakeTerminalPeer(bool openOnAnswer) : ITerminalWebRtcPeer
    {
        private readonly Channel<byte[]> _received = Channel.CreateUnbounded<byte[]>();
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Opened => _opened.Task;
        public Task Closed => _closed.Task;
        public ChannelReader<byte[]> Messages => _received.Reader;
        public List<byte[]> Sent { get; } = [];
        public int ApplyAnswerCount { get; private set; }
        public bool Disposed { get; private set; }
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) =>
            Task.FromResult("v=0\r\no=host 1 1 IN IP4 127.0.0.1\r\ns=terminal offer\r\nt=0 0\r\n");
        public void ApplyAnswer(string answerSdp)
        {
            ApplyAnswerCount++;
            if (openOnAnswer) CompleteOpen();
        }
        public void CompleteOpen() => _opened.TrySetResult();
        public bool TrySend(byte[] record) { Sent.Add(record); return true; }
        public ValueTask ReceiveAsync(byte[] record) => _received.Writer.WriteAsync(record);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _received.Writer.TryComplete();
            _closed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}

internal static class TerminalTestStreamExtensions
{
    internal static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadChunksAsync(this Stream stream)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0) yield break;
            yield return buffer.AsMemory(0, read).ToArray();
        }
    }
}
