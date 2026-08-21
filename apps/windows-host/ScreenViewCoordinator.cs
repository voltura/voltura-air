using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal sealed class ScreenViewCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan SignalingLifetime = TimeSpan.FromSeconds(15);
    private readonly Lock _gate = new();
    private readonly PairingManager _pairingManager;
    private readonly HostStatusPayloadFactory _statusFactory;
    private readonly IScreenViewCaptureSource _capture;
    private readonly IScreenViewWebRtcPeerFactory _peerFactory;
    private readonly IAppLogWriter _appLog;
    private readonly InputDispatcher? _inputDispatcher;
    private readonly ISystemPowerController? _powerController;
    private PendingView? _pending;
    private PendingView? _answering;
    private ActiveView? _active;
    private int _disposed;

    public ScreenViewCoordinator(
        PairingManager pairingManager,
        HostStatusPayloadFactory statusFactory,
        IScreenViewCaptureSource? capture = null,
        IScreenViewWebRtcPeerFactory? peerFactory = null,
        IAppLogWriter? appLog = null,
        InputDispatcher? inputDispatcher = null,
        ISystemPowerController? powerController = null)
    {
        _pairingManager = pairingManager;
        _statusFactory = statusFactory;
        _capture = capture ?? new DxgiScreenViewCaptureSource();
        _peerFactory = peerFactory ?? new ScreenViewWebRtcPeerFactory();
        _appLog = appLog ?? NullAppLog.Instance;
        _inputDispatcher = inputDispatcher;
        _powerController = powerController;
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
        RelayTurnConfiguration? relay = null,
        DateTimeOffset? now = null)
    {
        if (!CanStart(clientId))
            return Failure("permission-denied", "Screen viewing is disabled for this device.");
        ScreenViewSourcesResult discovery = DiscoverSources(clientId);
        if (!discovery.Succeeded)
            return Failure(discovery.Code, discovery.Message);
        ScreenViewSource? source = discovery.Sources.FirstOrDefault(source => string.Equals(source.Id, displayId, StringComparison.Ordinal));
        if (source is null)
            return Failure("display-unavailable", "The selected display is no longer available.");
        VirtualDesktopBounds virtualDesktop = VirtualDesktopBounds.From(discovery.Sources);

        string clientTranscript = $"VolturaAir screen-view:start:v2:{clientId}:{operationId}:{displayId}";
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(clientTranscript), clientSignature))
            return Failure("invalid-proof", "The WebRTC screen-view request could not be authenticated.");

        IScreenViewWebRtcPeer peer;
        try
        {
            peer = _peerFactory.Create(relay is null ? null : new ScreenViewPeerConfiguration(relay.HostIceServerUris, RelayOnly: true));
        }
        catch (Exception ex) when (ex is ScreenViewWebRtcException or DllNotFoundException or BadImageFormatException)
        {
            if (relay is not null)
                _appLog.Write(new AppLogEntry("relay_turn", "windows_host", Action: "screen_transport_failed", Outcome: "failed", Code: "setup"));
            return Failure("webrtc-unavailable", "The WebRTC screen transport is unavailable on this PC.");
        }

        var createdAt = now ?? DateTimeOffset.UtcNow;
        DirectScreenQualityMode directQuality = relay is null
            ? AppScreenViewSettings.Load().DirectQuality
            : relay.EffectiveQuality == RelayScreenQuality.DataSaver
                ? DirectScreenQualityMode.DataSaver
                : DirectScreenQualityMode.Automatic;
        var pending = new PendingView(
            clientId,
            operationId,
            source,
            virtualDesktop,
            createdAt + SignalingLifetime,
            peer,
            directQuality,
            relay?.MaximumBitrate);
        PendingView? expired;
        bool busy;
        lock (_gate)
        {
            expired = TakeExpiredPending(createdAt);
            busy = _active is not null || _pending is not null;
            busy = busy || _answering is not null;
            if (!busy)
            {
                _pending = pending;
                pending.ExpiryTask = ExpirePendingAsync(pending);
            }
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
            bool offerStillPending;
            lock (_gate)
            {
                offerStillPending = ReferenceEquals(_pending, pending);
            }
            if (!offerStillPending)
            {
                pending.Release();
                return Failure("offer-expired", "The WebRTC screen offer expired. Start again.");
            }
            string hostTranscript = $"VolturaAir screen-view:offer:v2:{clientId}:{operationId}:{displayId}:{offerHash}";
            return new ScreenViewStartResult(
                true,
                "accepted",
                "The encrypted WebRTC screen connection is ready.",
                offerSdp,
                _pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(hostTranscript)),
                relay?.IceServers,
                relay?.ExpiresAt,
                relay?.UsageBytes,
                relay?.CheckedAt,
                relay?.EffectiveQuality);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ScreenViewWebRtcException or ObjectDisposedException)
        {
            RemovePending(pending);
            pending.Release();
            if (relay is not null && ex is not OperationCanceledException)
                _appLog.Write(new AppLogEntry("relay_turn", "windows_host", Action: "screen_transport_failed", Outcome: "failed", Code: "offer"));
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
            if (pending is not null)
            {
                _pending = null;
                _answering = pending;
            }
        }
        expired?.Release();
        if (pending is null)
            return new(false, "offer-expired", "The WebRTC screen offer expired. Start again.");

        try
        {
            string answerHash = HashSdp(answerSdp);
            string transcript = $"VolturaAir screen-view:answer:v2:{clientId}:{operationId}:{pending.DisplayId}:{pending.OfferHash}:{answerHash}";
            if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), clientSignature))
            {
                RemoveAnswering(pending);
                pending.Release();
                return new(false, "invalid-proof", "The WebRTC screen answer could not be authenticated.");
            }

            try
            {
                pending.Peer.ApplyAnswer(answerSdp);
            }
            catch (Exception ex) when (ex is ScreenViewWebRtcException or ObjectDisposedException)
            {
                RemoveAnswering(pending);
                pending.Release();
                return new(false, "invalid-answer", "The PC rejected the WebRTC screen answer.");
            }

            ActiveView? active = null;
            lock (_gate)
            {
                if (ReferenceEquals(_answering, pending) && !pending.StopRequested && _active is null)
                {
                    active = new ActiveView(
                        pending.ClientId,
                        pending.OperationId,
                        pending.Source,
                        pending.VirtualDesktop,
                        pending.Peer,
                        pending.DirectQuality,
                        pending.MaximumBitrate);
                    _answering = null;
                    pending.DetachPeer();
                    _active = active;
                }
                else if (ReferenceEquals(_answering, pending))
                {
                    _answering = null;
                }
            }
            if (active is null)
            {
                pending.Release();
                return new(false, "offer-expired", "The WebRTC screen offer is no longer active.");
            }
            active.Runner = Task.Run(() => RunActiveAsync(active));
            return new(true, "accepted", "The encrypted WebRTC screen connection is opening.");
        }
        finally
        {
            pending.CompleteAnswerProcessing();
        }
    }

    public bool Stop(string clientId)
    {
        PendingView? pending = null;
        ActiveView? active = null;
        bool releasePending = false;
        bool answering = false;
        lock (_gate)
        {
            if (_pending?.ClientId == clientId)
            {
                pending = _pending;
                _pending = null;
                releasePending = true;
            }
            if (_answering?.ClientId == clientId)
            {
                _answering.StopRequested = true;
                answering = true;
            }
            if (_active?.ClientId == clientId) active = _active;
        }
        if (releasePending) pending!.Release();
        if (active is not null)
        {
            ReleaseHeldButtons(active);
            active.Stop.Cancel();
        }
        return releasePending || answering || active is not null;
    }

    public ScreenViewStoppedSession? StopActive()
    {
        ActiveView? active;
        lock (_gate)
        {
            active = _active is not null && _active.TryClaimHostStop() ? _active : null;
        }
        if (active is null) return null;
        ReleaseHeldButtons(active);
        active.Stop.Cancel();
        return new(active.ClientId, active.OperationId);
    }

    public ScreenViewOperationResult SetSource(string clientId, string displayId)
    {
        if (!CanStart(clientId))
            return new(false, "permission-denied", "Screen viewing is disabled for this device.");
        ScreenViewSourcesResult discovery = DiscoverSources(clientId);
        if (!discovery.Succeeded)
            return new(false, discovery.Code, discovery.Message);
        ScreenViewSource? source = discovery.Sources.FirstOrDefault(source => string.Equals(source.Id, displayId, StringComparison.Ordinal));
        if (source is null)
            return new(false, "display-unavailable", "The selected display is no longer available.");
        VirtualDesktopBounds virtualDesktop = VirtualDesktopBounds.From(discovery.Sources);

        ActiveView? active;
        lock (_gate)
        {
            active = _active?.ClientId == clientId ? _active : null;
            if (active is not null)
            {
                ReleaseHeldButtonsLocked(active);
                active.SetSource(source, virtualDesktop);
            }
        }
        if (active is null)
            return new(false, "not-viewing", "Start screen viewing before switching displays.");
        _capture.EndCapture();
        return new(true, "accepted", "The mirrored display was changed.");
    }

    public void ReportQuality(string clientId, string operationId, ScreenViewReceiverQuality quality)
    {
        ActiveView? active;
        ScreenViewQualityProfile previous;
        lock (_gate)
        {
            active = _active?.ClientId == clientId && _active.OperationId == operationId ? _active : null;
            if (active is null) return;
            previous = active.Quality;
            if (!active.ReportReceiverQuality(quality)) return;
            active.RequestKeyFrame();
        }
        ScreenViewQualityProfile current = active.Quality;
        _appLog.Write(new AppLogEntry(
            "screen_view",
            "windows_host",
            Action: current.RequiredBitrate < previous.RequiredBitrate ? "quality_reduced" : "quality_increased",
            Outcome: "accepted",
            Code: $"{current.Width}x{current.Height}@{current.FramesPerSecond}"));
    }

    public ScreenPointerDispatchResult DispatchPointer(string clientId, ValidatedInputCommand command)
    {
        if (_inputDispatcher is null)
            return new(false, "VAIR-SCREEN-POINTER-UNAVAILABLE", "Direct mouse control is unavailable on this PC.");
        if (!_statusFactory.CanViewScreen(clientId) || !_statusFactory.CanUseRemoteInput(clientId))
            return new(false, "VAIR-INPUT-DENIED", "Pointer and keyboard control is disabled for this device on the PC.");

        lock (_gate)
        {
            ActiveView? active = _active?.ClientId == clientId ? _active : null;
            if (active is null)
                return new(false, "VAIR-SCREEN-NOT-VIEWING", "Start screen viewing before using direct mouse control.");
            if (!string.Equals(active.DisplayId, command.DisplayId, StringComparison.Ordinal))
                return new(false, "VAIR-SCREEN-STALE-DISPLAY", "The direct mouse command targeted a display that is no longer selected.");

            ScreenPointerPosition position = MapPointer(active.Source, active.VirtualDesktop, command.X, command.Y);
            try
            {
                _ = _powerController?.DismissBlackoutIfActive();
                if (!_inputDispatcher.DispatchScreenPointer(
                    command,
                    position.DesktopX,
                    position.DesktopY,
                    position.AbsoluteX,
                    position.AbsoluteY,
                    out var outcome))
                {
                    return new(false, "VAIR-INPUT-UNSUPPORTED", "Unsupported direct mouse command.");
                }

                if (outcome == InputDispatchOutcome.Failed)
                    return new(false, "VAIR-INPUT-DISPATCH-FAILED", "Windows did not complete this direct mouse action.");

                if (command.Kind == InputCommandKind.ScreenPointerButton)
                {
                    if (string.Equals(command.Action, "down", StringComparison.Ordinal)) active.Hold(command.Button!);
                    else active.Release(command.Button!);
                }

                return new(true, "accepted", "The direct mouse command was accepted.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
            {
                InputDispatchDiagnostics.Write(command.Type, null, string.Empty, ex);
                ReleaseHeldButtonsLocked(active);
                return new(false, ex is InputDispatchException ? "VAIR-INPUT-NATIVE-SEND-FAILED" : "VAIR-INPUT-DISPATCH-FAILED", "Windows did not accept this direct mouse action. Try again.");
            }
        }
    }

    private async Task RunActiveAsync(ActiveView active)
    {
        bool activityStarted = false;
        active.Peer.Stopped += active.OnPeerStopped;
        active.Peer.KeyFrameRequested += active.OnKeyFrameRequested;
        try
        {
            await active.Peer.Connected.WaitAsync(SignalingLifetime, active.Stop.Token).ConfigureAwait(false);
            if (!CanStart(active.ClientId)) return;
            activityStarted = true;
            NotifyActivityChanged(true, active.ClientId, active.OperationId);
            await SendFramesAsync(active, active.Stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ScreenViewWebRtcException or ObjectDisposedException)
        {
        }
        finally
        {
            active.Peer.Stopped -= active.OnPeerStopped;
            active.Peer.KeyFrameRequested -= active.OnKeyFrameRequested;
            _capture.EndCapture();
            ReleaseHeldButtons(active);
            lock (_gate)
            {
                if (ReferenceEquals(_active, active)) _active = null;
            }
            active.Release();
            if (activityStarted)
                NotifyActivityChanged(false, active.ClientId, active.OperationId);
        }
    }

    private void NotifyActivityChanged(bool isActive, string clientId, string operationId)
    {
        var eventArgs = new ScreenViewActivityChangedEventArgs(isActive, clientId, operationId);
        foreach (EventHandler<ScreenViewActivityChangedEventArgs> subscriber in
            ActivityChanged?.GetInvocationList().Cast<EventHandler<ScreenViewActivityChangedEventArgs>>() ?? [])
        {
            try
            {
                subscriber(this, eventArgs);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _appLog.Write(new AppLogEntry(
                    "screen_view",
                    "windows_host",
                    Action: "activity_observer_failed",
                    Outcome: "failed",
                    Code: ex.GetType().Name));
            }
        }
    }

    private void ReleaseHeldButtons(ActiveView active)
    {
        lock (_gate) ReleaseHeldButtonsLocked(active);
    }

    private void ReleaseHeldButtonsLocked(ActiveView active)
    {
        if (!active.TakeHeldButtons() || _inputDispatcher is null) return;
        try { _inputDispatcher.ReleaseMouseButtons(); }
        catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
        {
            InputDispatchDiagnostics.Write("screen.pointer.cleanup", null, string.Empty, ex);
        }
    }

    internal static ScreenPointerPosition MapPointer(
        ScreenViewSource source,
        VirtualDesktopBounds virtualDesktop,
        double normalizedX,
        double normalizedY)
    {
        int rotatedWidth = source.Rotation is ScreenViewRotation.Rotate90 or ScreenViewRotation.Rotate270 ? source.Height : source.Width;
        int rotatedHeight = source.Rotation is ScreenViewRotation.Rotate90 or ScreenViewRotation.Rotate270 ? source.Width : source.Height;
        int rotatedX = (int)Math.Round(Math.Clamp(normalizedX, 0, 1) * Math.Max(0, rotatedWidth - 1), MidpointRounding.AwayFromZero);
        int rotatedY = (int)Math.Round(Math.Clamp(normalizedY, 0, 1) * Math.Max(0, rotatedHeight - 1), MidpointRounding.AwayFromZero);
        (int sourceX, int sourceY) = source.Rotation switch
        {
            ScreenViewRotation.Rotate90 => (rotatedY, source.Height - 1 - rotatedX),
            ScreenViewRotation.Rotate180 => (source.Width - 1 - rotatedX, source.Height - 1 - rotatedY),
            ScreenViewRotation.Rotate270 => (source.Width - 1 - rotatedY, rotatedX),
            _ => (rotatedX, rotatedY)
        };
        int desktopX = source.DesktopLeft + sourceX;
        int desktopY = source.DesktopTop + sourceY;
        int absoluteX = NormalizeAbsolute(desktopX, virtualDesktop.Left, virtualDesktop.Width);
        int absoluteY = NormalizeAbsolute(desktopY, virtualDesktop.Top, virtualDesktop.Height);
        return new(desktopX, desktopY, absoluteX, absoluteY);
    }

    private static int NormalizeAbsolute(int coordinate, int origin, int extent) =>
        extent <= 1
            ? 0
            : (int)Math.Round((double)(coordinate - origin) * ushort.MaxValue / (extent - 1), MidpointRounding.AwayFromZero);

    private async Task SendFramesAsync(ActiveView active, CancellationToken cancellationToken)
    {
        long eventSequence = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            string displayId = active.DisplayId;
            ScreenViewQualityProfile quality = active.Quality;
            try
            {
                ScreenViewEncodedFrame? frame = await _capture.CaptureVideoAsync(
                    displayId,
                    quality.CaptureProfile,
                    quality.TargetBitrate,
                    active.TakeForceKeyFrame(),
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(displayId, active.DisplayId, StringComparison.Ordinal)) continue;
                if (frame?.Cursor is not null)
                    active.Peer.TrySendEvent(ScreenViewRecordEncoder.EncodeCursor(++eventSequence, frame.Cursor));
                if (frame is { Bytes.Length: > 0 } && !active.Peer.TrySendH264(frame.Bytes, frame.FramesPerSecond))
                {
                    active.RequestKeyFrame();
                    active.ReportBackpressure();
                }
            }
            catch (ScreenViewCaptureException ex)
            {
                if ((string.Equals(ex.Code, "encoder-unavailable", StringComparison.Ordinal) ||
                    string.Equals(ex.Code, "encoder-failed", StringComparison.Ordinal)) &&
                    active.ReportProfileUnsupported())
                {
                    active.RequestKeyFrame();
                    continue;
                }
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

    private void RemoveAnswering(PendingView pending)
    {
        lock (_gate) if (ReferenceEquals(_answering, pending)) _answering = null;
    }

    private async Task ExpirePendingAsync(PendingView pending)
    {
        CancellationToken expiry = pending.ExpiryCancellation.Token;
        try
        {
            await Task.Delay(SignalingLifetime, expiry).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (expiry.IsCancellationRequested)
        {
            return;
        }

        bool release;
        lock (_gate)
        {
            release = ReferenceEquals(_pending, pending);
            if (release) _pending = null;
        }
        if (release) pending.Release();
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
        ActiveView? active;
        lock (_gate)
        {
            clientId = _active?.ClientId ?? _pending?.ClientId ?? _answering?.ClientId;
            active = _active;
        }
        if (clientId is not null && !CanStart(clientId)) Stop(clientId);
        else if (active is not null && !_statusFactory.CanUseRemoteInput(active.ClientId)) ReleaseHeldButtons(active);
    }

    private void StopAll()
    {
        string[] clientIds;
        lock (_gate)
        {
            clientIds = [.. new[] { _active?.ClientId, _pending?.ClientId, _answering?.ClientId }.OfType<string>().Distinct()];
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
        PendingView? answering;
        lock (_gate)
        {
            active = _active;
            answering = _answering;
        }
        StopAll();
        if (active?.Runner is not null)
        {
            try
            {
                await active.Runner.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _capture.EndCapture();
                active.Release();
                _ = ObserveLateCleanupAsync(active.Runner);
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (answering is not null)
        {
            try
            {
                await answering.AnswerCompletion.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                answering.Release();
                _ = ObserveLateCleanupAsync(answering.AnswerCompletion);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _capture.EndCapture();
    }

    private static async Task ObserveLateCleanupAsync(Task cleanup)
    {
        try { await cleanup.ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private static string HashSdp(string sdp) => ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));
    private static ScreenViewStartResult Failure(string code, string message) => new(false, code, message);

    private sealed class PendingView(
        string clientId,
        string operationId,
        ScreenViewSource source,
        VirtualDesktopBounds virtualDesktop,
        DateTimeOffset expiresAt,
        IScreenViewWebRtcPeer peer,
        DirectScreenQualityMode directQuality,
        int? maximumBitrate)
    {
        private IScreenViewWebRtcPeer? _peer = peer;
        public string ClientId { get; } = clientId;
        public string OperationId { get; } = operationId;
        public ScreenViewSource Source { get; } = source;
        public string DisplayId => Source.Id;
        public VirtualDesktopBounds VirtualDesktop { get; } = virtualDesktop;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public IScreenViewWebRtcPeer Peer => Volatile.Read(ref _peer) ?? throw new ObjectDisposedException(nameof(PendingView));
        public DirectScreenQualityMode DirectQuality { get; } = directQuality;
        public int? MaximumBitrate { get; } = maximumBitrate;
        public string? OfferHash { get; private set; }
        public bool StopRequested { get; set; }
        public CancellationTokenSource ExpiryCancellation { get; } = new();
        public Task? ExpiryTask { get; set; }
        private readonly TaskCompletionSource _answerCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task AnswerCompletion => _answerCompletion.Task;
        private int _expiryClosed;
        public void SetOffer(string offerHash) => OfferHash = offerHash;
        public void CompleteAnswerProcessing() => _answerCompletion.TrySetResult();
        public void DetachPeer() { CloseExpiry(); _peer = null; }
        public void Release()
        {
            CloseExpiry();
            Interlocked.Exchange(ref _peer, null)?.Dispose();
        }
        private void CloseExpiry()
        {
            if (Interlocked.Exchange(ref _expiryClosed, 1) != 0) return;
            ExpiryCancellation.Cancel();
            ExpiryCancellation.Dispose();
        }
    }

    private sealed class ActiveView(
        string clientId,
        string operationId,
        ScreenViewSource source,
        VirtualDesktopBounds virtualDesktop,
        IScreenViewWebRtcPeer peer,
        DirectScreenQualityMode directQuality,
        int? maximumBitrate)
    {
        private ScreenViewSource _source = source;
        private VirtualDesktopBounds _virtualDesktop = virtualDesktop;
        private readonly HashSet<string> _heldButtons = new(StringComparer.Ordinal);
        private readonly ScreenViewQualityController _quality = new(source, directQuality, maximumBitrate);
        private int _forceKeyFrame = 1;
        private int _released;
        private bool _hostStopClaimed;
        public string ClientId { get; } = clientId;
        public string OperationId { get; } = operationId;
        public ScreenViewSource Source => Volatile.Read(ref _source);
        public string DisplayId => Source.Id;
        public VirtualDesktopBounds VirtualDesktop => Volatile.Read(ref _virtualDesktop);
        public ScreenViewQualityProfile Quality => _quality.Current;
        public IScreenViewWebRtcPeer Peer { get; } = peer;
        public CancellationTokenSource Stop { get; } = new();
        public Task? Runner { get; set; }
        public void SetSource(ScreenViewSource source, VirtualDesktopBounds virtualDesktop)
        {
            Volatile.Write(ref _source, source);
            Volatile.Write(ref _virtualDesktop, virtualDesktop);
            _quality.SetSource(source);
            RequestKeyFrame();
        }
        public void Hold(string button) => _heldButtons.Add(button);
        public void Release(string button) => _heldButtons.Remove(button);
        public bool TryClaimHostStop()
        {
            if (_hostStopClaimed) return false;
            _hostStopClaimed = true;
            return true;
        }
        public bool TakeHeldButtons()
        {
            bool held = _heldButtons.Count > 0;
            _heldButtons.Clear();
            return held;
        }
        public bool TakeForceKeyFrame() => Interlocked.Exchange(ref _forceKeyFrame, 0) != 0;
        public void RequestKeyFrame() => Interlocked.Exchange(ref _forceKeyFrame, 1);
        public void OnPeerStopped(object? sender, EventArgs e) => Stop.Cancel();
        public void OnKeyFrameRequested(object? sender, EventArgs e) => RequestKeyFrame();
        public bool ReportBackpressure() => _quality.ReportBackpressure(DateTimeOffset.UtcNow);
        public bool ReportReceiverQuality(ScreenViewReceiverQuality quality) =>
            _quality.ReportReceiverQuality(quality, DateTimeOffset.UtcNow);
        public bool ReportProfileUnsupported() => _quality.ReportProfileUnsupported(DateTimeOffset.UtcNow);
        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            Stop.Dispose();
            Peer.Dispose();
        }
    }
}

internal readonly record struct ScreenPointerPosition(int DesktopX, int DesktopY, int AbsoluteX, int AbsoluteY);

internal sealed record VirtualDesktopBounds(int Left, int Top, int Width, int Height)
{
    public static VirtualDesktopBounds From(IReadOnlyList<ScreenViewSource> sources)
    {
        if (sources.Count == 0) return new(0, 0, 1, 1);
        int left = sources.Min(source => source.DesktopLeft);
        int top = sources.Min(source => source.DesktopTop);
        int right = sources.Max(source => source.DesktopLeft + source.Width);
        int bottom = sources.Max(source => source.DesktopTop + source.Height);
        return new(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}

internal sealed class ScreenViewActivityChangedEventArgs(bool active, string clientId, string operationId) : EventArgs
{
    public bool Active { get; } = active;
    public string ClientId { get; } = clientId;
    public string OperationId { get; } = operationId;
}

internal sealed record ScreenViewStoppedSession(string ClientId, string OperationId);
