using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed record PhoneWebcamStartResult(
    bool Succeeded,
    string Code,
    string Message,
    string? OfferSdp = null,
    string? HostSignature = null,
    IReadOnlyList<RelayIceServer>? IceServers = null,
    DateTimeOffset? TurnExpiresAt = null,
    long? RelayUsageBytes = null,
    DateTimeOffset? RelayUsageCheckedAt = null,
    RelayScreenQuality? RelayQuality = null,
    int? MaximumBitrate = null);

internal sealed record PhoneWebcamOperationResult(bool Succeeded, string Code, string Message);

internal sealed class PhoneWebcamActivityChangedEventArgs(
    string? clientId,
    string state,
    int? width = null,
    int? height = null,
    object? owner = null,
    string? operationId = null) : EventArgs
{
    internal string? ClientId { get; } = clientId;
    internal string State { get; } = state;
    internal int? Width { get; } = width;
    internal int? Height { get; } = height;
    internal object? Owner { get; } = owner;
    internal string? OperationId { get; } = operationId;
}

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Pending and active sessions are detached under the gate and disposed asynchronously by StopAsync and DisposeAsync.")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Pending session ownership transfers to the coordinator under the gate or is disposed on every rejected path.")]
internal sealed class PhoneWebcamCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan SignalingLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TransportOpenLifetime = TimeSpan.FromSeconds(20);
    private const int DirectMaximumBitrate = 12_000_000;
    private readonly Lock _gate = new();
    private readonly PairingManager _pairingManager;
    private readonly HostStatusPayloadFactory _statusFactory;
    private readonly IPhoneWebcamFeature _feature;
    private readonly IPhoneWebcamWebRtcPeerFactory _peerFactory;
    private readonly IPhoneWebcamAudioTarget _audioTarget;
    private readonly PhoneWebcamFrameSequence _frameSequence = new();
    private PendingSession? _pending;
    private PendingSession? _answering;
    private ActiveSession? _active;
    private long _sessionGeneration;
    private int _disposed;

    internal PhoneWebcamCoordinator(
        PairingManager pairingManager,
        HostStatusPayloadFactory statusFactory,
        IPhoneWebcamFeature feature,
        IPhoneWebcamWebRtcPeerFactory? peerFactory = null,
        IPhoneWebcamAudioTarget? audioTarget = null)
    {
        _pairingManager = pairingManager;
        _statusFactory = statusFactory;
        _feature = feature;
        _peerFactory = peerFactory ?? new PhoneWebcamWebRtcPeerFactory();
        _audioTarget = audioTarget ?? new PhoneWebcamAudioTarget();
        pairingManager.PairingRevoked += OnPairingRevoked;
        pairingManager.PermissionsChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
    }

    internal event EventHandler<PhoneWebcamActivityChangedEventArgs>? ActivityChanged;
    internal event EventHandler<PhoneWebcamActivityChangedEventArgs>? Ended;

    internal async Task<PhoneWebcamStartResult> StartAsync(
        object owner,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        bool useMicrophone,
        string clientSignature,
        RelayTurnConfiguration? relay,
        CancellationToken cancellationToken,
        DateTimeOffset? now = null)
    {
        if (!CanStart(clientId))
        {
            return Failure("permission-denied", "Phone webcam is not enabled for this device on the PC.");
        }

        if (captureWidth is < 1 or > 4096 || captureHeight is < 1 or > 4096 || captureFps is < 1 or > 60)
        {
            return Failure("invalid-capture", "The phone reported invalid camera settings.");
        }

        if (useMicrophone && !_audioTarget.Status.IsReady)
        {
            return Failure("microphone-unavailable", "VB-CABLE is not ready on this PC.");
        }

        string startTranscript = StartTranscript(clientId, operationId, captureWidth, captureHeight, captureFps, useMicrophone);
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(startTranscript), clientSignature))
        {
            return Failure("invalid-proof", "The Phone webcam request could not be authenticated.");
        }

        IPhoneWebcamWebRtcPeer peer;
        try
        {
            peer = _peerFactory.Create(relay, useMicrophone);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return Failure("webrtc-unavailable", "The Phone webcam WebRTC receiver is unavailable on this PC.");
        }

        var createdAt = now ?? DateTimeOffset.UtcNow;
        var pending = new PendingSession(
            owner,
            clientId,
            operationId,
            captureWidth,
            captureHeight,
            captureFps,
            useMicrophone,
            createdAt + SignalingLifetime,
            peer,
            relay is not null);
        PendingSession? expired;
        bool busy;
        lock (_gate)
        {
            expired = TakeExpiredPending(createdAt);
            busy = _pending is not null || _answering is not null || _active is not null;
            if (!busy)
            {
                pending.Generation = ++_sessionGeneration;
                _pending = pending;
            }
        }

        if (expired is not null)
        {
            await expired.DisposeAsync().ConfigureAwait(false);
        }

        if (busy)
        {
            await pending.DisposeAsync().ConfigureAwait(false);
            return Failure("busy", "Another phone is already using Voltura Air Webcam.");
        }

        try
        {
            string offerSdp = await peer.CreateOfferAsync(cancellationToken).ConfigureAwait(false);
            string offerHash = HashSdp(offerSdp);
            pending.OfferHash = offerHash;
            string offerTranscript = OfferTranscript(clientId, operationId, offerHash);
            _ = ExpirePendingAsync(pending);
            ReportSessionActivity(pending.Generation, clientId, "connecting", captureWidth, captureHeight);
            return new PhoneWebcamStartResult(
                true,
                "accepted",
                "The encrypted Phone webcam connection is ready.",
                offerSdp,
                _pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(offerTranscript)),
                relay?.IceServers,
                relay?.ExpiresAt,
                relay?.UsageBytes,
                relay?.CheckedAt,
                ToWireRelayQuality(relay?.EffectiveQuality),
                relay?.MaximumBitrate ?? DirectMaximumBitrate);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
            RemovePending(pending);
            await pending.DisposeAsync().ConfigureAwait(false);
            return Failure("webrtc-unavailable", "The PC could not create a Phone webcam offer.");
        }
    }

    internal static RelayScreenQuality? ToWireRelayQuality(RelayScreenQuality? quality) =>
        quality == RelayScreenQuality.High ? null : quality;

    internal async Task<PhoneWebcamOperationResult> CompleteAnswerAsync(
        object owner,
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        DateTimeOffset? now = null)
    {
        if (!CanStart(clientId))
        {
            await StopAsync(clientId, "permission-revoked").ConfigureAwait(false);
            return new(false, "permission-denied", "Phone webcam is not enabled for this device on the PC.");
        }

        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > ScreenViewProtocol.MaxSdpLength)
        {
            return new(false, "invalid-answer", "The Phone webcam WebRTC answer was invalid.");
        }

        PendingSession? pending;
        PendingSession? expired;
        lock (_gate)
        {
            expired = TakeExpiredPending(now ?? DateTimeOffset.UtcNow);
            pending = _pending is { } candidate &&
                ReferenceEquals(candidate.Owner, owner) &&
                candidate.ClientId == clientId &&
                candidate.OperationId == operationId &&
                candidate.OfferHash is not null
                    ? candidate
                    : null;
            if (pending is not null)
            {
                _pending = null;
                _answering = pending;
            }
        }

        if (expired is not null)
        {
            await expired.DisposeAsync().ConfigureAwait(false);
        }

        if (pending is null)
        {
            return new(false, "offer-expired", "The Phone webcam offer expired. Start again.");
        }

        string answerHash = HashSdp(answerSdp);
        string answerTranscript = AnswerTranscript(clientId, operationId, pending.OfferHash!, answerHash);
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(answerTranscript), clientSignature))
        {
            await RejectAnsweringAsync(pending).ConfigureAwait(false);
            return new(false, "invalid-proof", "The Phone webcam answer could not be authenticated.");
        }

        try
        {
            pending.Peer.ApplyAnswer(answerSdp);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            await RejectAnsweringAsync(pending).ConfigureAwait(false);
            return new(false, "invalid-answer", "The PC rejected the Phone webcam answer.");
        }

        ActiveSession createdActive;
        try
        {
            createdActive = await ActiveSession.CreateAsync(pending, _feature, _frameSequence, _audioTarget).ConfigureAwait(false);
            pending.DetachPeer();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await RejectAnsweringAsync(pending).ConfigureAwait(false);
            return pending.UseMicrophone
                ? new(false, "microphone-unavailable", "The PC could not open the VB-CABLE audio endpoint.")
                : new(false, "decoder-unavailable", "The PC could not start its H.264 Phone webcam decoder.");
        }

        ActiveSession active = createdActive;
        active.Pipeline.QualityChanged += quality =>
            ReportSessionActivity(
                active.Generation,
                clientId,
                "streaming",
                quality.Width,
                quality.Height,
                active.HasMicrophone);
        active.PipelineFailed += (_, _) => OnActiveFailure(active, "decoder-failed");
        active.AudioPipelineFailed += (_, _) => OnActiveFailure(active, "audio-failed");
        active.PeerStopped += (_, _) => OnActiveFailure(active, "transport-lost");

        try
        {
            createdActive.StartPipeline();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_answering, pending))
                {
                    _answering = null;
                }
            }

            try
            {
                await createdActive.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                pending.CompleteAnswering();
            }

            return pending.UseMicrophone
                ? new(false, "microphone-unavailable", "The PC could not start the VB-CABLE audio endpoint.")
                : new(false, "decoder-unavailable", "The PC could not start its H.264 Phone webcam decoder.");
        }

        var accepted = false;
        string? startupFailure;
        lock (_gate)
        {
            startupFailure = createdActive.FailureReason;
            if (!ReferenceEquals(_answering, pending) || pending.StopRequested || _active is not null || startupFailure is not null)
            {
            }
            else
            {
                _answering = null;
                _active = createdActive;
                accepted = true;
            }
        }
        if (!accepted)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_answering, pending))
                {
                    _answering = null;
                }
            }
            try
            {
                await createdActive.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                pending.CompleteAnswering();
            }
            return startupFailure switch
            {
                "audio-failed" => new(false, "microphone-unavailable", "The PC could not start the VB-CABLE audio endpoint."),
                "decoder-failed" => new(false, "decoder-unavailable", "The PC could not start its H.264 Phone webcam decoder."),
                "transport-lost" => new(false, "invalid-answer", "The Phone webcam transport stopped while starting."),
                _ => new(false, "offer-expired", "The Phone webcam offer is no longer active.")
            };
        }

        pending.CompleteAnswering();

        ReportSessionActivity(active.Generation, clientId, "connecting", pending.CaptureWidth, pending.CaptureHeight);
        _ = MonitorTrackOpenAsync(active);
        return new(true, "accepted", "Phone webcam is connecting to the PC.");
    }

    private async Task RejectAnsweringAsync(PendingSession pending)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_answering, pending))
            {
                _answering = null;
            }
        }

        try
        {
            await pending.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            pending.CompleteAnswering();
        }
    }

    private async Task MonitorTrackOpenAsync(ActiveSession active)
    {
        try
        {
            await active.MediaOpen.WaitAsync(TransportOpenLifetime).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            await StopSpecificAsync(active, "transport-lost").ConfigureAwait(false);
        }
    }

    private async Task ExpirePendingAsync(PendingSession pending)
    {
        TimeSpan delay = pending.ExpiresAt - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_pending, pending))
            {
                return;
            }

            _pending = null;
        }

        await pending.DisposeAsync().ConfigureAwait(false);
        ReportTerminalActivity(pending.Generation, pending.ClientId, "offer-expired");
        Ended?.Invoke(this, new PhoneWebcamActivityChangedEventArgs(
            pending.ClientId,
            "offer-expired",
            owner: pending.Owner,
            operationId: pending.OperationId));
    }

    internal async Task<bool> StopAsync(string clientId, string reason = "stopped", object? owner = null)
    {
        PendingSession? pending = null;
        PendingSession? answering = null;
        ActiveSession? active = null;
        lock (_gate)
        {
            if (_pending?.ClientId == clientId && (owner is null || ReferenceEquals(_pending.Owner, owner)))
            {
                pending = _pending;
                _pending = null;
            }

            if (_active?.ClientId == clientId && (owner is null || ReferenceEquals(_active.Owner, owner)))
            {
                active = _active;
                _active = null;
            }

            if (_answering?.ClientId == clientId &&
                (owner is null || ReferenceEquals(_answering.Owner, owner)) &&
                _answering.RequestStop())
            {
                answering = _answering;
            }
        }

        if (pending is not null)
        {
            await pending.DisposeAsync().ConfigureAwait(false);
        }

        if (active is not null)
        {
            await active.DisposeAsync().ConfigureAwait(false);
        }

        if (answering is not null)
        {
            await answering.AnsweringCompleted.ConfigureAwait(false);
        }

        PendingSession? endedPending = pending;
        ActiveSession? endedActive = active;
        PendingSession? endedAnswering = answering;
        if (endedPending is not null || endedAnswering is not null || endedActive is not null)
        {
            ReportTerminalActivity(
                endedActive?.Generation ?? endedAnswering?.Generation ?? endedPending!.Generation,
                clientId,
                reason);
            Ended?.Invoke(this, new PhoneWebcamActivityChangedEventArgs(
                clientId,
                reason,
                owner: endedActive?.Owner ?? endedAnswering?.Owner ?? endedPending?.Owner,
                operationId: endedActive?.OperationId ?? endedAnswering?.OperationId ?? endedPending?.OperationId));
        }

        return pending is not null || answering is not null || active is not null;
    }

    private async Task StopSpecificAsync(ActiveSession active, string reason)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, active))
            {
                return;
            }

            _active = null;
        }

        await active.DisposeAsync().ConfigureAwait(false);
        ReportTerminalActivity(active.Generation, active.ClientId, reason);
        Ended?.Invoke(this, new PhoneWebcamActivityChangedEventArgs(
            active.ClientId,
            reason,
            owner: active.Owner,
            operationId: active.OperationId));
    }

    private void OnActiveFailure(ActiveSession active, string reason)
    {
        Console.Error.WriteLine("Voltura Air Phone webcam media stopped: {0}.", reason);
        active.RecordFailure(reason);
        _ = StopSpecificAsync(active, reason);
    }

    private bool CanStart(string clientId) =>
        Volatile.Read(ref _disposed) == 0 && _statusFactory.CanUsePhoneWebcam(clientId);

    private PendingSession? TakeExpiredPending(DateTimeOffset now)
    {
        if (_pending is null || _pending.ExpiresAt > now)
        {
            return null;
        }

        PendingSession expired = _pending;
        _pending = null;
        return expired;
    }

    private void RemovePending(PendingSession pending)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
        }
    }

    private void OnPairingRevoked(object? sender, PairingRevokedEventArgs args)
    {
        if (args.ClientId is null)
        {
            _ = StopAllAsync("pairing-revoked");
        }
        else
        {
            _ = StopAsync(args.ClientId, "pairing-revoked");
        }
    }

    private void OnPermissionsChanged(object? sender, EventArgs args) => _ = StopUnauthorizedAsync();

    private async Task StopUnauthorizedAsync()
    {
        string? clientId;
        lock (_gate)
        {
            clientId = _active?.ClientId ?? _answering?.ClientId ?? _pending?.ClientId;
        }

        if (clientId is not null && !CanStart(clientId))
        {
            await StopAsync(clientId, "permission-revoked").ConfigureAwait(false);
        }
    }

    internal async Task StopAllAsync(string reason)
    {
        string[] clients;
        lock (_gate)
        {
            clients = [.. new[] { _pending?.ClientId, _answering?.ClientId, _active?.ClientId }
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)];
        }

        foreach (string client in clients)
        {
            await StopAsync(client, reason).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pairingManager.PairingRevoked -= OnPairingRevoked;
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        await StopAllAsync("host-stopped").ConfigureAwait(false);
    }

    internal static string StartTranscript(string clientId, string operationId, int width, int height, int fps, bool useMicrophone) =>
        $"VolturaAir phone-webcam:start:v2:{clientId}:{operationId}:{width}:{height}:{fps}:{useMicrophone.ToString().ToLowerInvariant()}";
    internal static string OfferTranscript(string clientId, string operationId, string offerHash) =>
        $"VolturaAir phone-webcam:offer:v2:{clientId}:{operationId}:{offerHash}";
    internal static string AnswerTranscript(string clientId, string operationId, string offerHash, string answerHash) =>
        $"VolturaAir phone-webcam:answer:v2:{clientId}:{operationId}:{offerHash}:{answerHash}";
    internal static string HashSdp(string sdp) =>
        ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));
    private static PhoneWebcamStartResult Failure(string code, string message) => new(false, code, message);

    private void ReportSessionActivity(
        long generation,
        string clientId,
        string state,
        int? width = null,
        int? height = null,
        bool hasMicrophone = false)
    {
        lock (_gate)
        {
            if (generation != _sessionGeneration ||
                (_pending?.Generation != generation &&
                 _answering?.Generation != generation &&
                 _active?.Generation != generation))
            {
                return;
            }

            ReportActivityCore(clientId, state, width, height, hasMicrophone);
        }
    }

    private void ReportTerminalActivity(long generation, string clientId, string state)
    {
        lock (_gate)
        {
            if (generation != _sessionGeneration || _pending is not null || _answering is not null || _active is not null)
            {
                return;
            }

            ReportActivityCore(clientId, state);
        }
    }

    private void ReportActivityCore(
        string clientId,
        string state,
        int? width = null,
        int? height = null,
        bool hasMicrophone = false)
    {
        if (_feature is PhoneWebcamFeature feature)
        {
            feature.ReportActivity(state, width, height, hasMicrophone);
        }
        ActivityChanged?.Invoke(this, new PhoneWebcamActivityChangedEventArgs(clientId, state, width, height));
    }

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The peer is disposed asynchronously by DisposeAsync or explicitly detached into ActiveSession.")]
    private sealed class PendingSession(
        object owner,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        bool useMicrophone,
        DateTimeOffset expiresAt,
        IPhoneWebcamWebRtcPeer peer,
        bool relayOnly) : IAsyncDisposable
    {
        internal object Owner { get; } = owner;
        private IPhoneWebcamWebRtcPeer? _peer = peer;
        internal string ClientId { get; } = clientId;
        internal string OperationId { get; } = operationId;
        internal int CaptureWidth { get; } = captureWidth;
        internal int CaptureHeight { get; } = captureHeight;
        internal int CaptureFps { get; } = captureFps;
        internal bool UseMicrophone { get; } = useMicrophone;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal bool RelayOnly { get; } = relayOnly;
        internal string? OfferHash { get; set; }
        internal long Generation { get; set; }
        private readonly TaskCompletionSource _answeringCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task AnsweringCompleted => _answeringCompleted.Task;
        internal bool StopRequested { get; private set; }
        internal IPhoneWebcamWebRtcPeer Peer => _peer ?? throw new ObjectDisposedException(nameof(PendingSession));
        internal bool RequestStop()
        {
            if (StopRequested)
            {
                return false;
            }

            StopRequested = true;
            return true;
        }
        internal void CompleteAnswering() => _answeringCompleted.TrySetResult();
        internal void DetachPeer() => _peer = null;
        public async ValueTask DisposeAsync()
        {
            IPhoneWebcamWebRtcPeer? released = Interlocked.Exchange(ref _peer, null);
            if (released is not null)
            {
                await released.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class ActiveSession : IAsyncDisposable
    {
        private readonly IPhoneWebcamWebRtcPeer _peer;
        private readonly EventHandler _stopped;
        private readonly PhoneWebcamAudioPipeline? _audioPipeline;
        private string? _failureReason;
        private int _disposed;

        private ActiveSession(
            PendingSession pending,
            PhoneWebcamVideoPipeline pipeline,
            PhoneWebcamAudioPipeline? audioPipeline)
        {
            Owner = pending.Owner;
            ClientId = pending.ClientId;
            OperationId = pending.OperationId;
            Generation = pending.Generation;
            _peer = pending.Peer;
            Pipeline = pipeline;
            _peer.AccessUnitReceived += Pipeline.Submit;
            Pipeline.KeyFrameRequested += OnKeyFrameRequested;
            Pipeline.Failed += OnPipelineFailed;
            _audioPipeline = audioPipeline;
            if (_audioPipeline is not null)
            {
                _peer.OpusPacketReceived += _audioPipeline.Submit;
                _audioPipeline.Failed += OnAudioPipelineFailed;
            }
            _stopped = (_, _) =>
            {
                RecordFailure("transport-lost");
                PeerStopped?.Invoke(this, EventArgs.Empty);
            };
            _peer.Stopped += _stopped;
        }

        internal static async Task<ActiveSession> CreateAsync(
            PendingSession pending,
            IPhoneWebcamFeature feature,
            PhoneWebcamFrameSequence frameSequence,
            IPhoneWebcamAudioTarget audioTarget)
        {
            PhoneWebcamAudioPipeline? audio = null;
            PhoneWebcamVideoPipeline? video = null;
            try
            {
                if (pending.UseMicrophone) audio = new PhoneWebcamAudioPipeline(audioTarget);
                video = new PhoneWebcamVideoPipeline(feature, frameSequence);
                return new ActiveSession(pending, video, audio);
            }
            catch
            {
                if (video is not null) await video.DisposeAsync().ConfigureAwait(false);
                if (audio is not null) await audio.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal string ClientId { get; }
        internal string OperationId { get; }
        internal long Generation { get; }
        internal object Owner { get; }
        internal Task MediaOpen => _peer.MediaOpen;
        internal PhoneWebcamVideoPipeline Pipeline { get; }
        internal bool HasMicrophone => _audioPipeline is not null;
        internal string? FailureReason => Volatile.Read(ref _failureReason);
        internal event EventHandler? PeerStopped;
        internal event EventHandler? PipelineFailed;
        internal event EventHandler? AudioPipelineFailed;

        internal void StartPipeline()
        {
            Pipeline.Start();
            _audioPipeline?.Start();
        }

        internal void RecordFailure(string reason) =>
            Interlocked.CompareExchange(ref _failureReason, reason, null);

        private void OnKeyFrameRequested(object? sender, EventArgs args) => _peer.RequestKeyFrame();
        private void OnPipelineFailed(object? sender, EventArgs args)
        {
            RecordFailure("decoder-failed");
            PipelineFailed?.Invoke(this, EventArgs.Empty);
        }

        private void OnAudioPipelineFailed(object? sender, EventArgs args)
        {
            RecordFailure("audio-failed");
            AudioPipelineFailed?.Invoke(this, EventArgs.Empty);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _peer.Stopped -= _stopped;
            _peer.AccessUnitReceived -= Pipeline.Submit;
            if (_audioPipeline is not null)
            {
                _peer.OpusPacketReceived -= _audioPipeline.Submit;
                _audioPipeline.Failed -= OnAudioPipelineFailed;
            }
            Pipeline.KeyFrameRequested -= OnKeyFrameRequested;
            Pipeline.Failed -= OnPipelineFailed;
            await _peer.DisposeAsync().ConfigureAwait(false);
            await Pipeline.DisposeAsync().ConfigureAwait(false);
            if (_audioPipeline is not null) await _audioPipeline.DisposeAsync().ConfigureAwait(false);
        }
    }
}
