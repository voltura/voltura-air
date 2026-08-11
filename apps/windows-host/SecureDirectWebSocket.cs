using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class SecureDirectWebSocket : WebSocket
{
    internal const string DataChannelLabel = "voltura-control";
    private const int MaximumMessageBytes = WebSocketTransport.MaxMessageBytes;
    private const int MaximumQueuedBytes = 256 * 1024;
    private const int MaximumNativeBufferedBytes = 256 * 1024;
    private const int MaximumSdpBytes = 32 * 1024;
    private readonly IPAddress _bindAddress;
    private readonly Channel<byte[]> _receive = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.MessageCallback _messageCallback;
    private readonly GCHandle _selfHandle;
    private int _peer;
    private int _dataChannel;
    private int _queuedBytes;
    private int _disposeState;
    private WebSocketState _state = WebSocketState.Connecting;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeDescription;
    private byte[]? _remainder;
    private int _remainderOffset;

    internal SecureDirectWebSocket(IPAddress bindAddress)
    {
        if (bindAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !LanAddressSelector.IsPrivateIpv4Address(bindAddress))
            throw new ArgumentException("Secure Direct requires a private IPv4 bind address.", nameof(bindAddress));

        _bindAddress = bindAddress;
        _descriptionCallback = OnDescription;
        _stateCallback = OnState;
        _gatheringCallback = OnGathering;
        _openCallback = OnOpen;
        _closedCallback = OnClosed;
        _errorCallback = OnError;
        _messageCallback = OnMessage;
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        nint pointer = GCHandle.ToIntPtr(_selfHandle);
        try
        {
            using var nativeBindAddress = new Utf8String(bindAddress.ToString());
            var configuration = new LibDataChannelNative.Configuration
            {
                BindAddress = nativeBindAddress.Pointer,
                CertificateType = LibDataChannelNative.CertificateType.Ecdsa,
                IceTransportPolicy = LibDataChannelNative.TransportPolicy.All,
                EnableIceTcp = 0,
                DisableAutoNegotiation = 1,
                ForceMediaTransport = 0,
                Mtu = 1280,
                MaxMessageSize = MaximumMessageBytes
            };
            _peer = EnsureCreated(LibDataChannelNative.rtcCreatePeerConnection(in configuration), "create the Secure Direct peer");
            LibDataChannelNative.rtcSetUserPointer(_peer, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetLocalDescriptionCallback(_peer, _descriptionCallback), "listen for the Secure Direct offer");
            EnsureSuccess(LibDataChannelNative.rtcSetStateChangeCallback(_peer, _stateCallback), "listen for Secure Direct state changes");
            EnsureSuccess(LibDataChannelNative.rtcSetGatheringStateChangeCallback(_peer, _gatheringCallback), "listen for Secure Direct candidate gathering");

            _dataChannel = EnsureCreated(LibDataChannelNative.rtcCreateDataChannel(_peer, DataChannelLabel), "create the Secure Direct DataChannel");
            LibDataChannelNative.rtcSetUserPointer(_dataChannel, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_dataChannel, _openCallback), "listen for the Secure Direct DataChannel");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_dataChannel, _closedCallback), "listen for Secure Direct closure");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_dataChannel, _errorCallback), "listen for Secure Direct errors");
            EnsureSuccess(LibDataChannelNative.rtcSetMessageCallback(_dataChannel, _messageCallback), "receive Secure Direct messages");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeDescription;
    public override string SubProtocol => string.Empty;
    public override WebSocketState State => _state;

    internal async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create the Secure Direct offer");
        return await _offer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (string.IsNullOrWhiteSpace(answerSdp) || Encoding.UTF8.GetByteCount(answerSdp) > MaximumSdpBytes)
            throw new InvalidDataException("The Secure Direct answer was invalid.");
        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply the Secure Direct answer");
    }

    internal async Task WaitForOpenAndValidateAsync(CancellationToken cancellationToken)
    {
        await _opened.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!HasPrivateHostPair())
        {
            Abort();
            throw new InvalidDataException("Secure Direct did not select the configured private LAN path.");
        }
        _state = WebSocketState.Open;
    }

    public override void Abort()
    {
        if (_state is not WebSocketState.Closed) _state = WebSocketState.Aborted;
        _receive.Writer.TryComplete();
        _opened.TrySetCanceled();
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _closeStatus = closeStatus;
        _closeDescription = statusDescription;
        _state = WebSocketState.Closed;
        _receive.Writer.TryComplete();
        CloseNative();
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        CloseAsync(closeStatus, statusDescription, cancellationToken);

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_remainder is null)
        {
            try
            {
                _remainder = await _receive.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                _remainderOffset = 0;
            }
            catch (ChannelClosedException)
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, _closeStatus, _closeDescription);
            }
        }
        int count = Math.Min(buffer.Count, _remainder.Length - _remainderOffset);
        _remainder.AsSpan(_remainderOffset, count).CopyTo(buffer.AsSpan());
        _remainderOffset += count;
        bool end = _remainderOffset == _remainder.Length;
        if (end)
        {
            Interlocked.Add(ref _queuedBytes, -_remainder.Length);
            _remainder = null;
        }
        return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state != WebSocketState.Open || messageType != WebSocketMessageType.Text || !endOfMessage ||
            buffer.Count == 0 || buffer.Count > MaximumMessageBytes)
            throw new WebSocketException(WebSocketError.InvalidMessageType);
        if (LibDataChannelNative.rtcGetBufferedAmount(_dataChannel) > MaximumNativeBufferedBytes)
        {
            Abort();
            throw new WebSocketException(WebSocketError.Faulted);
        }

        string text;
        try { text = new UTF8Encoding(false, true).GetString(buffer.AsSpan()); }
        catch (DecoderFallbackException exception) { throw new WebSocketException(WebSocketError.InvalidMessageType, exception); }
        nint native = Marshal.StringToCoTaskMemUTF8(text);
        try { EnsureSuccess(LibDataChannelNative.rtcSendTextMessage(_dataChannel, native, -1), "send a Secure Direct message"); }
        finally { Marshal.FreeCoTaskMem(native); }
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        Abort();
        _offer.TrySetCanceled();
        CloseNative();
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    private void CloseNative()
    {
        int channel = Interlocked.Exchange(ref _dataChannel, 0);
        int peer = Interlocked.Exchange(ref _peer, 0);
        if (channel > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(channel, 0);
            _ = LibDataChannelNative.rtcDeleteDataChannel(channel);
        }
        if (peer > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(peer, 0);
            _ = LibDataChannelNative.rtcClosePeerConnection(peer);
            _ = LibDataChannelNative.rtcDeletePeerConnection(peer);
        }
    }

    private void CompleteOffer()
    {
        int size = LibDataChannelNative.rtcGetLocalDescription(_peer, 0, 0);
        if (size <= 1 || size > MaximumSdpBytes + 1)
        {
            FailOffer();
            return;
        }
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int copied = LibDataChannelNative.rtcGetLocalDescription(_peer, buffer, size);
            string? sdp = copied > 1 ? Marshal.PtrToStringUTF8(buffer) : null;
            if (string.IsNullOrWhiteSpace(sdp) || Encoding.UTF8.GetByteCount(sdp) > MaximumSdpBytes) FailOffer();
            else _offer.TrySetResult(sdp);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void FailOffer() => _offer.TrySetException(new InvalidDataException("The Secure Direct offer was invalid."));

    private void ReceiveText(nint message, int size)
    {
        if (size >= 0) { Abort(); return; }
        string? text = Marshal.PtrToStringUTF8(message);
        if (string.IsNullOrEmpty(text)) { Abort(); return; }
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaximumMessageBytes) { Abort(); return; }
        int queued = Interlocked.Add(ref _queuedBytes, bytes.Length);
        if (queued > MaximumQueuedBytes || !_receive.Writer.TryWrite(bytes))
        {
            Interlocked.Add(ref _queuedBytes, -bytes.Length);
            Abort();
        }
    }

    private bool HasPrivateHostPair()
    {
        if (!TryGetAddress(LibDataChannelNative.rtcGetLocalAddress, out var local) ||
            !TryGetAddress(LibDataChannelNative.rtcGetRemoteAddress, out var remote) ||
            !local.Equals(_bindAddress) || !LanAddressSelector.IsPrivateIpv4Address(local) ||
            !LanAddressSelector.IsPrivateIpv4Address(remote)) return false;
        return true;
    }

    private bool TryGetAddress(Func<int, nint, int, int> getter, out IPAddress address)
    {
        address = IPAddress.None;
        int size = getter(_peer, 0, 0);
        if (size <= 1 || size > 4096) return false;
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (getter(_peer, buffer, size) <= 1 || Marshal.PtrToStringUTF8(buffer) is not { } endpoint) return false;
            if (IPAddress.TryParse(endpoint, out var direct) &&
                direct.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                address = direct;
                return true;
            }
            int separator = endpoint.LastIndexOf(':');
            if (separator <= 0 || !IPAddress.TryParse(endpoint[..separator], out var parsed) ||
                parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            address = parsed;
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static SecureDirectWebSocket? From(nint pointer) =>
        pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as SecureDirectWebSocket;
    private static void OnDescription(int peer, nint sdp, nint type, nint pointer) { }
    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer)
    {
        var owner = From(pointer);
        if (owner is not null && state is LibDataChannelNative.PeerState.Disconnected or LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Closed) owner.Abort();
    }
    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer)
    {
        if (state == LibDataChannelNative.GatheringState.Complete) From(pointer)?.CompleteOffer();
    }
    private static void OnOpen(int id, nint pointer)
    {
        var owner = From(pointer);
        if (owner is not null && id == owner._dataChannel) owner._opened.TrySetResult();
    }
    private static void OnClosed(int id, nint pointer)
    {
        var owner = From(pointer);
        if (owner is not null && id == owner._dataChannel) owner.Abort();
    }
    private static void OnError(int id, nint message, nint pointer) => From(pointer)?.Abort();
    private static void OnMessage(int id, nint message, int size, nint pointer)
    {
        var owner = From(pointer);
        if (owner is not null && id == owner._dataChannel) owner.ReceiveText(message, size);
    }
    private static int EnsureCreated(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"Could not {operation} (native error {result}).");
        return result;
    }
    private static void EnsureSuccess(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"Could not {operation} (native error {result}).");
    }
}
