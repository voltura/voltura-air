using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VolturaAir.Host;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal interface IPhoneWebcamWebRtcPeer : IAsyncDisposable
{
    event Action<byte[], uint>? AccessUnitReceived;
    event Action<byte[]>? OpusPacketReceived
    {
        add { }
        remove { }
    }
    event EventHandler? Stopped;
    Task TrackOpen { get; }
    Task MediaOpen => TrackOpen;
    Task<string> CreateOfferAsync(CancellationToken cancellationToken);
    void ApplyAnswer(string answerSdp);
    void RequestKeyFrame();
}

internal interface IPhoneWebcamWebRtcPeerFactory
{
    IPhoneWebcamWebRtcPeer Create(RelayTurnConfiguration? relay);
    IPhoneWebcamWebRtcPeer Create(RelayTurnConfiguration? relay, bool useMicrophone) => Create(relay);
}

internal sealed class PhoneWebcamWebRtcPeerFactory : IPhoneWebcamWebRtcPeerFactory
{
    public IPhoneWebcamWebRtcPeer Create(RelayTurnConfiguration? relay) => new PhoneWebcamWebRtcPeer(relay);
    public IPhoneWebcamWebRtcPeer Create(RelayTurnConfiguration? relay, bool useMicrophone) => new PhoneWebcamWebRtcPeer(relay, useMicrophone);
}

internal sealed class PhoneWebcamWebRtcPeer : IPhoneWebcamWebRtcPeer
{
    internal const int MaximumAudioRtpPacketBytes = 2048;
    private const int MaximumSdpLength = 32 * 1024;
    private const int CandidateBufferLength = 4096;
    private const int H264PayloadType = 102;
    private const int OpusPayloadType = 111;
    private const string H264Profile = "profile-level-id=42e028;packetization-mode=1;level-asymmetry-allowed=1";
    private readonly TaskCompletionSource<string> _offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _trackOpen = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource? _audioTrackOpen;
    private readonly LibDataChannelNative.DescriptionCallback _descriptionCallback;
    private readonly LibDataChannelNative.StateCallback _stateCallback;
    private readonly LibDataChannelNative.GatheringCallback _gatheringCallback;
    private readonly LibDataChannelNative.OpenCallback _openCallback;
    private readonly LibDataChannelNative.ClosedCallback _closedCallback;
    private readonly LibDataChannelNative.ErrorCallback _errorCallback;
    private readonly LibDataChannelNative.MessageCallback _messageCallback;
    private readonly H264RtpDepacketizer _depacketizer = new();
    private readonly bool _relayOnly;
    private readonly bool _useMicrophone;
    private readonly List<ITurnTlsBridge> _turnTlsBridges = [];
    private readonly GCHandle _selfHandle;
    private int _peer;
    private int _track;
    private int _audioTrack;
    private bool _receiveFailureReported;
    private bool _disposed;

    internal PhoneWebcamWebRtcPeer(RelayTurnConfiguration? relay = null, bool useMicrophone = false)
    {
        _audioTrackOpen = useMicrophone
            ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        _useMicrophone = useMicrophone;
        _relayOnly = relay is not null;
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
            uint videoSsrc = CreateSsrc();
            uint audioSsrc = useMicrophone ? CreateSsrc(videoSsrc) : 0;
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
                Ssrc = videoSsrc,
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

            if (useMicrophone)
            {
                using var audioMid = new Utf8String("1");
                using var audioName = new Utf8String("phone-audio");
                using var audioTrackId = new Utf8String("phone-audio");
                var audioInitialization = new LibDataChannelNative.TrackInit
                {
                    Direction = LibDataChannelNative.Direction.ReceiveOnly,
                    Codec = LibDataChannelNative.Codec.Opus,
                    PayloadType = OpusPayloadType,
                    Ssrc = audioSsrc,
                    Mid = audioMid.Pointer,
                    Name = audioName.Pointer,
                    Msid = stream.Pointer,
                    TrackId = audioTrackId.Pointer
                };
                _audioTrack = EnsureCreated(LibDataChannelNative.rtcAddTrackEx(_peer, in audioInitialization), "create Opus receive track");
                LibDataChannelNative.rtcSetUserPointer(_audioTrack, pointer);
                EnsureSuccess(LibDataChannelNative.rtcSetOpenCallback(_audioTrack, _openCallback), "set audio track open callback");
                EnsureSuccess(LibDataChannelNative.rtcSetClosedCallback(_audioTrack, _closedCallback), "set audio track close callback");
                EnsureSuccess(LibDataChannelNative.rtcSetErrorCallback(_audioTrack, _errorCallback), "set audio track error callback");
                EnsureSuccess(LibDataChannelNative.rtcSetMessageCallback(_audioTrack, _messageCallback), "set audio RTP callback");
                EnsureSuccess(LibDataChannelNative.rtcChainRtcpReceivingSession(_audioTrack), "configure audio RTCP receiver reports");
            }
        }
        catch
        {
            DisposeNative();
            throw;
        }
    }

    public event Action<byte[], uint>? AccessUnitReceived;
    public event Action<byte[]>? OpusPacketReceived;
    public event EventHandler? Stopped;
    public Task TrackOpen => _trackOpen.Task;
    public Task MediaOpen => _audioTrackOpen is null
        ? _trackOpen.Task
        : Task.WhenAll(_trackOpen.Task, _audioTrackOpen.Task);

    public void RequestKeyFrame()
    {
        int track = Volatile.Read(ref _track);
        if (track > 0) _ = LibDataChannelNative.rtcRequestKeyframe(track);
    }

    public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSuccess(LibDataChannelNative.rtcSetLocalDescription(_peer, "offer"), "create complete offer");
        return await _offer.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
    }

    public void ApplyAnswer(string answerSdp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > MaximumSdpLength || !answerSdp.StartsWith("v=0", StringComparison.Ordinal))
            throw new InvalidOperationException("The browser answer was empty, invalid, or too large.");
        if (!HasExpectedMedia(answerSdp, _useMicrophone))
            throw new InvalidOperationException("The browser answer did not contain the expected H.264 and Opus media sections.");
        if (_relayOnly && !HasOnlyRelayCandidates(answerSdp))
            throw new InvalidOperationException("The browser answer was not relay-only.");
        EnsureSuccess(LibDataChannelNative.rtcSetRemoteDescription(_peer, answerSdp, "answer"), "apply browser answer");
    }

    internal string GetSelectedRouteDescription()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint local = Marshal.AllocHGlobal(CandidateBufferLength);
        nint remote = Marshal.AllocHGlobal(CandidateBufferLength);
        try
        {
            int result = LibDataChannelNative.rtcGetSelectedCandidatePair(
                _peer,
                local,
                CandidateBufferLength,
                remote,
                CandidateBufferLength);
            return result < 0
                ? $"unavailable (native error {result})"
                : $"local {Marshal.PtrToStringUTF8(local)}; remote {Marshal.PtrToStringUTF8(remote)}";
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

    private void ReceiveAudioRtp(nint message, int size)
    {
        byte[]? packet = CopyAudioRtpPacket(message, size);
        if (packet is null) return;
        OpusPacketReceived?.Invoke(packet);
    }

    internal static byte[]? CopyAudioRtpPacket(nint message, int size)
    {
        if (size <= 0 || size > MaximumAudioRtpPacketBytes) return null;
        var packet = new byte[size];
        Marshal.Copy(message, packet, 0, size);
        return packet;
    }

    private void ReportReceiveFailure(Exception exception)
    {
        if (_receiveFailureReported) return;
        _receiveFailureReported = true;
        _ = exception;
    }

    private void DisposeNative()
    {
        if (_disposed) return;
        _disposed = true;
        _offer.TrySetCanceled();
        _trackOpen.TrySetCanceled();
        _audioTrackOpen?.TrySetCanceled();
        int track = Interlocked.Exchange(ref _track, 0);
        int audioTrack = Interlocked.Exchange(ref _audioTrack, 0);
        int peer = Interlocked.Exchange(ref _peer, 0);
        if (track > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(track, 0);
            _ = LibDataChannelNative.rtcDeleteTrack(track);
        }
        if (audioTrack > 0)
        {
            LibDataChannelNative.rtcSetUserPointer(audioTrack, 0);
            _ = LibDataChannelNative.rtcDeleteTrack(audioTrack);
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
                !HasDistinctMediaSsrcs(sdp, _useMicrophone) ||
                _turnTlsBridges.Count > 0 && !HasOnlyRelayCandidates(sdp))
                _offer.TrySetException(new InvalidOperationException("The generated offer was invalid."));
            else
                _offer.TrySetResult(sdp);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool HasOnlyRelayCandidates(string sdp)
    {
        string[] candidates = [.. sdp.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("a=candidate:", StringComparison.Ordinal))];
        return candidates.Length > 0 && candidates.All(line => line.Contains(" typ relay", StringComparison.Ordinal));
    }

    private static uint CreateSsrc(uint excluded = 0)
    {
        uint ssrc;
        do { ssrc = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue); }
        while (ssrc == excluded);
        return ssrc;
    }

    internal static bool HasDistinctMediaSsrcs(string sdp, bool useMicrophone)
    {
        string[] lines = sdp.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mediaSsrcs = new List<HashSet<uint>>();
        HashSet<uint>? current = null;
        foreach (string line in lines)
        {
            if (line.StartsWith("m=", StringComparison.Ordinal))
            {
                string kind = line[2..].Split(' ', 2)[0];
                current = kind is "video" or "audio" ? [] : null;
                if (current is not null) mediaSsrcs.Add(current);
                continue;
            }
            if (current is null || !line.StartsWith("a=ssrc:", StringComparison.Ordinal)) continue;
            ReadOnlySpan<char> value = line.AsSpan("a=ssrc:".Length);
            int separator = value.IndexOfAny(' ', '\t');
            if (separator >= 0) value = value[..separator];
            if (!uint.TryParse(value, out uint ssrc) || ssrc == 0) return false;
            current.Add(ssrc);
        }
        if (mediaSsrcs.Count != (useMicrophone ? 2 : 1) || mediaSsrcs.Any(static ssrcs => ssrcs.Count != 1)) return false;
        return mediaSsrcs.SelectMany(static ssrcs => ssrcs).Distinct().Count() == mediaSsrcs.Count;
    }

    internal static bool HasExpectedMedia(string sdp, bool useMicrophone)
    {
        string[] lines = sdp.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var sections = new List<(string Kind, List<string> Lines)>();
        foreach (string line in lines)
        {
            if (line.StartsWith("m=", StringComparison.Ordinal))
            {
                string kind = line[2..].Split(' ', 2)[0];
                sections.Add((kind, [line]));
            }
            else if (sections.Count > 0)
            {
                sections[^1].Lines.Add(line);
            }
        }
        if (sections.Count != (useMicrophone ? 2 : 1)) return false;
        (string Kind, List<string> Lines)[] video = [.. sections.Where(section => section.Kind == "video")];
        (string Kind, List<string> Lines)[] audio = [.. sections.Where(section => section.Kind == "audio")];
        if (video.Length != 1 || audio.Length != (useMicrophone ? 1 : 0)) return false;
        if (!HasExactCodec(video[0], H264PayloadType.ToString(System.Globalization.CultureInfo.InvariantCulture), "H264/90000", "sendonly")) return false;
        if (!useMicrophone) return true;
        return HasExactCodec(audio[0], OpusPayloadType.ToString(System.Globalization.CultureInfo.InvariantCulture), "opus/48000/2", "sendonly");
    }

    private static bool HasExactCodec(
        (string Kind, List<string> Lines) section,
        string payloadType,
        string codec,
        string direction)
    {
        string[] media = section.Lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (media.Length != 4 || media[1] == "0" || !media[3].Equals(payloadType, StringComparison.Ordinal)) return false;
        string[] mappings = [.. section.Lines.Where(line => line.StartsWith("a=rtpmap:", StringComparison.Ordinal))];
        string[] directions = [.. section.Lines.Where(static line =>
            line is "a=sendrecv" or "a=sendonly" or "a=recvonly" or "a=inactive")];
        string prefix = $"a=rtpmap:{payloadType} ";
        return mappings.Length == 1 && mappings[0].StartsWith(prefix, StringComparison.Ordinal) &&
            mappings[0][prefix.Length..].Equals(codec, StringComparison.OrdinalIgnoreCase) &&
            directions.Length == 1 && directions[0].Equals($"a={direction}", StringComparison.Ordinal);
    }

    private static PhoneWebcamWebRtcPeer? From(nint pointer) => pointer == 0 ? null : GCHandle.FromIntPtr(pointer).Target as PhoneWebcamWebRtcPeer;
    private static void OnDescription(int peer, nint sdp, nint type, nint pointer) { _ = peer; _ = sdp; _ = type; _ = pointer; }
    private static void OnState(int peer, LibDataChannelNative.PeerState state, nint pointer)
    {
        _ = peer;
        PhoneWebcamWebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        if (state is LibDataChannelNative.PeerState.Failed or LibDataChannelNative.PeerState.Disconnected or LibDataChannelNative.PeerState.Closed)
        {
            owner._trackOpen.TrySetException(new InvalidOperationException("The Phone webcam WebRTC transport stopped."));
            owner._audioTrackOpen?.TrySetException(new InvalidOperationException("The Phone webcam WebRTC transport stopped."));
            owner.Stopped?.Invoke(owner, EventArgs.Empty);
        }
    }
    private static void OnGathering(int peer, LibDataChannelNative.GatheringState state, nint pointer)
    {
        _ = peer;
        PhoneWebcamWebRtcPeer? owner = From(pointer);
        if (owner is not null && state == LibDataChannelNative.GatheringState.Complete) owner.CompleteOffer();
    }
    private static void OnOpen(int id, nint pointer)
    {
        PhoneWebcamWebRtcPeer? owner = From(pointer);
        if (owner is null) return;
        if (id == owner._track) owner._trackOpen.TrySetResult();
        else if (id == owner._audioTrack) owner._audioTrackOpen?.TrySetResult();
    }
    private static void OnClosed(int id, nint pointer)
    {
        PhoneWebcamWebRtcPeer? owner = From(pointer);
        if (owner is not null && (id == owner._track || id == owner._audioTrack))
        {
            owner.Stopped?.Invoke(owner, EventArgs.Empty);
        }
    }
    private static void OnError(int id, nint message, nint pointer)
    {
        PhoneWebcamWebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._track && id != owner._audioTrack) return;
        string detail = Marshal.PtrToStringUTF8(message) ?? "unknown native error";
        TaskCompletionSource open = id == owner._track ? owner._trackOpen : owner._audioTrackOpen!;
        ReportTrackError(open, () => owner.Stopped?.Invoke(owner, EventArgs.Empty), detail);
    }

    internal static void ReportTrackError(TaskCompletionSource trackOpen, Action stopped, string detail)
    {
        trackOpen.TrySetException(new InvalidOperationException($"Video track error: {detail}"));
        stopped();
    }
    private static void OnMessage(int id, nint message, int size, nint pointer)
    {
        PhoneWebcamWebRtcPeer? owner = From(pointer);
        if (owner is null || id != owner._track && id != owner._audioTrack) return;
        try
        {
            if (id == owner._track) owner.ReceiveRtp(message, size);
            else owner.ReceiveAudioRtp(message, size);
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
