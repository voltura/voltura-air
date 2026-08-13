using System.Runtime.InteropServices;
using VolturaAir.Host;

namespace WebRtcSpike.Host;

internal sealed class WebRtcPeer : IAsyncDisposable
{
    private const int MaximumSdpLength = 32 * 1024;
    private const int CandidateBufferLength = 4096;
    private const int H264PayloadType = 102;
    private const string H264Profile = "profile-level-id=42e028;packetization-mode=1;level-asymmetry-allowed=1";
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _trackOpen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.MessageCallback _messageCallback;
    private readonly H264RtpDepacketizer _depacketizer = new();
    private readonly List<ITurnTlsBridge> _turnTlsBridges = [];
    private readonly GCHandle _selfHandle;
    private int _peer;
    private int _track;
    private bool _receiveFailureReported;
    private bool _disposed;

    internal WebRtcPeer(RelayTurnConfiguration? relay = null)
    {
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
            IReadOnlyList<string> configuredIceServers = relay is null
                ? []
                : TurnTlsIceServerMapper.Map(relay.HostIceServerUris, endpoint =>
                {
                    var bridge = new TurnTlsBridge(endpoint);
                    _turnTlsBridges.Add(bridge);
                    return bridge.LocalIceServerUri;
                });
            using var iceServers = new NativeIceServerList(configuredIceServers);
            var configuration = new LibDataChannelNative.Configuration
            {
                IceServers = iceServers.Pointer,
                IceServersCount = iceServers.Count,
                CertificateType = LibDataChannelNative.CertificateType.Ecdsa,
                IceTransportPolicy = relay is null
                    ? LibDataChannelNative.TransportPolicy.All
                    : LibDataChannelNative.TransportPolicy.Relay,
                EnableIceTcp = relay is null ? (byte)0 : (byte)1,
                DisableAutoNegotiation = 1,
                ForceMediaTransport = 1,
                Mtu = 1280
            };
            _peer = EnsureCreated(LibDataChannelNative.rtcCreatePeerConnection(in configuration), "create peer connection");
            LibDataChannelNative.rtcSetUserPointer(_peer, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetLocalDescriptionCallback(_peer, _descriptionCallback), "set description callback");
            EnsureSuccess(LibDataChannelNative.rtcSetStateChangeCallback(_peer, _stateCallback), "set state callback");
            EnsureSuccess(LibDataChannelNative.rtcSetGatheringStateChangeCallback(_peer, _gatheringCallback), "set gathering callback");

            using var mid = new Utf8String("0");
            using var name = new Utf8String("phone-video");
            using var stream = new Utf8String("voltura-webcam");
            using var trackId = new Utf8String("phone-video");
            using var profile = new Utf8String(H264Profile);
            var initialization = new LibDataChannelNative.TrackInit
            {
                Direction = LibDataChannelNative.Direction.ReceiveOnly,
                Codec = LibDataChannelNative.Codec.H264,
                PayloadType = H264PayloadType,
                Mid = mid.Pointer,
                Name = name.Pointer,
                Msid = stream.Pointer,
                TrackId = trackId.Pointer,
                Profile = profile.Pointer
            };
            _track = EnsureCreated(LibDataChannelNative.rtcAddTrackEx(_peer, in initialization), "create H.264 receive track");
            LibDataChannelNative.rtcSetUserPointer(_track, pointer);
            EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_track, _openCallback), "set track open callback");
            EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_track, _closedCallback), "set track close callback");
            EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_track, _errorCallback), "set track error callback");
            EnsureSuccess(LibDataChannelNative.rtcSetMessageCallback(_track, _messageCallback), "set RTP callback");
            EnsureSuccess(LibDataChannelNative.rtcChainRtcpReceivingSession(_track), "configure RTCP receiver reports");
        }
        catch
        {
            DisposeNative();
            throw;
        }
    }

    internal event Action<byte[], uint>? AccessUnitReceived;
    internal Task TrackOpen => _trackOpen.Task;

    internal void RequestKeyFrame()
    {
        int track = Volatile.Read(ref _track);
        if (track > 0) _ = LibDataChannelNative.rtcRequestKeyframe(track);
    }

    internal async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create complete offer");
        return await _offer.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
    }

    internal void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > MaximumSdpLength || !answerSdp.StartsWith("v=0", StringComparison.Ordinal))
            throw new InvalidOperationException("The browser answer was empty, invalid, or too large.");
        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply browser answer");
    }

    internal void PrintSelectedRoute()
    {
        nint local = Marshal.AllocHGlobal(CandidateBufferLength);
        nint remote = Marshal.AllocHGlobal(CandidateBufferLength);
        try
        {
            int result = LibDataChannelNative.rtcGetSelectedCandidatePair(_peer, local, CandidateBufferLength, remote, CandidateBufferLength);
            Console.WriteLine(result < 0
                ? $"Selected candidate pair: unavailable (native error {result})"
                : $"Selected candidate pair: local {Marshal.PtrToStringUTF8(local)}; remote {Marshal.PtrToStringUTF8(remote)}");
        }
        finally
        {
            Marshal.FreeHGlobal(local);
            Marshal.FreeHGlobal(remote);
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeNative();
        return ValueTask.CompletedTask;
    }

    private void ReceiveRtp(nint message, int size)
    {
        if (size <= 0 || size > 64 * 1024) return;
        byte[] packet = new byte[size];
        Marshal.Copy(message, packet, 0, size);
        H264DepacketizeResult result;
        lock (_depacketizer)
        {
            result = _depacketizer.Push(packet);
        }
        if (result.RequestKeyFrame) RequestKeyFrame();
        if (result is { AccessUnit: not null, RtpTimestamp: not null })
        {
            AccessUnitReceived?.Invoke(result.AccessUnit, result.RtpTimestamp.Value);
            _receiveFailureReported = false;
        }
    }

    private void ReportReceiveFailure(Exception exception)
    {
        if (_receiveFailureReported) return;
        _receiveFailureReported = true;
        Console.Error.WriteLine($"RTP receive rejected a packet: {exception.Message}");
    }

    private void DisposeNative()
    {
        if (_disposed) return;
        _disposed = true;
        _offer.TrySetCanceled();
        _trackOpen.TrySetCanceled();
        int track = Interlocked.Exchange(ref _track, 0);
        int peer = Interlocked.Exchange(ref _peer, 0);
        if (track > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(track, 0);
            _ = LibDataChannelNative.rtcDeleteTrack(track);
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
        _depacketizer.Dispose();
    }

    private void CompleteOffer()
    {
        if (_turnTlsBridges.FirstOrDefault(bridge => bridge.FailureCode is not null) is { FailureCode: not null })
        {
            _offer.TrySetException(new InvalidOperationException("The TURN TLS connection failed."));
            return;
        }
        int size = LibDataChannelNative.rtcGetLocalDescription(_peer, 0, 0);
        if (size <= 1 || size > MaximumSdpLength + 1)
        {
            _offer.TrySetException(new InvalidOperationException("The generated offer was unavailable or too large."));
            return;
        }
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int result = LibDataChannelNative.rtcGetLocalDescription(_peer, buffer, size);
            string? sdp = result > 1 ? Marshal.PtrToStringUTF8(buffer) : null;
            if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > MaximumSdpLength ||
                _turnTlsBridges.Count > 0 && !HasOnlyRelayCandidates(sdp))
                _offer.TrySetException(new InvalidOperationException("The generated offer was invalid."));
            else
                _offer.TrySetResult(sdp);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool HasOnlyRelayCandidates(string sdp)
    {
        string[] candidates = sdp.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("a=candidate:", StringComparison.Ordinal))
            .ToArray();
        return candidates.Length > 0 && candidates.All(line => line.Contains(" typ relay", StringComparison.Ordinal));
    }

    private static WebRtcPeer? From(nint pointer) => pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as WebRtcPeer;
    private static void OnDescription(int peer, nint sdp, nint type, nint pointer) { _ = peer; _ = sdp; _ = type; _ = pointer; }
    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer)
    {
        _ = peer;
        WebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        Console.WriteLine($"ICE: {state.ToString().ToLowerInvariant()}");
        if (state is LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Disconnected)
            Console.WriteLine("Transport lost. The original peer and host remain; press Ctrl+C to stop.");
    }
    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer)
    {
        _ = peer;
        WebRtcPeer? owner = From(pointer);
        Console.WriteLine($"ICE gathering: {state.ToString().ToLowerInvariant()}");
        if (owner is not null && state == LibDataChannelNative.GatheringState.Complete) owner.CompleteOffer();
    }
    private static void OnOpen(int id, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._track) return;
        Console.WriteLine("Video track: open");
        owner._trackOpen.TrySetResult();
    }
    private static void OnClosed(int id, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is not null && id == owner._track) Console.WriteLine("Video track: closed; waiting frame remains active.");
    }
    private static void OnError(int id, nint message, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._track) return;
        string detail = Marshal.PtrToStringUTF8(message) ?? "unknown native error";
        owner._trackOpen.TrySetException(new InvalidOperationException($"Video track error: {detail}"));
    }
    private static void OnMessage(int id, nint message, int size, nint pointer)
    {
        WebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._track) return;
        try
        {
            owner.ReceiveRtp(message, size);
        }
        catch (Exception exception)
        {
            owner.ReportReceiveFailure(exception);
        }
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
