using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed record FileTransferPeerConfiguration(
    IReadOnlyList<string> IceServerUris,
    bool RelayOnly,
    string DataChannelLabel = FileTransferProtocol.DataChannelLabel,
    int MinimumRecordBytes = FileTransferProtocol.HeaderBytes,
    int MaximumRecordBytes = FileTransferProtocol.MaximumRecordBytes,
    long MaximumBufferedBytes = FileTransferProtocol.MaximumUnacknowledgedBytes,
    bool CoalesceIncomingMessages = false);

internal interface IFileTransferWebRtcPeer : IAsyncDisposable
{
    Task Opened { get; }
    ChannelReader<byte[]> Messages { get; }
    Task<string> CreateOfferAsync(CancellationToken cancellationToken);
    void ApplyAnswer(string answerSdp);
    bool TrySend(byte[] record);
}

internal interface IFileTransferWebRtcPeerFactory
{
    IFileTransferWebRtcPeer Create(FileTransferPeerConfiguration? configuration);
}

internal sealed class FileTransferWebRtcPeerFactory : IFileTransferWebRtcPeerFactory
{
    public IFileTransferWebRtcPeer Create(FileTransferPeerConfiguration? configuration) => new FileTransferWebRtcPeer(configuration);
}

internal sealed class IsolatedFileTransferWebRtcPeerFactory : IFileTransferWebRtcPeerFactory
{
    public IFileTransferWebRtcPeer Create(FileTransferPeerConfiguration? configuration) => new IsolatedFileTransferWebRtcPeer(configuration);

    private sealed class IsolatedFileTransferWebRtcPeer(FileTransferPeerConfiguration? configuration) : IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = CreateMessageChannel(configuration);
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Opened => _opened.Task;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("v=0\r\no=voltura 1 1 IN IP4 127.0.0.1\r\ns=Voltura Air isolated file transfer\r\nt=0 0\r\n");
        public void ApplyAnswer(string answerSdp)
        {
            if (string.IsNullOrWhiteSpace(answerSdp)) throw new FileTransferWebRtcException("The isolated answer was empty.");
            _opened.TrySetResult();
        }
        public bool TrySend(byte[] record) =>
            record.Length >= (configuration?.MinimumRecordBytes ?? FileTransferProtocol.HeaderBytes) &&
            record.Length <= (configuration?.MaximumRecordBytes ?? FileTransferProtocol.MaximumRecordBytes);
        public ValueTask DisposeAsync()
        {
            _opened.TrySetCanceled();
            _messages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private static Channel<byte[]> CreateMessageChannel(FileTransferPeerConfiguration? configuration) =>
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(
            configuration?.CoalesceIncomingMessages == true ? 1 : 16)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = configuration?.CoalesceIncomingMessages == true
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.Wait
        });
}

internal sealed class FileTransferWebRtcPeer : IFileTransferWebRtcPeer
{
    private readonly Lock _gate = new();
    private readonly Channel<byte[]> _messages;
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.MessageCallback _messageCallback;
    private readonly List<ITurnTlsBridge> _turnTlsBridges = [];
    private readonly int _minimumRecordBytes;
    private readonly int _maximumRecordBytes;
    private readonly long _maximumBufferedBytes;
    private readonly GCHandle _selfHandle;
    private int _peer;
    private int _channel;
    private bool _channelOpen;
    private bool _stopped;
    private bool _disposed;

    internal FileTransferWebRtcPeer(FileTransferPeerConfiguration? configuration)
        : this(configuration, static endpoint => new TurnTlsBridge(endpoint))
    {
    }

    internal FileTransferWebRtcPeer(FileTransferPeerConfiguration? configuration, Func<TurnTlsEndpoint, ITurnTlsBridge> createTurnTlsBridge)
    {
        string channelLabel = configuration?.DataChannelLabel ?? FileTransferProtocol.DataChannelLabel;
        _minimumRecordBytes = configuration?.MinimumRecordBytes ?? FileTransferProtocol.HeaderBytes;
        _maximumRecordBytes = configuration?.MaximumRecordBytes ?? FileTransferProtocol.MaximumRecordBytes;
        _maximumBufferedBytes = configuration?.MaximumBufferedBytes ?? FileTransferProtocol.MaximumUnacknowledgedBytes;
        if (string.IsNullOrWhiteSpace(channelLabel) || channelLabel.Length > 64 ||
            _minimumRecordBytes < 1 || _maximumRecordBytes < _minimumRecordBytes ||
            _maximumRecordBytes > FileTransferProtocol.MaximumRecordBytes || _maximumBufferedBytes < _maximumRecordBytes)
            throw new ArgumentOutOfRangeException(nameof(configuration));
        _messages = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(
            configuration?.CoalesceIncomingMessages == true ? 1 : 16)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = configuration?.CoalesceIncomingMessages == true
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.Wait
        });
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
                ForceMediaTransport = 0,
                Mtu = 1280,
                MaxMessageSize = _maximumRecordBytes
            };
            _peer = EnsureCreated(LibDataChannelNative.rtcCreatePeerConnection(in nativeConfiguration), "create the file-transfer peer");
            LibDataChannelNative.rtcSetUserPointer(_peer, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetLocalDescriptionCallback(_peer, _descriptionCallback), "listen for the file-transfer offer");
            EnsureSuccess(LibDataChannelNative.rtcSetStateChangeCallback(_peer, _stateCallback), "listen for file-transfer state");
            EnsureSuccess(LibDataChannelNative.rtcSetGatheringStateChangeCallback(_peer, _gatheringCallback), "listen for file-transfer candidates");
            _channel = EnsureCreated(LibDataChannelNative.rtcCreateDataChannel(_peer, channelLabel), "create the file-transfer channel");
            LibDataChannelNative.rtcSetUserPointer(_channel, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_channel, _openCallback), "listen for file-transfer channel open");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_channel, _closedCallback), "listen for file-transfer channel closure");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_channel, _errorCallback), "listen for file-transfer errors");
            EnsureSuccess(LibDataChannelNative.rtcSetMessageCallback(_channel, _messageCallback), "receive file-transfer records");
        }
        catch
        {
            DisposeCore();
            throw;
        }
    }

    public Task Opened => _opened.Task;
    public ChannelReader<byte[]> Messages => _messages.Reader;

    public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create the file-transfer offer");
        return await _offer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > ScreenViewProtocol.MaxSdpLength)
            throw new FileTransferWebRtcException("The file-transfer answer was invalid.");
        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply the file-transfer answer");
    }

    public bool TrySend(byte[] record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Length < _minimumRecordBytes || record.Length > _maximumRecordBytes) return false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new FileTransferWebRtcException("The file-transfer connection stopped.");
            if (!_channelOpen || LibDataChannelNative.rtcGetBufferedAmount(_channel) > _maximumBufferedBytes) return false;
            if (LibDataChannelNative.rtcSendMessage(_channel, record, record.Length) >= 0) return true;
            _stopped = true;
            _channelOpen = false;
            throw new FileTransferWebRtcException("The file-transfer connection stopped.");
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    private void CompleteOffer()
    {
        if (_turnTlsBridges.Any(bridge => bridge.FailureCode is not null))
        {
            _offer.TrySetException(new FileTransferWebRtcException("The TURN TLS connection failed."));
            return;
        }
        int size = LibDataChannelNative.rtcGetLocalDescription(_peer, 0, 0);
        if (size <= 1 || size > ScreenViewProtocol.MaxSdpLength + 1)
        {
            _offer.TrySetException(new FileTransferWebRtcException("The file-transfer offer was invalid."));
            return;
        }
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int copied = LibDataChannelNative.rtcGetLocalDescription(_peer, buffer, size);
            string? sdp = copied > 1 ? Marshal.PtrToStringUTF8(buffer) : null;
            if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > ScreenViewProtocol.MaxSdpLength)
                _offer.TrySetException(new FileTransferWebRtcException("The file-transfer offer was unavailable."));
            else _offer.TrySetResult(sdp);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void Stop(Exception? error = null)
    {
        lock (_gate)
        {
            if (_disposed || _stopped) return;
            _stopped = true;
            _channelOpen = false;
        }
        var exception = error ?? new FileTransferWebRtcException("The file-transfer connection stopped.");
        _opened.TrySetException(exception);
        _offer.TrySetException(exception);
        _messages.Writer.TryComplete(exception);
    }

    private void DisposeCore()
    {
        int channel;
        int peer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            channel = _channel;
            peer = _peer;
            _channel = 0;
            _peer = 0;
            _channelOpen = false;
        }
        _opened.TrySetCanceled();
        _offer.TrySetCanceled();
        _messages.Writer.TryComplete();
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
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        foreach (ITurnTlsBridge bridge in _turnTlsBridges) bridge.Dispose();
        _turnTlsBridges.Clear();
    }

    private static FileTransferWebRtcPeer? From(nint pointer) => pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as FileTransferWebRtcPeer;
    private static void OnDescription(int peer, nint sdp, nint type, nint pointer) { }
    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer)
    {
        if (state is LibDataChannelNative.PeerState.Disconnected or LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Closed) From(pointer)?.Stop();
    }
    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer)
    {
        if (state == LibDataChannelNative.GatheringState.Complete) From(pointer)?.CompleteOffer();
    }
    private static void OnOpen(int id, nint pointer)
    {
        var owner = From(pointer);
        if (owner is null || id != owner._channel) return;
        lock (owner._gate) owner._channelOpen = true;
        owner._opened.TrySetResult();
    }
    private static void OnClosed(int id, nint pointer) => From(pointer)?.Stop();
    private static void OnError(int id, nint message, nint pointer) => From(pointer)?.Stop();
    private static void OnMessage(int id, nint message, int size, nint pointer)
    {
        var owner = From(pointer);
        if (owner is null || id != owner._channel || size < owner._minimumRecordBytes || size > owner._maximumRecordBytes)
        {
            owner?.Stop(new FileTransferWebRtcException("An invalid file-transfer record was received."));
            return;
        }
        var bytes = new byte[size];
        Marshal.Copy(message, bytes, 0, size);
        if (!owner._messages.Writer.TryWrite(bytes)) owner.Stop(new FileTransferWebRtcException("The file-transfer receive queue is full."));
    }
    private static int EnsureCreated(int result, string operation)
    {
        if (result < 0) throw new FileTransferWebRtcException($"Could not {operation} (native error {result}).");
        return result;
    }
    private static void EnsureSuccess(int result, string operation)
    {
        if (result < 0) throw new FileTransferWebRtcException($"Could not {operation} (native error {result}).");
    }
}

internal sealed class FileTransferWebRtcException(string message, Exception? innerException = null) : Exception(message, innerException);
