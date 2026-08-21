using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VolturaAir.Host;

internal interface IScreenViewWebRtcPeer : IDisposable
{
    event EventHandler? Stopped;
    event EventHandler? KeyFrameRequested;
    Task Connected { get; }
    Task<string> CreateOfferAsync(CancellationToken cancellationToken);
    void ApplyAnswer(string answerSdp);
    bool TrySendH264(byte[] accessUnit, int framesPerSecond);
    bool TrySendEvent(byte[] eventBytes);
}

internal interface IScreenViewWebRtcPeerFactory
{
    IScreenViewWebRtcPeer Create();
    IScreenViewWebRtcPeer Create(ScreenViewPeerConfiguration? configuration) => Create();
}

internal sealed record ScreenViewPeerConfiguration(IReadOnlyList<string> IceServerUris, bool RelayOnly);

internal sealed class ScreenViewWebRtcPeerFactory : IScreenViewWebRtcPeerFactory
{
    public IScreenViewWebRtcPeer Create() => new ScreenViewWebRtcPeer();
    public IScreenViewWebRtcPeer Create(ScreenViewPeerConfiguration? configuration) => new ScreenViewWebRtcPeer(configuration);
}

internal sealed class IsolatedScreenViewWebRtcPeerFactory : IScreenViewWebRtcPeerFactory
{
    public IScreenViewWebRtcPeer Create() => new IsolatedScreenViewWebRtcPeer();

    private sealed class IsolatedScreenViewWebRtcPeer : IScreenViewWebRtcPeer
    {
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;
        public event EventHandler? Stopped;
        public event EventHandler? KeyFrameRequested;
        public Task Connected => _connected.Task;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Task.FromResult("v=0\r\no=voltura 1 1 IN IP4 127.0.0.1\r\ns=Voltura Air isolated screen test\r\nt=0 0\r\n");
        }
        public void ApplyAnswer(string answerSdp)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(answerSdp)) throw new ScreenViewWebRtcException("The isolated answer was empty.");
            _connected.TrySetResult();
        }
        public bool TrySendH264(byte[] accessUnit, int framesPerSecond) => !_disposed && accessUnit.Length > 0 && framesPerSecond > 0;
        public bool TrySendEvent(byte[] eventBytes) => !_disposed && eventBytes.Length > 0;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connected.TrySetCanceled();
            _ = Stopped;
            _ = KeyFrameRequested;
        }
    }
}

internal sealed class ScreenViewWebRtcPeer : IScreenViewWebRtcPeer
{
    private const int H264PayloadType = 102;
    internal const string H264FormatParameters = "profile-level-id=42e034;packetization-mode=1;level-asymmetry-allowed=1";
    private const uint H264ClockRate = 90_000;
    private const uint MaximumStoredRtpPackets = 256;
    private static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(10);
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly GCHandle _selfHandle;
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.PliCallback _pliCallback;
    private readonly List<ITurnTlsBridge> _turnTlsBridges = [];
    private int _peer;
    private int _track;
    private int _eventsChannel;
    private uint _rtpTimestamp;
    private long _lastVideoSendTimestamp;
    private bool _trackOpen;
    private bool _eventsOpen;
    private bool _transportConnected;
    private bool _disposed;

    public ScreenViewWebRtcPeer(ScreenViewPeerConfiguration? peerConfiguration = null)
        : this(peerConfiguration, static endpoint => new TurnTlsBridge(endpoint))
    {
    }

    internal ScreenViewWebRtcPeer(
        ScreenViewPeerConfiguration? peerConfiguration,
        Func<TurnTlsEndpoint, ITurnTlsBridge> createTurnTlsBridge)
    {
        ArgumentNullException.ThrowIfNull(createTurnTlsBridge);
        _descriptionCallback = OnDescription;
        _stateCallback = OnState;
        _gatheringCallback = OnGathering;
        _openCallback = OnOpen;
        _closedCallback = OnClosed;
        _errorCallback = OnError;
        _pliCallback = OnPictureLoss;
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        nint pointer = GCHandle.ToIntPtr(_selfHandle);
        try
        {
            IReadOnlyList<string> configuredIceServers = peerConfiguration?.RelayOnly == true
                ? TurnTlsIceServerMapper.Map(peerConfiguration.IceServerUris, endpoint =>
                {
                    ITurnTlsBridge bridge = createTurnTlsBridge(endpoint);
                    _turnTlsBridges.Add(bridge);
                    return bridge.LocalIceServerUri;
                })
                : peerConfiguration?.IceServerUris ?? [];
            using var iceServers = new NativeIceServerList(configuredIceServers);
            var configuration = new LibDataChannelNative.Configuration
            {
                IceServers = iceServers.Pointer,
                IceServersCount = iceServers.Count,
                CertificateType = LibDataChannelNative.CertificateType.Ecdsa,
                IceTransportPolicy = peerConfiguration?.RelayOnly == true
                    ? LibDataChannelNative.TransportPolicy.Relay
                    : LibDataChannelNative.TransportPolicy.All,
                EnableIceTcp = peerConfiguration?.RelayOnly == true ? (byte)1 : (byte)0,
                DisableAutoNegotiation = 1,
                ForceMediaTransport = 1,
                Mtu = 1280
            };
            _peer = EnsureCreated(LibDataChannelNative.rtcCreatePeerConnection(in configuration), "create the WebRTC peer");
            LibDataChannelNative.rtcSetUserPointer(_peer, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetLocalDescriptionCallback(_peer, _descriptionCallback), "listen for the WebRTC offer");
            EnsureSuccess(LibDataChannelNative.rtcSetStateChangeCallback(_peer, _stateCallback), "listen for WebRTC state changes");
            EnsureSuccess(LibDataChannelNative.rtcSetGatheringStateChangeCallback(_peer, _gatheringCallback), "listen for WebRTC candidate gathering");

            using var mid = new Utf8String("0");
            using var name = new Utf8String("screen-video");
            using var stream = new Utf8String("voltura-screen");
            using var trackId = new Utf8String("screen-video");
            using var profile = new Utf8String(H264FormatParameters);
            uint ssrc = RandomUInt32();
            if (ssrc == 0) ssrc = 1;
            var trackInitialization = new LibDataChannelNative.TrackInit
            {
                Direction = LibDataChannelNative.Direction.SendOnly,
                Codec = LibDataChannelNative.Codec.H264,
                PayloadType = H264PayloadType,
                Ssrc = ssrc,
                Mid = mid.Pointer,
                Name = name.Pointer,
                Msid = stream.Pointer,
                TrackId = trackId.Pointer,
                Profile = profile.Pointer
            };
            _track = EnsureCreated(LibDataChannelNative.rtcAddTrackEx(_peer, in trackInitialization), "create the WebRTC video track");
            LibDataChannelNative.rtcSetUserPointer(_track, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_track, _openCallback), "listen for the video track");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_track, _closedCallback), "listen for video track closure");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_track, _errorCallback), "listen for video track errors");

            using var cname = new Utf8String("voltura-air");
            _rtpTimestamp = RandomUInt32();
            var packetizerInitialization = new LibDataChannelNative.PacketizerInit
            {
                Ssrc = ssrc,
                Cname = cname.Pointer,
                PayloadType = H264PayloadType,
                ClockRate = H264ClockRate,
                SequenceNumber = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1),
                Timestamp = _rtpTimestamp,
                MaxFragmentSize = 1200,
                NalSeparator = LibDataChannelNative.NalUnitSeparator.StartSequence
            };
            EnsureSuccess(LibDataChannelNative.rtcSetH264Packetizer(_track, in packetizerInitialization), "configure H.264 packetization");
            EnsureSuccess(LibDataChannelNative.rtcChainRtcpSrReporter(_track), "configure WebRTC sender reports");
            EnsureSuccess(LibDataChannelNative.rtcChainRtcpNackResponder(_track, MaximumStoredRtpPackets), "configure WebRTC retransmission");
            EnsureSuccess(LibDataChannelNative.rtcChainPliHandler(_track, _pliCallback), "listen for keyframe requests");

            _eventsChannel = EnsureCreated(LibDataChannelNative.rtcCreateDataChannel(_peer, "screen-events"), "create the screen event channel");
            LibDataChannelNative.rtcSetUserPointer(_eventsChannel, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_eventsChannel, _openCallback), "listen for the screen event channel");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_eventsChannel, _closedCallback), "listen for screen event channel closure");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_eventsChannel, _errorCallback), "listen for screen event channel errors");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event EventHandler? Stopped;
    public event EventHandler? KeyFrameRequested;

    public Task Connected => _connected.Task;

    public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create the WebRTC offer");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OfferTimeout);
        return await _offer.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
    }

    public void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply the WebRTC answer");
    }

    public bool TrySendH264(byte[] accessUnit, int framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(accessUnit);
        if (accessUnit.Length == 0 || framesPerSecond is < 1 or > 60) return false;
        lock (_gate)
        {
            if (_disposed || !_trackOpen || LibDataChannelNative.rtcGetBufferedAmount(_track) > 1_000_000) return false;
            long now = TimeProvider.System.GetTimestamp();
            if (_lastVideoSendTimestamp != 0)
            {
                double elapsedSeconds = TimeProvider.System.GetElapsedTime(_lastVideoSendTimestamp, now).TotalSeconds;
                uint increment = (uint)Math.Clamp(
                    (long)Math.Round(elapsedSeconds * H264ClockRate),
                    H264ClockRate / 60,
                    H264ClockRate);
                _rtpTimestamp = unchecked(_rtpTimestamp + increment);
            }
            EnsureSuccess(LibDataChannelNative.rtcSetTrackRtpTimestamp(_track, _rtpTimestamp), "set the screen video timestamp");
            int result = LibDataChannelNative.rtcSendMessage(_track, accessUnit, accessUnit.Length);
            if (result < 0) return false;
            _lastVideoSendTimestamp = now;
            return true;
        }
    }

    public bool TrySendEvent(byte[] eventBytes)
    {
        ArgumentNullException.ThrowIfNull(eventBytes);
        if (eventBytes.Length == 0 || eventBytes.Length > 64 * 1024) return false;
        lock (_gate)
        {
            return !_disposed &&
                _eventsChannel > 0 &&
                _eventsOpen &&
                LibDataChannelNative.rtcGetBufferedAmount(_eventsChannel) <= 128 * 1024 &&
                LibDataChannelNative.rtcSendMessage(_eventsChannel, eventBytes, eventBytes.Length) >= 0;
        }
    }

    public void Dispose()
    {
        int eventsChannel;
        int track;
        int peer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _connected.TrySetCanceled();
            _offer.TrySetCanceled();
            eventsChannel = _eventsChannel;
            track = _track;
            peer = _peer;
            _eventsChannel = 0;
            _track = 0;
            _peer = 0;
            _eventsOpen = false;
            _trackOpen = false;
        }
        if (eventsChannel > 0) _ = LibDataChannelNative.rtcDeleteDataChannel(eventsChannel);
        if (track > 0) _ = LibDataChannelNative.rtcDeleteTrack(track);
        if (peer > 0)
        {
            _ = LibDataChannelNative.rtcClosePeerConnection(peer);
            _ = LibDataChannelNative.rtcDeletePeerConnection(peer);
        }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        foreach (ITurnTlsBridge bridge in _turnTlsBridges) bridge.Dispose();
        _turnTlsBridges.Clear();
    }

    private void CompleteOffer()
    {
        if (_turnTlsBridges.FirstOrDefault(bridge => bridge.FailureCode is not null) is { FailureCode: not null })
        {
            _offer.TrySetException(new ScreenViewWebRtcException("The TURN TLS connection failed."));
            return;
        }
        int size = LibDataChannelNative.rtcGetLocalDescription(_peer, 0, 0);
        if (size <= 1 || size > ScreenViewProtocol.MaxSdpLength + 1)
        {
            _offer.TrySetException(new ScreenViewWebRtcException("The generated WebRTC offer was invalid."));
            return;
        }
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int copied = LibDataChannelNative.rtcGetLocalDescription(_peer, buffer, size);
            if (copied <= 1)
            {
                _offer.TrySetException(new ScreenViewWebRtcException("The generated WebRTC offer was unavailable."));
                return;
            }
            string? sdp = Marshal.PtrToStringUTF8(buffer);
            if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > ScreenViewProtocol.MaxSdpLength)
            {
                _offer.TrySetException(new ScreenViewWebRtcException("The generated WebRTC offer exceeded its limit."));
                return;
            }
            _offer.TrySetResult(sdp);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ScreenViewWebRtcPeer? From(nint pointer) =>
        pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as ScreenViewWebRtcPeer;

    private static void OnDescription(int peer, nint sdp, nint type, nint pointer)
    {
        _ = peer;
        _ = sdp;
        _ = type;
        _ = pointer;
    }

    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer)
    {
        _ = peer;
        ScreenViewWebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        if (state == LibDataChannelNative.PeerState.Connected)
        {
            lock (owner._gate)
            {
                owner._transportConnected = true;
                owner.TryCompleteConnected();
            }
        }
        else if (state is LibDataChannelNative.PeerState.Disconnected or LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Closed)
        {
            owner._connected.TrySetException(new ScreenViewWebRtcException("The WebRTC screen connection stopped."));
            owner.Stopped?.Invoke(owner, EventArgs.Empty);
        }
    }

    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer)
    {
        _ = peer;
        if (state == LibDataChannelNative.GatheringState.Complete) From(pointer)?.CompleteOffer();
    }

    private static void OnOpen(int id, nint pointer)
    {
        ScreenViewWebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        lock (owner._gate)
        {
            if (id == owner._track) owner._trackOpen = true;
            if (id == owner._eventsChannel) owner._eventsOpen = true;
            owner.TryCompleteConnected();
        }
    }

    private static void OnClosed(int id, nint pointer)
    {
        ScreenViewWebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        lock (owner._gate)
        {
            if (id == owner._track) owner._trackOpen = false;
            if (id == owner._eventsChannel) owner._eventsOpen = false;
        }
        if (!owner._disposed) owner.Stopped?.Invoke(owner, EventArgs.Empty);
    }

    private static void OnError(int id, nint message, nint pointer)
    {
        _ = id;
        _ = message;
        ScreenViewWebRtcPeer? owner = From(pointer);
        if (owner is not null && !owner._disposed) owner.Stopped?.Invoke(owner, EventArgs.Empty);
    }

    private static void OnPictureLoss(int track, nint pointer)
    {
        _ = track;
        ScreenViewWebRtcPeer? owner = From(pointer);
        owner?.KeyFrameRequested?.Invoke(owner, EventArgs.Empty);
    }


    private static int EnsureCreated(int result, string operation)
    {
        if (result < 0) throw new ScreenViewWebRtcException($"Could not {operation} (native error {result}).");
        return result;
    }

    private static void EnsureSuccess(int result, string operation)
    {
        if (result < 0) throw new ScreenViewWebRtcException($"Could not {operation} (native error {result}).");
    }

    private static uint RandomUInt32() => BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint)));

    private void TryCompleteConnected()
    {
        if (_transportConnected && _trackOpen && _eventsOpen) _connected.TrySetResult();
    }

}


internal static class ScreenViewProtocol
{
    internal const int MaxSdpLength = 32 * 1024;
}

internal sealed class ScreenViewWebRtcException(string message, Exception? innerException = null) : Exception(message, innerException);
