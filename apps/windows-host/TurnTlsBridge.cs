using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace VolturaAir.Host;

internal sealed record TurnTlsEndpoint(string Host, int Port, string Username, string Credential)
{
    public static bool TryParse(string value, out TurnTlsEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("turns:", StringComparison.OrdinalIgnoreCase))
            return false;

        string absolute = value.StartsWith("turns://", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"turns://{value[6..]}";
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "turns", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        int separator = uri.UserInfo.IndexOf(':');
        if (separator <= 0 || separator == uri.UserInfo.Length - 1)
            return false;
        string transport = ParseTransport(uri.Query);
        if (transport is not ("tcp" or "tls"))
            return false;

        try
        {
            endpoint = new TurnTlsEndpoint(
                uri.IdnHost,
                uri.Port,
                Uri.UnescapeDataString(uri.UserInfo[..separator]),
                Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]));
            return endpoint.Username.Length > 0 && endpoint.Credential.Length > 0;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static string ParseTransport(string query)
    {
        foreach (string component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = component.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], "transport", StringComparison.OrdinalIgnoreCase))
                return pair[1].ToLowerInvariant();
        }
        return "tcp";
    }
}

internal static class TurnTlsIceServerMapper
{
    public static IReadOnlyList<string> Map(
        IReadOnlyList<string> source,
        Func<TurnTlsEndpoint, string> createBridge)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(createBridge);
        var mapped = new List<string>();
        foreach (string value in source)
        {
            if (TurnTlsEndpoint.TryParse(value, out var endpoint) && endpoint is not null)
                mapped.Add(createBridge(endpoint));
        }
        if (mapped.Count == 0)
            throw new ScreenViewWebRtcException("The relay did not provide a TURN TLS server.");
        return mapped;
    }
}

internal static class TurnStreamFraming
{
    internal const int MaximumFrameBytes = 64 * 1024;
    private const uint StunMagicCookie = 0x2112A442;

    public static byte[] PrepareDatagramForStream(ReadOnlySpan<byte> datagram)
    {
        int frameLength = GetDatagramFrameLength(datagram);
        int streamLength = IsChannelData(datagram) ? AlignToFour(frameLength) : frameLength;
        var result = new byte[streamLength];
        datagram[..frameLength].CopyTo(result);
        return result;
    }

    public static async ValueTask<byte[]?> ReadStreamFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        if (!await ReadExactlyOrEofAsync(stream, prefix, cancellationToken).ConfigureAwait(false))
            return null;

        if (IsChannelData(prefix))
        {
            int payloadLength = BinaryPrimitives.ReadUInt16BigEndian(prefix.AsSpan(2, 2));
            int datagramLength = 4 + payloadLength;
            int streamLength = AlignToFour(datagramLength);
            ValidateBound(datagramLength);
            var result = new byte[datagramLength];
            prefix.CopyTo(result, 0);
            await ReadExactlyAsync(stream, result.AsMemory(4, payloadLength), cancellationToken).ConfigureAwait(false);
            int padding = streamLength - datagramLength;
            if (padding > 0)
                await ReadExactlyAsync(stream, new byte[padding], cancellationToken).ConfigureAwait(false);
            return result;
        }

        if ((prefix[0] & 0xC0) != 0)
            throw new InvalidDataException("The TURN stream contained a reserved frame type.");
        int bodyLength = BinaryPrimitives.ReadUInt16BigEndian(prefix.AsSpan(2, 2));
        if ((bodyLength & 3) != 0)
            throw new InvalidDataException("The TURN STUN message was not four-byte aligned.");
        int totalLength = 20 + bodyLength;
        ValidateBound(totalLength);
        var stun = new byte[totalLength];
        prefix.CopyTo(stun, 0);
        await ReadExactlyAsync(stream, stun.AsMemory(4), cancellationToken).ConfigureAwait(false);
        ValidateStun(stun);
        return stun;
    }

    private static int GetDatagramFrameLength(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < 4)
            throw new InvalidDataException("The TURN datagram was shorter than its header.");
        if (IsChannelData(datagram))
        {
            int frameLength = 4 + BinaryPrimitives.ReadUInt16BigEndian(datagram[2..4]);
            ValidateBound(frameLength);
            if (datagram.Length < frameLength || datagram.Length > AlignToFour(frameLength))
                throw new InvalidDataException("The TURN ChannelData length was invalid.");
            return frameLength;
        }
        if ((datagram[0] & 0xC0) != 0 || datagram.Length < 20)
            throw new InvalidDataException("The TURN datagram used a reserved frame type.");
        int totalLength = 20 + BinaryPrimitives.ReadUInt16BigEndian(datagram[2..4]);
        if (((totalLength - 20) & 3) != 0)
            throw new InvalidDataException("The TURN STUN message was not four-byte aligned.");
        ValidateBound(totalLength);
        if (datagram.Length != totalLength)
            throw new InvalidDataException("The TURN STUN message length was invalid.");
        ValidateStun(datagram);
        return totalLength;
    }

    private static void ValidateStun(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 20 || BinaryPrimitives.ReadUInt32BigEndian(frame[4..8]) != StunMagicCookie)
            throw new InvalidDataException("The TURN STUN magic cookie was invalid.");
    }

    private static bool IsChannelData(ReadOnlySpan<byte> frame) => (frame[0] & 0xC0) == 0x40;
    private static int AlignToFour(int value) => (value + 3) & ~3;
    private static void ValidateBound(int length)
    {
        if (length > MaximumFrameBytes)
            throw new InvalidDataException("The TURN frame exceeded the supported size.");
    }

    private static async ValueTask<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0) return false;
                throw new EndOfStreamException("The TURN stream ended inside a frame.");
            }
            offset += read;
        }
        return true;
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!await ReadExactlyOrEofAsync(stream, buffer, cancellationToken).ConfigureAwait(false))
            throw new EndOfStreamException("The TURN stream ended inside a frame.");
    }
}

internal readonly record struct TurnDatagram(byte[] Payload, IPEndPoint RemoteEndpoint);

internal interface ITurnDatagramSocket : IDisposable
{
    int LocalPort { get; }
    ValueTask<TurnDatagram> ReceiveAsync(CancellationToken cancellationToken);
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, IPEndPoint endpoint, CancellationToken cancellationToken);
}

internal sealed class LoopbackTurnDatagramSocket : ITurnDatagramSocket
{
    private readonly UdpClient _client = new(AddressFamily.InterNetwork);

    public LoopbackTurnDatagramSocket()
    {
        _client.Client.ExclusiveAddressUse = true;
        _client.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalPort = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
    }

    public int LocalPort { get; }

    public async ValueTask<TurnDatagram> ReceiveAsync(CancellationToken cancellationToken)
    {
        UdpReceiveResult result = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return new TurnDatagram(result.Buffer, result.RemoteEndPoint);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        _ = await _client.SendAsync(payload, endpoint, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _client.Dispose();
}

internal interface ITurnTlsBridge : IDisposable
{
    string LocalIceServerUri { get; }
    string? FailureCode { get; }
}

internal sealed class TurnTlsBridge : ITurnTlsBridge
{
    private readonly TurnTlsEndpoint _endpoint;
    private readonly ITurnDatagramSocket _socket;
    private readonly Func<TurnTlsEndpoint, CancellationToken, Task<Stream>> _connect;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _completion;
    private int _disposed;

    public TurnTlsBridge(TurnTlsEndpoint endpoint)
        : this(endpoint, new LoopbackTurnDatagramSocket(), ConnectTlsAsync)
    {
    }

    internal TurnTlsBridge(
        TurnTlsEndpoint endpoint,
        ITurnDatagramSocket socket,
        Func<TurnTlsEndpoint, CancellationToken, Task<Stream>> connect)
    {
        _endpoint = endpoint;
        _socket = socket;
        _connect = connect;
        LocalIceServerUri = $"turn:{Uri.EscapeDataString(endpoint.Username)}:{Uri.EscapeDataString(endpoint.Credential)}@127.0.0.1:{socket.LocalPort}?transport=udp";
        _completion = RunAsync();
    }

    public string LocalIceServerUri { get; }
    public string? FailureCode { get; private set; }
    internal Task Completion => _completion;

    private async Task RunAsync()
    {
        try
        {
            TurnDatagram first = await ReceiveLoopbackAsync(_stop.Token).ConfigureAwait(false);
            await using Stream stream = await _connect(_endpoint, _stop.Token).ConfigureAwait(false);
            await stream.WriteAsync(TurnStreamFraming.PrepareDatagramForStream(first.Payload), _stop.Token).ConfigureAwait(false);
            using var active = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            Task upload = UploadAsync(stream, first.RemoteEndpoint, active.Token);
            Task download = DownloadAsync(stream, first.RemoteEndpoint, active.Token);
            _ = await Task.WhenAny(upload, download).ConfigureAwait(false);
            await active.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(upload, download).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (AuthenticationException)
        {
            FailureCode = "tls";
        }
        catch (SocketException)
        {
            FailureCode = "network";
        }
        catch (IOException)
        {
            FailureCode = "network";
        }
        catch (InvalidDataException)
        {
            FailureCode = "frame";
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task UploadAsync(Stream stream, IPEndPoint owner, CancellationToken cancellationToken)
    {
        while (true)
        {
            TurnDatagram datagram = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (!datagram.RemoteEndpoint.Equals(owner)) continue;
            byte[] frame = TurnStreamFraming.PrepareDatagramForStream(datagram.Payload);
            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DownloadAsync(Stream stream, IPEndPoint owner, CancellationToken cancellationToken)
    {
        while (true)
        {
            byte[]? frame = await TurnStreamFraming.ReadStreamFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null) return;
            await _socket.SendAsync(frame, owner, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<TurnDatagram> ReceiveLoopbackAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TurnDatagram datagram = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (IPAddress.IsLoopback(datagram.RemoteEndpoint.Address)) return datagram;
        }
    }

    private static async Task<Stream> ConnectTlsAsync(TurnTlsEndpoint endpoint, CancellationToken cancellationToken)
    {
        var connection = new OwnedNetworkStream();
        try
        {
            await connection.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task IgnoreCancellationAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _socket.Dispose();
        _stop.Dispose();
    }

    private sealed class OwnedNetworkStream : Stream
    {
        private readonly TcpClient _client = new() { NoDelay = true };
        private SslStream? _stream;

        public async Task ConnectAsync(TurnTlsEndpoint endpoint, CancellationToken cancellationToken)
        {
            await _client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
            _stream = new SslStream(_client.GetStream(), leaveInnerStreamOpen: false);
            await _stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = endpoint.Host,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.Online
            }, cancellationToken).ConfigureAwait(false);
        }

        private Stream Stream => _stream ?? throw new InvalidOperationException("The TURN TLS stream is not connected.");
        public override bool CanRead => Stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => Stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => Stream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => Stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => Stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Stream.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => Stream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => Stream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream?.Dispose();
                _client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
