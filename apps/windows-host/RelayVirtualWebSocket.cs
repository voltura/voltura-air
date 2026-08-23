using System.Net.WebSockets;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class RelayVirtualWebSocket(
    Guid sessionId,
    string routeId,
    Func<RelayEnvelope, CancellationToken, Task> send) : WebSocket
{
    private readonly Channel<byte[]> _receive = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true
    });
    private int _state = (int)WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeStatusDescription;
    private byte[]? _remainder;
    private int _remainderOffset;
    private RelaySessionCrypto? _crypto;
    private int _closeStarted;

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeStatusDescription;
    public override string SubProtocol => string.Empty;
    public override WebSocketState State => (WebSocketState)Volatile.Read(ref _state);
    public string RouteId => routeId;

    public bool TryReceive(byte[] payload, bool isBinary)
    {
        try
        {
            if (_crypto is not null)
            {
                if (!isBinary) throw new System.Security.Cryptography.CryptographicException("Encrypted Relay frames must be binary.");
                payload = _crypto.Decrypt(payload);
            }
            else if (isBinary)
            {
                throw new System.Security.Cryptography.CryptographicException("Relay pairing frames must be text.");
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Abort();
            return false;
        }
        if (State != WebSocketState.Open || payload.Length > WebSocketTransport.MaxMessageBytes || !_receive.Writer.TryWrite(payload))
        {
            Abort();
            return false;
        }

        return true;
    }

    public void CompleteFromRelay()
    {
        Interlocked.CompareExchange(ref _state, (int)WebSocketState.CloseReceived, (int)WebSocketState.Open);
        _closeStatus = WebSocketCloseStatus.EndpointUnavailable;
        _closeStatusDescription = "Relay device disconnected";
        _receive.Writer.TryComplete();
    }

    public override void Abort()
    {
        Interlocked.Exchange(ref _state, (int)WebSocketState.Aborted);
        _receive.Writer.TryComplete();
    }

    public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _closeStarted, 1) != 0) return;
        _closeStatus = closeStatus;
        _closeStatusDescription = statusDescription;
        Interlocked.Exchange(ref _state, (int)WebSocketState.Closed);
        _receive.Writer.TryComplete();
        await send(new RelayEnvelope(RelayEnvelopeKind.CloseDevice, sessionId, []), cancellationToken).ConfigureAwait(false);
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        CloseAsync(closeStatus, statusDescription, cancellationToken);

    public override void Dispose()
    {
        Abort();
        _crypto?.Dispose();
        _crypto = null;
    }

    internal void EnableEncryption(RelaySessionCrypto crypto)
    {
        if (_crypto is not null) throw new InvalidOperationException("Relay session encryption is already active.");
        _crypto = crypto;
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (State != WebSocketState.Open)
        {
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, CloseStatus, CloseStatusDescription);
        }

        if (_remainder is null)
        {
            try
            {
                _remainder = await _receive.Reader.ReadAsync(cancellationToken);
                _remainderOffset = 0;
            }
            catch (ChannelClosedException)
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, CloseStatus, CloseStatusDescription);
            }
        }

        if (State != WebSocketState.Open)
        {
            _remainder = null;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, CloseStatus, CloseStatusDescription);
        }

        var count = Math.Min(buffer.Count, _remainder.Length - _remainderOffset);
        _remainder.AsSpan(_remainderOffset, count).CopyTo(buffer.AsSpan());
        _remainderOffset += count;
        var end = _remainderOffset == _remainder.Length;
        if (end) _remainder = null;
        return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (State != WebSocketState.Open || messageType != WebSocketMessageType.Text || !endOfMessage)
        {
            throw new WebSocketException(WebSocketError.InvalidMessageType);
        }

        var payload = _crypto is null ? [.. buffer] : _crypto.Encrypt(buffer);
        return send(new RelayEnvelope(_crypto is null ? RelayEnvelopeKind.Text : RelayEnvelopeKind.Binary, sessionId, payload), cancellationToken);
    }

}
