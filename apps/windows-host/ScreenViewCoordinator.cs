using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal sealed class ScreenViewCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan SignalingLifetime = TimeSpan.FromSeconds(15);
    private const int InitialBitrate = 6_000_000;
    private readonly Lock _gate = new();
    private readonly PairingManager _pairingManager;
    private readonly HostStatusPayloadFactory _statusFactory;
    private readonly IScreenViewCaptureSource _capture;
    private readonly IScreenViewWebRtcPeerFactory _peerFactory;
    private PendingView? _pending;
    private ActiveView? _active;
    private int _disposed;

    public ScreenViewCoordinator(
        PairingManager pairingManager,
        HostStatusPayloadFactory statusFactory,
        IScreenViewCaptureSource? capture = null,
        IScreenViewWebRtcPeerFactory? peerFactory = null)
    {
        _pairingManager = pairingManager;
        _statusFactory = statusFactory;
        _capture = capture ?? new DxgiScreenViewCaptureSource();
        _peerFactory = peerFactory ?? new ScreenViewWebRtcPeerFactory();
        pairingManager.PairingRevoked += OnPairingRevoked;
        pairingManager.PermissionsChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
        AppDeveloperSettings.Changed += OnDeveloperSettingsChanged;
    }

    public event EventHandler<ScreenViewActivityChangedEventArgs>? ActivityChanged;

    public void Stop() => StopAll();

    public ScreenViewSourcesResult GetSources(string clientId) => DiscoverSources(clientId);

    public async Task<ScreenViewStartResult> StartAsync(
        string clientId,
        string operationId,
        string displayId,
        string clientSignature,
        CancellationToken cancellationToken,
        DateTimeOffset? now = null)
    {
        if (!CanStart(clientId))
            return Failure("permission-denied", "Screen viewing is disabled for this device.");
        ScreenViewSourcesResult discovery = DiscoverSources(clientId);
        if (!discovery.Succeeded)
            return Failure(discovery.Code, discovery.Message);
        if (!discovery.Sources.Any(source => string.Equals(source.Id, displayId, StringComparison.Ordinal)))
            return Failure("display-unavailable", "The selected display is no longer available.");

        string clientTranscript = $"VolturaAir screen-view:start:v2:{clientId}:{operationId}:{displayId}";
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(clientTranscript), clientSignature))
            return Failure("invalid-proof", "The WebRTC screen-view request could not be authenticated.");

        IScreenViewWebRtcPeer peer;
        try
        {
            peer = _peerFactory.Create();
        }
        catch (Exception ex) when (ex is ScreenViewWebRtcException or DllNotFoundException or BadImageFormatException)
        {
            return Failure("webrtc-unavailable", "The WebRTC screen transport is unavailable on this PC.");
        }

        var createdAt = now ?? DateTimeOffset.UtcNow;
        var pending = new PendingView(clientId, operationId, displayId, createdAt + SignalingLifetime, peer);
        PendingView? expired;
        bool busy;
        lock (_gate)
        {
            expired = TakeExpiredPending(createdAt);
            busy = _active is not null || _pending is not null;
            if (!busy) _pending = pending;
        }
        expired?.Release();
        if (busy)
        {
            pending.Release();
            return Failure("busy", "Another device is already viewing the screen.");
        }

        try
        {
            string offerSdp = await peer.CreateOfferAsync(cancellationToken).ConfigureAwait(false);
            string offerHash = HashSdp(offerSdp);
            pending.SetOffer(offerHash);
            string hostTranscript = $"VolturaAir screen-view:offer:v2:{clientId}:{operationId}:{displayId}:{offerHash}";
            return new ScreenViewStartResult(
                true,
                "accepted",
                "The encrypted WebRTC screen connection is ready.",
                offerSdp,
                _pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(hostTranscript)));
        }
        catch (Exception ex) when (ex is OperationCanceledException or ScreenViewWebRtcException or ObjectDisposedException)
        {
            RemovePending(pending);
            pending.Release();
            return Failure("webrtc-unavailable", "The PC could not create a WebRTC screen offer.");
        }
    }

    public ScreenViewOperationResult CompleteAnswer(
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        DateTimeOffset? now = null)
    {
        if (!CanStart(clientId))
            return new(false, "permission-denied", "Screen viewing is disabled for this device.");
        if (string.IsNullOrWhiteSpace(answerSdp) || answerSdp.Length > ScreenViewProtocol.MaxSdpLength)
            return new(false, "invalid-answer", "The WebRTC screen answer was invalid.");

        PendingView? pending;
        PendingView? expired;
        lock (_gate)
        {
            expired = TakeExpiredPending(now ?? DateTimeOffset.UtcNow);
            pending = _pending is not null &&
                _pending.ClientId == clientId &&
                _pending.OperationId == operationId &&
                _pending.OfferHash is not null
                ? _pending
                : null;
        }
        expired?.Release();
        if (pending is null)
            return new(false, "offer-expired", "The WebRTC screen offer expired. Start again.");

        string answerHash = HashSdp(answerSdp);
        string transcript = $"VolturaAir screen-view:answer:v2:{clientId}:{operationId}:{pending.DisplayId}:{pending.OfferHash}:{answerHash}";
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), clientSignature))
        {
            RemovePending(pending);
            pending.Release();
            return new(false, "invalid-proof", "The WebRTC screen answer could not be authenticated.");
        }

        try
        {
            pending.Peer.ApplyAnswer(answerSdp);
        }
        catch (Exception ex) when (ex is ScreenViewWebRtcException or ObjectDisposedException)
        {
            RemovePending(pending);
            pending.Release();
            return new(false, "invalid-answer", "The PC rejected the WebRTC screen answer.");
        }

        ActiveView? active = null;
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending) && _active is null)
            {
                active = new ActiveView(pending.ClientId, pending.DisplayId, pending.Peer);
                _pending = null;
                pending.DetachPeer();
                _active = active;
            }
        }
        if (active is null)
            return new(false, "offer-expired", "The WebRTC screen offer is no longer active.");
        active.Runner = Task.Run(() => RunActiveAsync(active));
        return new(true, "accepted", "The encrypted WebRTC screen connection is opening.");
    }

    public bool Stop(string clientId)
    {
        PendingView? pending = null;
        ActiveView? active = null;
        lock (_gate)
        {
            if (_pending?.ClientId == clientId)
            {
                pending = _pending;
                _pending = null;
            }
            if (_active?.ClientId == clientId) active = _active;
        }
        pending?.Release();
        active?.Stop.Cancel();
        return pending is not null || active is not null;
    }

    public ScreenViewOperationResult SetSource(string clientId, string displayId)
    {
        if (!CanStart(clientId))
            return new(false, "permission-denied", "Screen viewing is disabled for this device.");
        ScreenViewSourcesResult discovery = DiscoverSources(clientId);
        if (!discovery.Succeeded)
            return new(false, discovery.Code, discovery.Message);
        if (!discovery.Sources.Any(source => string.Equals(source.Id, displayId, StringComparison.Ordinal)))
            return new(false, "display-unavailable", "The selected display is no longer available.");

        ActiveView? active;
        lock (_gate)
        {
            active = _active?.ClientId == clientId ? _active : null;
            active?.SetDisplay(displayId);
        }
        if (active is null)
            return new(false, "not-viewing", "Start screen viewing before switching displays.");
        _capture.EndCapture();
        return new(true, "accepted", "The mirrored display was changed.");
    }

    private async Task RunActiveAsync(ActiveView active)
    {
        bool activityStarted = false;
        active.Peer.Stopped += active.OnPeerStopped;
        active.Peer.KeyFrameRequested += active.OnKeyFrameRequested;
        active.Peer.BitrateEstimated += active.OnBitrateEstimated;
        try
        {
            await active.Peer.Connected.WaitAsync(SignalingLifetime, active.Stop.Token).ConfigureAwait(false);
            if (!CanStart(active.ClientId)) return;
            activityStarted = true;
            ActivityChanged?.Invoke(this, new ScreenViewActivityChangedEventArgs(true, active.ClientId));
            await SendFramesAsync(active, active.Stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ScreenViewWebRtcException or ObjectDisposedException)
        {
        }
        finally
        {
            active.Peer.Stopped -= active.OnPeerStopped;
            active.Peer.KeyFrameRequested -= active.OnKeyFrameRequested;
            active.Peer.BitrateEstimated -= active.OnBitrateEstimated;
            _capture.EndCapture();
            lock (_gate)
            {
                if (ReferenceEquals(_active, active)) _active = null;
            }
            active.Release();
            if (activityStarted)
                ActivityChanged?.Invoke(this, new ScreenViewActivityChangedEventArgs(false, active.ClientId));
        }
    }

    private async Task SendFramesAsync(ActiveView active, CancellationToken cancellationToken)
    {
        long eventSequence = 0;
        var profile = new ScreenViewCaptureProfile(1920, 1080, true, 30);
        while (!cancellationToken.IsCancellationRequested)
        {
            string displayId = active.DisplayId;
            try
            {
                ScreenViewEncodedFrame? frame = await _capture.CaptureVideoAsync(
                    displayId,
                    profile,
                    active.TargetBitrate,
                    active.TakeForceKeyFrame(),
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(displayId, active.DisplayId, StringComparison.Ordinal)) continue;
                if (frame?.Cursor is not null)
                    active.Peer.TrySendEvent(ScreenViewRecordEncoder.EncodeCursor(++eventSequence, frame.Cursor));
                if (frame is { Bytes.Length: > 0 } && !active.Peer.TrySendH264(frame.Bytes, frame.FramesPerSecond))
                    active.RequestKeyFrame();
            }
            catch (ScreenViewCaptureException ex)
            {
                active.Peer.TrySendEvent(ScreenViewRecordEncoder.EncodeStatus(ex.Code, ex.Message));
                return;
            }
        }
    }

    private bool CanStart(string clientId) => Volatile.Read(ref _disposed) == 0 && _statusFactory.CanViewScreen(clientId);

    private ScreenViewSourcesResult DiscoverSources(string clientId)
    {
        if (!CanStart(clientId))
            return new(false, "permission-denied", "Screen viewing is disabled for this device.", []);

        try
        {
            IReadOnlyList<ScreenViewSource> sources = _capture.GetSources();
            return new(
                true,
                "accepted",
                sources.Count > 0 ? "Displays are available." : "No displays are available.",
                sources);
        }
        catch (ScreenViewCaptureException ex)
        {
            return new(false, ex.Code, ex.Message, []);
        }
    }

    private PendingView? TakeExpiredPending(DateTimeOffset now)
    {
        if (_pending is null || _pending.ExpiresAt > now) return null;
        PendingView expired = _pending;
        _pending = null;
        return expired;
    }

    private void RemovePending(PendingView pending)
    {
        lock (_gate) if (ReferenceEquals(_pending, pending)) _pending = null;
    }

    private void OnPairingRevoked(object? sender, PairingRevokedEventArgs e)
    {
        if (e.ClientId is null) StopAll(); else Stop(e.ClientId);
    }

    private void OnPermissionsChanged(object? sender, EventArgs e) => StopUnauthorized();
    private void OnDeveloperSettingsChanged(object? sender, EventArgs e) => StopUnauthorized();

    private void StopUnauthorized()
    {
        string? clientId;
        lock (_gate) clientId = _active?.ClientId ?? _pending?.ClientId;
        if (clientId is not null && !CanStart(clientId)) Stop(clientId);
    }

    private void StopAll()
    {
        string[] clientIds;
        lock (_gate)
        {
            clientIds = [.. new[] { _active?.ClientId, _pending?.ClientId }.OfType<string>().Distinct()];
        }
        foreach (string clientId in clientIds) Stop(clientId);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _pairingManager.PairingRevoked -= OnPairingRevoked;
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        AppDeveloperSettings.Changed -= OnDeveloperSettingsChanged;
        ActiveView? active;
        lock (_gate) active = _active;
        StopAll();
        if (active?.Runner is not null)
        {
            try { await active.Runner.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
        }
        _capture.EndCapture();
    }

    private static string HashSdp(string sdp) => ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));
    private static ScreenViewStartResult Failure(string code, string message) => new(false, code, message);

    private sealed class PendingView(
        string clientId,
        string operationId,
        string displayId,
        DateTimeOffset expiresAt,
        IScreenViewWebRtcPeer peer)
    {
        private IScreenViewWebRtcPeer? _peer = peer;
        public string ClientId { get; } = clientId;
        public string OperationId { get; } = operationId;
        public string DisplayId { get; } = displayId;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public IScreenViewWebRtcPeer Peer => _peer ?? throw new ObjectDisposedException(nameof(PendingView));
        public string? OfferHash { get; private set; }
        public void SetOffer(string offerHash) => OfferHash = offerHash;
        public void DetachPeer() => _peer = null;
        public void Release() { _peer?.Dispose(); _peer = null; }
    }

    private sealed class ActiveView(string clientId, string displayId, IScreenViewWebRtcPeer peer)
    {
        private string _displayId = displayId;
        private int _targetBitrate = InitialBitrate;
        private int _forceKeyFrame = 1;
        public string ClientId { get; } = clientId;
        public string DisplayId => Volatile.Read(ref _displayId);
        public int TargetBitrate => Volatile.Read(ref _targetBitrate);
        public IScreenViewWebRtcPeer Peer { get; } = peer;
        public CancellationTokenSource Stop { get; } = new();
        public Task? Runner { get; set; }
        public void SetDisplay(string displayId) { Volatile.Write(ref _displayId, displayId); RequestKeyFrame(); }
        public bool TakeForceKeyFrame() => Interlocked.Exchange(ref _forceKeyFrame, 0) != 0;
        public void RequestKeyFrame() => Interlocked.Exchange(ref _forceKeyFrame, 1);
        public void OnPeerStopped(object? sender, EventArgs e) => Stop.Cancel();
        public void OnKeyFrameRequested(object? sender, EventArgs e) => RequestKeyFrame();
        public void OnBitrateEstimated(object? sender, ScreenViewBitrateEventArgs e)
        {
            int bitrate = e.Bitrate switch
            {
                < 2_500_000 => 2_000_000,
                < 4_500_000 => 4_000_000,
                < 7_000_000 => 6_000_000,
                _ => 8_000_000
            };
            if (Interlocked.Exchange(ref _targetBitrate, bitrate) != bitrate) RequestKeyFrame();
        }
        public void Release() { Stop.Dispose(); Peer.Dispose(); }
    }
}

internal sealed class ScreenViewActivityChangedEventArgs(bool active, string clientId) : EventArgs
{
    public bool Active { get; } = active;
    public string ClientId { get; } = clientId;
}
