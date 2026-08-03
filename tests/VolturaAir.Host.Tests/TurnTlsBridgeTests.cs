using System.Buffers.Binary;
using System.Net;
using System.Security.Authentication;
using System.Threading.Channels;

namespace VolturaAir.Host.Tests;

public sealed class TurnTlsBridgeTests
{
    [Fact]
    public void ParsesOnlyAuthenticatedTurnTlsEndpoints()
    {
        Assert.True(TurnTlsEndpoint.TryParse(
            "turns:user%3Aone:secret%40value@turn.cloudflare.com:443?transport=tcp",
            out var endpoint));
        Assert.Equal("turn.cloudflare.com", endpoint!.Host);
        Assert.Equal(443, endpoint.Port);
        Assert.Equal("user:one", endpoint.Username);
        Assert.Equal("secret@value", endpoint.Credential);

        Assert.False(TurnTlsEndpoint.TryParse("turn:user:secret@turn.cloudflare.com:3478?transport=udp", out _));
        Assert.False(TurnTlsEndpoint.TryParse("turns:user:secret@turn.cloudflare.com:443?transport=udp", out _));
        Assert.False(TurnTlsEndpoint.TryParse("turns:turn.cloudflare.com:443?transport=tcp", out _));
        Assert.False(TurnTlsEndpoint.TryParse("turns:user:secret@turn.cloudflare.com:443?transport=tcp#value", out _));
    }

    [Fact]
    public void RelayMappingUsesOnlyTlsBridgesAndRequiresOne()
    {
        var observed = new List<TurnTlsEndpoint>();
        IReadOnlyList<string> mapped = TurnTlsIceServerMapper.Map(
        [
            "turns:user:secret@turn.cloudflare.com:443?transport=tcp",
            "turn:user:secret@turn.cloudflare.com:3478?transport=udp"
        ],
        endpoint =>
        {
            observed.Add(endpoint);
            return "turn:user:secret@127.0.0.1:41234?transport=udp";
        });

        Assert.Single(observed);
        Assert.Equal(["turn:user:secret@127.0.0.1:41234?transport=udp"], mapped);
        Assert.Throws<ScreenViewWebRtcException>(() =>
            TurnTlsIceServerMapper.Map(
                ["turn:user:secret@turn.cloudflare.com:3478?transport=udp"],
                _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void StreamFramingPreservesStunAndPadsChannelData()
    {
        byte[] stun = StunFrame();
        Assert.Equal(stun, TurnStreamFraming.PrepareDatagramForStream(stun));

        byte[] channelData = [0x40, 0x01, 0x00, 0x03, 0x11, 0x22, 0x33];
        Assert.Equal(
            [0x40, 0x01, 0x00, 0x03, 0x11, 0x22, 0x33, 0x00],
            TurnStreamFraming.PrepareDatagramForStream(channelData));
    }

    [Fact]
    public async Task StreamReaderHandlesFragmentationAndRemovesChannelPadding()
    {
        byte[] bytes =
        [
            .. StunFrame(),
            0x40, 0x01, 0x00, 0x03, 0x11, 0x22, 0x33, 0x00
        ];
        await using var stream = new FragmentedReadStream(bytes);

        Assert.Equal(StunFrame(), await TurnStreamFraming.ReadStreamFrameAsync(stream, CancellationToken.None));
        Assert.Equal(
            [0x40, 0x01, 0x00, 0x03, 0x11, 0x22, 0x33],
            await TurnStreamFraming.ReadStreamFrameAsync(stream, CancellationToken.None));
        Assert.Null(await TurnStreamFraming.ReadStreamFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task FramingRejectsMalformedReservedAndOversizeMessages()
    {
        Assert.Throws<InvalidDataException>(() =>
            TurnStreamFraming.PrepareDatagramForStream([0x80, 0, 0, 0]));
        Assert.Throws<InvalidDataException>(() =>
            TurnStreamFraming.PrepareDatagramForStream([0, 1, 0, 0, 0, 0, 0, 0]));
        byte[] unaligned = StunFrame();
        BinaryPrimitives.WriteUInt16BigEndian(unaligned.AsSpan(2), 1);
        Assert.Throws<InvalidDataException>(() =>
            TurnStreamFraming.PrepareDatagramForStream([.. unaligned, 0]));

        byte[] oversized = [0, 1, 0xff, 0xff, 0x21, 0x12, 0xa4, 0x42, .. new byte[12]];
        await using var stream = new MemoryStream(oversized);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await TurnStreamFraming.ReadStreamFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task BridgeReportsTlsFailureAndCleansUpWithoutOpeningPorts()
    {
        var socket = new FakeTurnDatagramSocket(41234);
        var endpoint = new TurnTlsEndpoint("turn.cloudflare.com", 443, "user:name", "secret@value");
        using var bridge = new TurnTlsBridge(
            endpoint,
            socket,
            (_, _) => Task.FromException<Stream>(new AuthenticationException("test")));
        Assert.Equal(
            "turn:user%3Aname:secret%40value@127.0.0.1:41234?transport=udp",
            bridge.LocalIceServerUri);

        socket.Queue(StunFrame(), new IPEndPoint(IPAddress.Loopback, 51321));
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("tls", bridge.FailureCode);
        bridge.Dispose();
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task BridgeForwardsTurnFramesAcrossFakeBoundaries()
    {
        var socket = new FakeTurnDatagramSocket(41234);
        var stream = new ScriptedDuplexStream();
        var endpoint = new TurnTlsEndpoint("turn.cloudflare.com", 443, "user", "secret");
        using var bridge = new TurnTlsBridge(endpoint, socket, (_, _) => Task.FromResult<Stream>(stream));
        var owner = new IPEndPoint(IPAddress.Loopback, 51321);

        socket.Queue(StunFrame(), owner);
        Assert.Equal(StunFrame(), await stream.ReadWrittenAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));

        stream.QueueRead([0x40, 0x01, 0x00, 0x03, 0x11, 0x22, 0x33, 0x00]);
        TurnDatagram relayed = await socket.ReadSentAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(owner, relayed.RemoteEndpoint);
        Assert.Equal([0x40, 0x01, 0x00, 0x03, 0x11, 0x22, 0x33], relayed.Payload);

        bridge.Dispose();
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task BridgeDisposalCancelsAnIdleBoundary()
    {
        var socket = new FakeTurnDatagramSocket(41234);
        var endpoint = new TurnTlsEndpoint("turn.cloudflare.com", 443, "user", "secret");
        var bridge = new TurnTlsBridge(endpoint, socket, (_, _) => Task.FromResult<Stream>(Stream.Null));

        bridge.Dispose();
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(socket.Disposed);
    }

    private static byte[] StunFrame()
    {
        var frame = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(frame, 0x0003);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), 0x2112A442);
        return frame;
    }

    private sealed class FakeTurnDatagramSocket(int localPort) : ITurnDatagramSocket
    {
        private readonly Channel<TurnDatagram> _receive = Channel.CreateUnbounded<TurnDatagram>();
        private readonly Channel<TurnDatagram> _sent = Channel.CreateUnbounded<TurnDatagram>();
        private readonly CancellationTokenSource _disposed = new();
        public int LocalPort { get; } = localPort;
        public bool Disposed { get; private set; }

        public void Queue(byte[] payload, IPEndPoint endpoint) =>
            Assert.True(_receive.Writer.TryWrite(new TurnDatagram(payload, endpoint)));

        public async ValueTask<TurnDatagram> ReceiveAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
            return await _receive.Reader.ReadAsync(linked.Token);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> payload, IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(_sent.Writer.TryWrite(new TurnDatagram(payload.ToArray(), endpoint)));
            return ValueTask.CompletedTask;
        }

        public ValueTask<TurnDatagram> ReadSentAsync() => _sent.Reader.ReadAsync();

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _disposed.Cancel();
            _receive.Writer.TryComplete();
            _sent.Writer.TryComplete();
            _disposed.Dispose();
        }
    }

    private sealed class FragmentedReadStream(byte[] source) : MemoryStream(source)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private sealed class ScriptedDuplexStream : Stream
    {
        private readonly Channel<byte[]> _reads = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> _writes = Channel.CreateUnbounded<byte[]>();
        private byte[]? _currentRead;
        private int _currentOffset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public void QueueRead(byte[] frame) => Assert.True(_reads.Writer.TryWrite(frame));
        public ValueTask<byte[]> ReadWrittenAsync() => _writes.Reader.ReadAsync();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_currentRead is null || _currentOffset == _currentRead.Length)
            {
                _currentRead = await _reads.Reader.ReadAsync(cancellationToken);
                _currentOffset = 0;
            }
            int count = Math.Min(buffer.Length, _currentRead.Length - _currentOffset);
            _currentRead.AsMemory(_currentOffset, count).CopyTo(buffer);
            _currentOffset += count;
            return count;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(_writes.Writer.TryWrite(buffer.ToArray()));
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reads.Writer.TryComplete();
                _writes.Writer.TryComplete();
            }
            base.Dispose(disposing);
        }
    }
}
