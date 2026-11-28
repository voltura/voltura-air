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
    private static readonly TimeSpan VideoCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VideoKeyFrameThreshold = TimeSpan.FromSeconds(4);
    // Allow the phone's 10-second capture and 5-second sender update deadlines to finish.
    private static readonly TimeSpan VideoIdleLimit = TimeSpan.FromSeconds(18);
    private const int DirectMaximumBitrate = 12_000_000;
    private readonly Lock _gate = new();
    private readonly PairingManager _pairingManager;
    private readonly HostStatusPayloadFactory _statusFactory;
    private readonly IPhoneWebcamFeature _feature;
    private readonly IPhoneWebcamWebRtcPeerFactory _peerFactory;
    private readonly IPhoneWebcamAudioTarget _audioTarget;
    private readonly TimeProvider _timeProvider;
    private readonly PhoneWebcamFrameSequence _frameSequence = new();
    private readonly HashSet<Task> _retiring = [];
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
        IPhoneWebcamAudioTarget? audioTarget = null,
        TimeProvider? timeProvider = null)
    {
        _pairingManager = pairingManager;
        _statusFactory = statusFactory;
        _feature = feature;
        _peerFactory = peerFactory ?? new PhoneWebcamWebRtcPeerFactory();
        _audioTarget = audioTarget ?? new PhoneWebcamAudioTarget();
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        Func<CancellationToken, Task<RelayTurnConfiguration?>>? getRelayTurnConfiguration,
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

        PendingSession? pending = null;
        PendingSession? expired;
        lock (_gate)
        {
            expired = TakeExpiredPending(now ?? _timeProvider.GetUtcNow());
            if (Volatile.Read(ref _disposed) == 0 && _pending is null && _answering is null && _active is null)
            {
                _pending = pending = new PendingSession(owner, clientId, operationId, useMicrophone)
                {
                    Generation = ++_sessionGeneration
                };
            }
        }

        if (pending is null)
        {
            if (expired is not null)
                await FinishExpiredPendingAsync(expired).ConfigureAwait(false);
            return Failure("busy", "Another phone is already using Voltura Air Webcam.");
        }

        var offerReady = false;
        var activityReported = false;
        try
        {
            if (expired is not null)
                await FinishExpiredPendingAsync(expired).ConfigureAwait(false);
            using var offerLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pending.Lifetime);
            offerLifetime.Token.ThrowIfCancellationRequested();
            if (!CanStart(clientId))
                return Failure("permission-denied", "Phone webcam is not enabled for this device on the PC.");
            RelayTurnConfiguration? relay = null;
            if (getRelayTurnConfiguration is not null)
            {
                relay = await getRelayTurnConfiguration(offerLifetime.Token).ConfigureAwait(false);
                offerLifetime.Token.ThrowIfCancellationRequested();
                if (relay is null)
                    return Failure("turn-unavailable", "Relay Phone webcam is temporarily unavailable.");
            }
            IPhoneWebcamWebRtcPeer peer = _peerFactory.Create(relay, useMicrophone);
            pending.AttachPeer(peer);
            offerLifetime.Token.ThrowIfCancellationRequested();
            string offerSdp = await peer.CreateOfferAsync(offerLifetime.Token).ConfigureAwait(false);
            string offerHash = HashSdp(offerSdp);
            string offerTranscript = OfferTranscript(clientId, operationId, offerHash);
            string signature = _pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(offerTranscript));
            activityReported = true;
            ReportSessionActivity(pending.Generation, clientId, "connecting", captureWidth, captureHeight);
            lock (_gate)
            {
                if (!ReferenceEquals(_pending, pending) || offerLifetime.IsCancellationRequested)
                    return Failure("offer-expired", "The Phone webcam offer is no longer active.");
                pending.OfferHash = offerHash;
                pending.ExpiresAt = (now ?? _timeProvider.GetUtcNow()) + SignalingLifetime;
                offerReady = true;
            }
            _ = ExpirePendingAsync(pending);
            return new PhoneWebcamStartResult(
                true,
                "accepted",
                "The encrypted Phone webcam connection is ready.",
                offerSdp,
                signature,
                relay?.IceServers,
                relay?.ExpiresAt,
                relay?.UsageBytes,
                relay?.CheckedAt,
                ToWireRelayQuality(relay?.EffectiveQuality),
                relay?.MaximumBitrate ?? DirectMaximumBitrate);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or InvalidOperationException or
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return Failure("webrtc-unavailable", "The PC could not create a Phone webcam offer.");
        }
        finally
        {
            pending.CompleteOfferCreation();
            if (!offerReady)
            {
                bool removed = RemovePending(pending);
                await pending.DisposeAsync().ConfigureAwait(false);
                if (removed && activityReported)
                    ReportTerminalActivity(pending.Generation, clientId, "idle");
            }
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
            expired = TakeExpiredPending(now ?? _timeProvider.GetUtcNow());
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
            await FinishExpiredPendingAsync(expired).ConfigureAwait(false);
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
            long generation = pending.Generation;
            createdActive = await ActiveSession.CreateAsync(
                pending, frame => PublishActiveFrame(generation, frame), _frameSequence, _audioTarget).ConfigureAwait(false);
            pending.DetachPeer();
            await pending.DisposeAsync().ConfigureAwait(false);
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
            createdActive.StartAudioPipeline();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await RejectAnsweringAsync(pending, createdActive).ConfigureAwait(false);
            return pending.UseMicrophone
                ? new(false, "microphone-unavailable", "The PC could not start the VB-CABLE audio endpoint.")
                : new(false, "decoder-unavailable", "The PC could not start its H.264 Phone webcam decoder.");
        }

        var accepted = false;
        string? startupFailure;
        lock (_gate)
        {
            startupFailure = createdActive.FailureReason;
            if (ReferenceEquals(_answering, pending) && !pending.StopRequested && _active is null && startupFailure is null)
            {
                _answering = null;
                _active = createdActive;
                createdActive.Pipeline.Start();
                accepted = true;
            }
        }
        if (!accepted)
        {
            await RejectAnsweringAsync(pending, createdActive).ConfigureAwait(false);
            return startupFailure switch
            {
                "audio-failed" => new(false, "microphone-unavailable", "The PC could not start the VB-CABLE audio endpoint."),
                "transport-lost" => new(false, "offer-expired", "The Phone webcam transport stopped while starting."),
                _ => new(false, "offer-expired", "The Phone webcam offer is no longer active.")
            };
        }

        pending.CompleteAnswering();

        _ = MonitorVideoAsync(active);
        return new(true, "accepted", "Phone webcam is connecting to the PC.");
    }

    private async Task RejectAnsweringAsync(PendingSession pending, ActiveSession? active = null)
    {
        try
        {
            if (active is null)
                await pending.DisposeAsync().ConfigureAwait(false);
            else
                await active.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            bool resetActivity = false;
            lock (_gate)
            {
                if (ReferenceEquals(_answering, pending))
                {
                    _answering = null;
                    resetActivity = !pending.StopRequested;
                }
                pending.CompleteAnswering();
            }

            if (resetActivity)
                ReportTerminalActivity(pending.Generation, pending.ClientId, "idle");
        }
    }

    private async Task MonitorVideoAsync(ActiveSession active)
    {
        CancellationToken retired = active.Retired;
        try
        {
            await active.MediaOpen.WaitAsync(TransportOpenLifetime, _timeProvider, retired).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (!retired.IsCancellationRequested)
                await StopSpecificAsync(active, "transport-lost").ConfigureAwait(false);
            return;
        }

        long openedAt = _timeProvider.GetTimestamp();
        using var timer = new PeriodicTimer(VideoCheckInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(retired).ConfigureAwait(false))
            {
                long lastFrame = active.LastVideoTimestamp;
                TimeSpan idle = _timeProvider.GetElapsedTime(Math.Max(lastFrame, openedAt));
                if (idle >= VideoIdleLimit)
                {
                    await StopSpecificAsync(active, "decoder-failed", lastFrame).ConfigureAwait(false);
                }
                else if (idle >= VideoKeyFrameThreshold && !retired.IsCancellationRequested)
                {
                    active.RequestKeyFrame();
                }
            }
        }
        catch (OperationCanceledException) when (retired.IsCancellationRequested)
        {
        }
    }

    private async Task ExpirePendingAsync(PendingSession pending)
    {
        TimeSpan delay = pending.ExpiresAt - _timeProvider.GetUtcNow();
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _timeProvider, pending.Lifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pending.Lifetime.IsCancellationRequested)
        {
            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_pending, pending))
            {
                return;
            }

            DetachPendingLocked();
        }

        await FinishExpiredPendingAsync(pending).ConfigureAwait(false);
    }

    private async Task FinishExpiredPendingAsync(PendingSession pending)
    {
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
        bool claimedAnswering = false;
        lock (_gate)
        {
            if (_pending?.ClientId == clientId && (owner is null || ReferenceEquals(_pending.Owner, owner)))
            {
                pending = DetachPendingLocked();
            }

            if (_active?.ClientId == clientId && (owner is null || ReferenceEquals(_active.Owner, owner)))
            {
                active = _active;
                _active = null;
                TrackRetirementLocked(active.Retirement);
            }

            if (_answering?.ClientId == clientId &&
                (owner is null || ReferenceEquals(_answering.Owner, owner)))
            {
                answering = _answering;
                claimedAnswering = answering.RequestStop();
            }
        }

        long? retiredGeneration = active?.Generation ?? pending?.Generation;
        try
        {
            if (retiredGeneration is { } generation)
            {
                ReportTerminalActivity(generation, clientId, reason);
            }
        }
        finally
        {
            if (pending is not null)
            {
                await pending.DisposeAsync().ConfigureAwait(false);
            }

            if (active is not null)
            {
                await active.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (answering is not null)
        {
            await answering.AnsweringCompleted.ConfigureAwait(false);
            if (retiredGeneration is null && claimedAnswering)
            {
                ReportTerminalActivity(answering.Generation, clientId, reason);
            }
        }

        PendingSession? endedPending = pending;
        ActiveSession? endedActive = active;
        PendingSession? endedAnswering = claimedAnswering ? answering : null;
        if (endedPending is not null || endedAnswering is not null || endedActive is not null)
        {
            Ended?.Invoke(this, new PhoneWebcamActivityChangedEventArgs(
                clientId,
                reason,
                owner: endedActive?.Owner ?? endedAnswering?.Owner ?? endedPending?.Owner,
                operationId: endedActive?.OperationId ?? endedAnswering?.OperationId ?? endedPending?.OperationId));
        }

        return pending is not null || claimedAnswering || active is not null;
    }

    private async Task StopSpecificAsync(ActiveSession active, string reason, long? stalledAt = null)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, active) ||
                (stalledAt is { } timestamp && active.LastVideoTimestamp != timestamp))
            {
                return;
            }

            _active = null;
            TrackRetirementLocked(active.Retirement);
        }

        try
        {
            ReportTerminalActivity(active.Generation, active.ClientId, reason);
        }
        finally
        {
            await active.DisposeAsync().ConfigureAwait(false);
        }
        Ended?.Invoke(this, new PhoneWebcamActivityChangedEventArgs(
            active.ClientId,
            reason,
            owner: active.Owner,
            operationId: active.OperationId));
    }

    private void OnActiveFailure(ActiveSession active, string reason)
    {
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

        return DetachPendingLocked();
    }

    private bool RemovePending(PendingSession pending)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_pending, pending)) return false;
            DetachPendingLocked();
            return true;
        }
    }

    private PendingSession? DetachPendingLocked()
    {
        PendingSession? pending = _pending;
        _pending = null;
        if (pending is not null) TrackRetirementLocked(pending.Retirement);
        return pending;
    }

    private void TrackRetirementLocked(Task completion)
    {
        _retiring.Add(completion);
        _ = ForgetRetirementAsync(completion);
    }

    private async Task ForgetRetirementAsync(Task completion)
    {
        await completion.ConfigureAwait(false);
        lock (_gate) _retiring.Remove(completion);
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

        Task[] retiring;
        lock (_gate) retiring = [.. _retiring];
        await Task.WhenAll(retiring).ConfigureAwait(false);
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

    private void PublishActiveFrame(long generation, PhoneWebcamFrame frame)
    {
        lock (_gate)
        {
            if (_active is { } active && active.Generation == generation)
            {
                active.NoteVideoPublished(_timeProvider.GetTimestamp());
                _feature.Publish(frame);
                return;
            }
        }
        frame.Dispose();
    }

    private static async ValueTask DisposePeerAsync(IPhoneWebcamWebRtcPeer peer)
    {
        try
        {
            await peer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await Console.Error.WriteLineAsync($"Voltura Air Phone webcam peer cleanup failed ({exception.GetType().Name}).")
                .ConfigureAwait(false);
        }
    }

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

            try
            {
                ReportActivityCore(clientId, state);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine("Voltura Air Phone webcam activity notification failed ({0}).", exception.GetType().Name);
            }
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
    private sealed class PendingSession : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly TaskCompletionSource _offerCreationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _answeringCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cleanupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IPhoneWebcamWebRtcPeer? _peer;
        private int _disposing;

        internal PendingSession(object owner, string clientId, string operationId, bool useMicrophone)
        {
            Owner = owner;
            ClientId = clientId;
            OperationId = operationId;
            UseMicrophone = useMicrophone;
            Lifetime = _lifetime.Token;
        }

        internal object Owner { get; }
        internal string ClientId { get; }
        internal string OperationId { get; }
        internal bool UseMicrophone { get; }
        internal CancellationToken Lifetime { get; }
        internal DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.MaxValue;
        internal string? OfferHash { get; set; }
        internal long Generation { get; set; }
        internal Task AnsweringCompleted => _answeringCompleted.Task;
        internal Task Retirement => _cleanupCompleted.Task;
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
        internal void AttachPeer(IPhoneWebcamWebRtcPeer peer) => _peer = peer;
        internal void CompleteOfferCreation() => _offerCreationCompleted.TrySetResult();
        internal void CompleteAnswering() => _answeringCompleted.TrySetResult();
        internal void DetachPeer() => _peer = null;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposing, 1) != 0)
            {
                await _cleanupCompleted.Task.ConfigureAwait(false);
                return;
            }
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
                await _offerCreationCompleted.Task.ConfigureAwait(false);
                IPhoneWebcamWebRtcPeer? released = Interlocked.Exchange(ref _peer, null);
                if (released is not null)
                    await DisposePeerAsync(released).ConfigureAwait(false);
            }
            finally
            {
                _lifetime.Dispose();
                _cleanupCompleted.TrySetResult();
            }
        }
    }

    private sealed class ActiveSession : IAsyncDisposable
    {
        private readonly IPhoneWebcamWebRtcPeer _peer;
        private readonly EventHandler _stopped;
        private readonly PhoneWebcamAudioPipeline? _audioPipeline;
        private readonly CancellationTokenSource _retired = new();
        private readonly TaskCompletionSource _cleanupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _lastVideoTimestamp = long.MinValue;
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
            if (_peer.TransportStopped) RecordFailure("transport-lost");
            Retired = _retired.Token;
        }

        internal static async Task<ActiveSession> CreateAsync(
            PendingSession pending,
            Action<PhoneWebcamFrame> publish,
            PhoneWebcamFrameSequence frameSequence,
            IPhoneWebcamAudioTarget audioTarget)
        {
            PhoneWebcamAudioPipeline? audio = null;
            PhoneWebcamVideoPipeline? video = null;
            try
            {
                if (pending.UseMicrophone)
                {
                    audio = await PhoneWebcamAudioPipeline.CreateAsync(audioTarget).ConfigureAwait(false);
                }
                video = new PhoneWebcamVideoPipeline(publish, frameSequence);
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
        internal CancellationToken Retired { get; }
        internal Task Retirement => _cleanupCompleted.Task;
        internal long LastVideoTimestamp => Volatile.Read(ref _lastVideoTimestamp);
        internal PhoneWebcamVideoPipeline Pipeline { get; }
        internal bool HasMicrophone => _audioPipeline is not null;
        internal string? FailureReason => Volatile.Read(ref _failureReason);
        internal event EventHandler? PeerStopped;
        internal event EventHandler? PipelineFailed;
        internal event EventHandler? AudioPipelineFailed;

        internal void StartAudioPipeline() => _audioPipeline?.Start();
        internal void NoteVideoPublished(long timestamp) => Volatile.Write(ref _lastVideoTimestamp, timestamp);
        internal void RequestKeyFrame() => _peer.RequestKeyFrame();

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
                await Retirement.ConfigureAwait(false);
                return;
            }

            try
            {
                await _retired.CancelAsync().ConfigureAwait(false);
                _retired.Dispose();

                _peer.Stopped -= _stopped;
                _peer.AccessUnitReceived -= Pipeline.Submit;
                if (_audioPipeline is not null)
                {
                    _peer.OpusPacketReceived -= _audioPipeline.Submit;
                    _audioPipeline.Failed -= OnAudioPipelineFailed;
                }
                Pipeline.KeyFrameRequested -= OnKeyFrameRequested;
                Pipeline.Failed -= OnPipelineFailed;
                Task videoStopped = Pipeline.DisposeAsync().AsTask();
                Task audioStopped = _audioPipeline?.DisposeAsync().AsTask() ?? Task.CompletedTask;
                // Native peer deletion can wait for the callback that reported the failure.
                Task peerStopped = Task.Run(() => DisposePeerAsync(_peer).AsTask());
                await Task.WhenAll(videoStopped, audioStopped, peerStopped).ConfigureAwait(false);
            }
            finally
            {
                _cleanupCompleted.TrySetResult();
            }
        }
    }
}
