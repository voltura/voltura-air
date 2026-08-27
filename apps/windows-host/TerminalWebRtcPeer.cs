using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal interface ITerminalWebRtcPeer : IAsyncDisposable
{
    Task Opened { get; }
    Task Closed { get; }
    ChannelReader<byte[]> Messages { get; }
    Task<string> CreateOfferAsync(CancellationToken cancellationToken);
    void ApplyAnswer(string answerSdp);
    bool TrySend(byte[] record);
}

internal interface ITerminalWebRtcPeerFactory
{
    ITerminalWebRtcPeer Create(FileTransferPeerConfiguration? configuration);
}

internal sealed class TerminalWebRtcPeerFactory : ITerminalWebRtcPeerFactory
{
    public ITerminalWebRtcPeer Create(FileTransferPeerConfiguration? configuration) => new TerminalWebRtcPeer(configuration);
}

internal sealed class IsolatedTerminalWebRtcPeerFactory : ITerminalWebRtcPeerFactory
{
    public ITerminalWebRtcPeer Create(FileTransferPeerConfiguration? configuration) => new IsolatedPeer();

    private sealed class IsolatedPeer : ITerminalWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateBounded<byte[]>(16);
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Opened => _opened.Task;
        public Task Closed => _closed.Task;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("v=0\r\no=voltura 1 1 IN IP4 127.0.0.1\r\ns=Voltura Air isolated terminal\r\nt=0 0\r\n");
        public void ApplyAnswer(string answerSdp)
        {
            if (string.IsNullOrWhiteSpace(answerSdp)) throw new TerminalWebRtcException("The isolated answer was empty.");
            _opened.TrySetResult();
        }
        public bool TrySend(byte[] record) => record.Length is >= 1 and <= TerminalProtocol.MaximumRecordBytes;
        public ValueTask DisposeAsync()
        {
            _opened.TrySetCanceled();
            _closed.TrySetResult();
            _messages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class TerminalWebRtcPeer : ITerminalWebRtcPeer
{
    private readonly Lock _gate = new();
    private readonly Channel<byte[]> _messages = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.MessageCallback _messageCallback;
    private readonly List<ITurnTlsBridge> _turnTlsBridges = [];
    private readonly GCHandle _selfHandle;
    private int _peer;
    private int _channel;
    private bool _channelOpen;
    private bool _stopped;
    private bool _disposed;

    internal TerminalWebRtcPeer(FileTransferPeerConfiguration? configuration)
        : this(configuration, static endpoint => new TurnTlsBridge(endpoint)) { }

    internal TerminalWebRtcPeer(FileTransferPeerConfiguration? configuration, Func<TurnTlsEndpoint, ITurnTlsBridge> createTurnTlsBridge)
    {
        _descriptionCallback = OnDescription;
        _stateCallback = OnState;
        _gatheringCallback = OnGathering;
        _openCallback = OnOpen;
        _closedCallback = OnClosed;
        _errorCallback = OnError;
        _messageCallback = OnMessage;
        _selfHandle = GCHandle.Alloc(this);
        nint pointer = GCHandle.ToIntPtr(_selfHandle);
        try
        {
            IReadOnlyList<string> iceServers = configuration?.RelayOnly == true
                ? TurnTlsIceServerMapper.Map(configuration.IceServerUris, endpoint =>
                {
                    ITurnTlsBridge bridge = createTurnTlsBridge(endpoint);
                    _turnTlsBridges.Add(bridge);
                    return bridge.LocalIceServerUri;
                })
                : configuration?.IceServerUris ?? [];
            using var nativeIceServers = new NativeIceServerList(iceServers);
            var nativeConfiguration = new LibDataChannelNative.Configuration
            {
                IceServers = nativeIceServers.Pointer,
                IceServersCount = nativeIceServers.Count,
                CertificateType = LibDataChannelNative.CertificateType.Ecdsa,
                IceTransportPolicy = configuration?.RelayOnly == true ? LibDataChannelNative.TransportPolicy.Relay : LibDataChannelNative.TransportPolicy.All,
                EnableIceTcp = configuration?.RelayOnly == true ? (byte)1 : (byte)0,
                DisableAutoNegotiation = 1,
                Mtu = 1280,
                MaxMessageSize = TerminalProtocol.MaximumRecordBytes
            };
            _peer = EnsureCreated(LibDataChannelNative.rtcCreatePeerConnection(in nativeConfiguration), "create the terminal peer");
            LibDataChannelNative.rtcSetUserPointer(_peer, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetLocalDescriptionCallback(_peer, _descriptionCallback), "listen for the terminal offer");
            EnsureSuccess(LibDataChannelNative.rtcSetStateChangeCallback(_peer, _stateCallback), "listen for terminal state");
            EnsureSuccess(LibDataChannelNative.rtcSetGatheringStateChangeCallback(_peer, _gatheringCallback), "listen for terminal candidates");
            _channel = EnsureCreated(LibDataChannelNative.rtcCreateDataChannel(_peer, TerminalProtocol.DataChannelLabel), "create the terminal channel");
            LibDataChannelNative.rtcSetUserPointer(_channel, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_channel, _openCallback), "listen for terminal channel open");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_channel, _closedCallback), "listen for terminal channel closure");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_channel, _errorCallback), "listen for terminal errors");
            EnsureSuccess(LibDataChannelNative.rtcSetMessageCallback(_channel, _messageCallback), "receive terminal records");
        }
        catch { DisposeCore(); throw; }
    }

    public Task Opened => _opened.Task;
    public Task Closed => _closed.Task;
    public ChannelReader<byte[]> Messages => _messages.Reader;

    public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create the terminal offer");
        return await _offer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > ScreenViewProtocol.MaxSdpLength)
            throw new TerminalWebRtcException("The terminal answer was invalid.");
        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply the terminal answer");
    }

    public bool TrySend(byte[] record)
    {
        if (record.Length is < 1 or > TerminalProtocol.MaximumRecordBytes) return false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new TerminalWebRtcException("The terminal connection stopped.");
            int bufferedAmount = LibDataChannelNative.rtcGetBufferedAmount(_channel);
            if (!_channelOpen || !CanBufferRecord(bufferedAmount, record.Length)) return false;
            if (LibDataChannelNative.rtcSendMessage(_channel, record, record.Length) >= 0) return true;
            throw new TerminalWebRtcException("The terminal connection stopped.");
        }
    }

    public ValueTask DisposeAsync() { DisposeCore(); return ValueTask.CompletedTask; }

    private void CompleteOffer()
    {
        if (_turnTlsBridges.Any(bridge => bridge.FailureCode is not null)) { _offer.TrySetException(new TerminalWebRtcException("The TURN TLS connection failed.")); return; }
        int size = LibDataChannelNative.rtcGetLocalDescription(_peer, 0, 0);
        if (size <= 1 || size > ScreenViewProtocol.MaxSdpLength + 1) { _offer.TrySetException(new TerminalWebRtcException("The terminal offer was invalid.")); return; }
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int copied = LibDataChannelNative.rtcGetLocalDescription(_peer, buffer, size);
            string? sdp = copied > 1 ? Marshal.PtrToStringUTF8(buffer) : null;
            if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > ScreenViewProtocol.MaxSdpLength) _offer.TrySetException(new TerminalWebRtcException("The terminal offer was unavailable."));
            else _offer.TrySetResult(sdp);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void Stop(Exception? error = null)
    {
        lock (_gate) { if (_disposed || _stopped) return; _stopped = true; _channelOpen = false; }
        var exception = error ?? new TerminalWebRtcException("The terminal connection stopped.");
        _opened.TrySetException(exception);
        _offer.TrySetException(exception);
        _messages.Writer.TryComplete(exception);
        _closed.TrySetResult();
    }

    private void DisposeCore()
    {
        int channel; int peer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; channel = _channel; peer = _peer; _channel = 0; _peer = 0; _channelOpen = false;
        }
        _opened.TrySetCanceled(); _offer.TrySetCanceled(); _messages.Writer.TryComplete(); _closed.TrySetResult();
        if (channel > 0) { LibDataChannelNative.rtcSetUserPointer(channel, 0); _ = LibDataChannelNative.rtcDeleteDataChannel(channel); }
        if (peer > 0) { LibDataChannelNative.rtcSetUserPointer(peer, 0); _ = LibDataChannelNative.rtcClosePeerConnection(peer); _ = LibDataChannelNative.rtcDeletePeerConnection(peer); }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        foreach (ITurnTlsBridge bridge in _turnTlsBridges) bridge.Dispose();
    }

    private static TerminalWebRtcPeer? From(nint pointer) => pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as TerminalWebRtcPeer;
    private static void OnDescription(int peer, nint sdp, nint type, nint pointer) { }
    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer) { if (ShouldStopForPeerState(state)) From(pointer)?.Stop(); }
    internal static bool ShouldStopForPeerState(LibDataChannelNative.PeerState state) => state is LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Closed;
    internal static bool CanBufferRecord(int bufferedAmount, int recordLength) =>
        bufferedAmount >= 0 && recordLength is >= 1 and <= TerminalProtocol.MaximumRecordBytes &&
        bufferedAmount <= TerminalProtocol.MaximumBufferedAmountBytes - recordLength;
    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer) { if (state == LibDataChannelNative.GatheringState.Complete) From(pointer)?.CompleteOffer(); }
    private static void OnOpen(int id, nint pointer) { var owner = From(pointer); if (owner is null || id != owner._channel) return; lock (owner._gate) owner._channelOpen = true; owner._opened.TrySetResult(); }
    private static void OnClosed(int id, nint pointer) => From(pointer)?.Stop();
    private static void OnError(int id, nint message, nint pointer) => From(pointer)?.Stop();
    private static void OnMessage(int id, nint message, int size, nint pointer)
    {
        var owner = From(pointer);
        if (owner is null || id != owner._channel || size is < 1 or > TerminalProtocol.MaximumRecordBytes) { owner?.Stop(new TerminalWebRtcException("An invalid terminal record was received.")); return; }
        var bytes = new byte[size]; Marshal.Copy(message, bytes, 0, size);
        if (!owner._messages.Writer.TryWrite(bytes)) owner.Stop(new TerminalWebRtcException("The terminal receive queue is full."));
    }
    private static int EnsureCreated(int result, string operation) { if (result < 0) throw new TerminalWebRtcException($"Could not {operation} (native error {result})."); return result; }
    private static void EnsureSuccess(int result, string operation) { if (result < 0) throw new TerminalWebRtcException($"Could not {operation} (native error {result})."); }
}

internal sealed class TerminalWebRtcException(string message, Exception? innerException = null) : Exception(message, innerException);
