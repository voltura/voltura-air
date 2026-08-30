using System.Net.WebSockets;
using System.Text;

namespace VolturaAir.Host.Features.Apps;

internal sealed class AppsPreviewSessionCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan PreviewSignalingLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PreviewSendLifetime = TimeSpan.FromSeconds(3);
    private readonly HostStatusPayloadFactory _status;
    private readonly PairingManager _pairingManager;
    private readonly WebSocketTransport _transport;
    private readonly IAppsWindowAdapter _windows;
    private readonly bool _relayMode;
    private readonly Func<CancellationToken, Task<RelayTurnConfiguration?>> _getRelayTurnConfiguration;
    private readonly IFileTransferWebRtcPeerFactory _peerFactory;
    private readonly Func<string, WebSocket, string, IReadOnlyDictionary<string, AppsWindowSnapshot>?> _getWindowMap;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private AppsPreviewSession? _preview;
    private PendingPreviewEnsure? _pendingEnsure;
    private int _disposed;

    internal AppsPreviewSessionCoordinator(
        HostStatusPayloadFactory status,
        PairingManager pairingManager,
        WebSocketTransport transport,
        IAppsWindowAdapter windows,
        bool relayMode,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        IFileTransferWebRtcPeerFactory peerFactory,
        Func<string, WebSocket, string, IReadOnlyDictionary<string, AppsWindowSnapshot>?> getWindowMap)
    {
        _status = status;
        _pairingManager = pairingManager;
        _transport = transport;
        _windows = windows;
        _relayMode = relayMode;
        _getRelayTurnConfiguration = getRelayTurnConfiguration;
        _peerFactory = peerFactory;
        _getWindowMap = getWindowMap;
    }

    internal void Ensure(
        WebSocket socket,
        string clientId,
        string offerOperationId,
        bool includesVolturaAir)
    {
        lock (_gate)
        {
            if (!_status.CanPreviewOpenApps(clientId) ||
                includesVolturaAir && !_status.CanControlHostApplication(clientId))
            {
                if (_pendingEnsure is { } pending &&
                    pending.ClientId == clientId &&
                    ReferenceEquals(pending.Socket, socket))
                {
                    _pendingEnsure = null;
                }
                if (_preview is { } unauthorized &&
                    unauthorized.ClientId == clientId &&
                    ReferenceEquals(unauthorized.Socket, socket))
                {
                    unauthorized.Cancellation.Cancel();
                }
                return;
            }

            if (_preview is { } current)
            {
                if (current.ClientId == clientId && ReferenceEquals(current.Socket, socket))
                {
                    current.IncludesVolturaAir |= includesVolturaAir;
                }
                if (current.Cancellation.IsCancellationRequested)
                {
                    _pendingEnsure = new(socket, clientId, offerOperationId, includesVolturaAir);
                }
                return;
            }

            StartPreviewLocked(socket, clientId, offerOperationId, includesVolturaAir);
        }
    }

    private void StartPreviewLocked(
        WebSocket socket,
        string clientId,
        string offerOperationId,
        bool includesVolturaAir)
    {
#pragma warning disable CA2000 // AppsPreviewSession owns the linked cancellation source through its run task.
        var preview = new AppsPreviewSession(
            NewId(),
            clientId,
            socket,
            offerOperationId,
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        {
            IncludesVolturaAir = includesVolturaAir
        };
#pragma warning restore CA2000
        _preview = preview;
        _pendingEnsure = null;
        preview.RunTask = RunPreviewAsync(preview);
        _ = preview.RunTask.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal async Task AnswerAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string offerOperationId,
        string previewId,
        string answerSdp,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        AppsPreviewSession? preview;
        lock (_gate)
        {
            preview = _preview is { } current &&
                current.ClientId == clientId &&
                ReferenceEquals(current.Socket, socket) &&
                current.Id == previewId &&
                current.OfferOperationId == offerOperationId
                    ? current
                    : null;
        }

        if (preview?.Peer is null || preview.OfferHash is null)
        {
            await SendAnswerResultAsync(
                socket,
                operationId,
                false,
                "offer-expired",
                "The Apps preview offer expired.",
                cancellationToken);
            return;
        }

        string answerHash = FileTransferNegotiation.HashSdp(answerSdp);
        string transcript = AnswerTranscript(
            clientId,
            _pairingManager.HostIdentity.PublicKey,
            offerOperationId,
            operationId,
            previewId,
            preview.OfferHash,
            answerHash);
        if (!_pairingManager.VerifyClientSignature(
                clientId,
                Encoding.UTF8.GetBytes(transcript),
                clientSignature))
        {
            Cancel(clientId, socket);
            await SendAnswerResultAsync(
                socket,
                operationId,
                false,
                "invalid-proof",
                "The Apps preview answer could not be authenticated.",
                cancellationToken);
            return;
        }

        try
        {
            preview.Peer.ApplyAnswer(answerSdp);
            preview.AnswerApplied.TrySetResult();
            await SendAnswerResultAsync(
                socket,
                operationId,
                true,
                "accepted",
                "Apps previews connected.",
                cancellationToken);
        }
        catch (Exception exception) when (exception is FileTransferWebRtcException or ObjectDisposedException)
        {
            Cancel(clientId, socket);
            await SendAnswerResultAsync(
                socket,
                operationId,
                false,
                "invalid-answer",
                "The PC rejected the Apps preview answer.",
                cancellationToken);
        }
    }

    internal void Stop(string clientId, WebSocket socket, string previewId)
    {
        lock (_gate)
        {
            if (_preview is { } preview &&
                preview.ClientId == clientId &&
                ReferenceEquals(preview.Socket, socket) &&
                preview.Id == previewId)
            {
                preview.Cancellation.Cancel();
            }
        }
    }

    internal void Cancel(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            if (_pendingEnsure is { } pending &&
                pending.ClientId == clientId &&
                ReferenceEquals(pending.Socket, socket))
            {
                _pendingEnsure = null;
            }
            if (_preview is { } preview &&
                preview.ClientId == clientId &&
                ReferenceEquals(preview.Socket, socket))
            {
                preview.Cancellation.Cancel();
            }
        }
    }

    internal void PermissionsChanged()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _sendGate.Wait();
            try
            {
                if (_pendingEnsure is { } pending &&
                    (!_status.CanPreviewOpenApps(pending.ClientId) ||
                        pending.IncludesVolturaAir && !_status.CanControlHostApplication(pending.ClientId)))
                {
                    _pendingEnsure = null;
                }
                if (_preview is { } preview &&
                    (!_status.CanPreviewOpenApps(preview.ClientId) ||
                        preview.IncludesVolturaAir && !_status.CanControlHostApplication(preview.ClientId)))
                {
                    preview.Cancellation.Cancel();
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        AppsPreviewSession? preview;
        lock (_gate)
        {
            preview = _preview;
            _pendingEnsure = null;
        }

        if (preview is not null)
        {
            await preview.Cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (preview?.RunTask is { } runTask)
        {
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or TimeoutException or FileTransferWebRtcException)
            {
            }
        }

        _lifetime.Dispose();
        if (preview?.RunTask is { IsCompleted: false } pendingRun)
        {
            _ = pendingRun.ContinueWith(
                _ => _sendGate.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        else
        {
            _sendGate.Dispose();
        }
    }

    private async Task RunPreviewAsync(AppsPreviewSession preview)
    {
        try
        {
            RelayTurnConfiguration? relay = null;
            if (_relayMode)
            {
                try
                {
                    relay = await _getRelayTurnConfiguration(preview.Token).ConfigureAwait(false);
                }
                catch (RelayQuotaReachedException)
                {
                    await PublishEndedAsync(preview, "relay-quota", "Apps previews are unavailable because the Relay usage limit was reached.").ConfigureAwait(false);
                    return;
                }

                if (relay is null || FileTransferNegotiation.WouldReachRelayCutoff(
                        relay.UsageBytes,
                        relay.CutoffBytes,
                        AppsProtocol.MaximumPreviewBytes))
                {
                    await PublishEndedAsync(preview, "relay-quota", "Apps previews are unavailable because the Relay usage limit was reached.").ConfigureAwait(false);
                    return;
                }
            }

            preview.Relay = relay;
            preview.Peer = _peerFactory.Create(new FileTransferPeerConfiguration(
                relay?.HostIceServerUris ?? [],
                RelayOnly: relay is not null,
                DataChannelLabel: AppsProtocol.DataChannelLabel,
                MinimumRecordBytes: AppsProtocol.MinimumRecordBytes,
                MaximumRecordBytes: AppsProtocol.MaximumRecordBytes,
                CoalesceIncomingMessages: true));
            using (var signaling = CancellationTokenSource.CreateLinkedTokenSource(preview.Token))
            {
                signaling.CancelAfter(PreviewSignalingLifetime);
                string offerSdp = await preview.Peer.CreateOfferAsync(signaling.Token).ConfigureAwait(false);
                preview.OfferHash = FileTransferNegotiation.HashSdp(offerSdp);
                string transcript = OfferTranscript(
                    preview.ClientId,
                    _pairingManager.HostIdentity.PublicKey,
                    preview.OfferOperationId,
                    preview.Id,
                    preview.OfferHash);
                await _transport.SendAsync(preview.Socket, new
                {
                    type = "apps.preview.offer",
                    operationId = preview.OfferOperationId,
                    previewId = preview.Id,
                    offerSdp,
                    hostSignature = _pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(transcript)),
                    iceServers = relay?.IceServers,
                    turnExpiresAt = relay?.ExpiresAt
                }, signaling.Token).ConfigureAwait(false);
                await preview.AnswerApplied.Task.WaitAsync(signaling.Token).ConfigureAwait(false);
                await preview.Peer.Opened.WaitAsync(signaling.Token).ConfigureAwait(false);
            }

            await foreach (byte[] message in preview.Peer.Messages.ReadAllAsync(preview.Token).ConfigureAwait(false))
            {
                if (!AppsProtocol.TryParseRequest(message, out string revision, out string[] windowIds))
                {
                    await PublishEndedAsync(preview, "invalid-record", "The Apps preview request was invalid.").ConfigureAwait(false);
                    return;
                }

                await SendRequestedPreviewsAsync(preview, revision, windowIds).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (preview.Token.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            await PublishEndedAsync(preview, "offer-expired", "The Apps preview offer expired.").ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is FileTransferWebRtcException or ObjectDisposedException or WebSocketException)
        {
            await PublishEndedAsync(preview, "transport-lost", "Apps previews are unavailable.").ConfigureAwait(false);
        }
        finally
        {
            if (preview.Peer is not null)
            {
                await preview.Peer.DisposeAsync().ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (ReferenceEquals(_preview, preview))
                {
                    _preview = null;
                    if (Volatile.Read(ref _disposed) == 0 &&
                        _pendingEnsure is { } pending &&
                        _status.CanPreviewOpenApps(pending.ClientId) &&
                        (!pending.IncludesVolturaAir ||
                            _status.CanControlHostApplication(pending.ClientId)))
                    {
                        StartPreviewLocked(
                            pending.Socket,
                            pending.ClientId,
                            pending.OfferOperationId,
                            pending.IncludesVolturaAir);
                    }
                    else
                    {
                        _pendingEnsure = null;
                    }
                }
            }

            preview.Cancellation.Dispose();
        }
    }

    private async Task SendRequestedPreviewsAsync(
        AppsPreviewSession preview,
        string revision,
        IReadOnlyList<string> windowIds)
    {
        if (!_status.CanPreviewOpenApps(preview.ClientId))
        {
            await PublishEndedAsync(preview, "permission-revoked", "Screen viewing permission was revoked.").ConfigureAwait(false);
            await preview.Cancellation.CancelAsync().ConfigureAwait(false);
            return;
        }

        IReadOnlyDictionary<string, AppsWindowSnapshot>? windows =
            _getWindowMap(preview.ClientId, preview.Socket, revision);
        foreach (string windowId in windowIds.Distinct(StringComparer.Ordinal))
        {
            preview.Token.ThrowIfCancellationRequested();
            if (windows is null || !windows.TryGetValue(windowId, out var window) || !window.PreviewSupported)
            {
                await SendRecordAsync(preview, AppsProtocol.CreateUnavailableHeader(windowId)).ConfigureAwait(false);
                continue;
            }

            bool includeVolturaAir = _status.CanControlHostApplication(preview.ClientId);
            AppsPreviewCaptureResult capture = _windows.CapturePreview(window, includeVolturaAir, preview.Token);
            if (!capture.Succeeded || capture.Content is null ||
                capture.Content.Length > AppsProtocol.MaximumPreviewBytes)
            {
                await SendRecordAsync(
                    preview,
                    AppsProtocol.CreateUnavailableHeader(windowId),
                    window.IsVolturaAir).ConfigureAwait(false);
                continue;
            }

            if (!_status.CanPreviewOpenApps(preview.ClientId) ||
                window.IsVolturaAir && !_status.CanControlHostApplication(preview.ClientId))
            {
                await preview.Cancellation.CancelAsync().ConfigureAwait(false);
                return;
            }

            if (preview.Relay is { } relay &&
                FileTransferNegotiation.WouldReachRelayCutoff(
                    GetProjectedRelayUsage(relay.UsageBytes, preview.RelayPayloadBytes),
                    relay.CutoffBytes,
                    capture.Content.Length))
            {
                await PublishEndedAsync(preview, "relay-quota", "Apps previews stopped at the Relay usage limit.").ConfigureAwait(false);
                await preview.Cancellation.CancelAsync().ConfigureAwait(false);
                return;
            }

            await SendRecordAsync(
                preview,
                AppsProtocol.CreatePreviewHeader(
                    windowId,
                    capture.Width,
                    capture.Height,
                    capture.Content.Length),
                window.IsVolturaAir).ConfigureAwait(false);
            for (int offset = 0; offset < capture.Content.Length; offset += AppsProtocol.PreviewChunkBytes)
            {
                int count = Math.Min(AppsProtocol.PreviewChunkBytes, capture.Content.Length - offset);
                await SendRecordAsync(
                    preview,
                    AppsProtocol.CreatePreviewData(
                        windowId,
                        offset,
                        capture.Content.AsSpan(offset, count)),
                    window.IsVolturaAir).ConfigureAwait(false);
            }

            preview.RelayPayloadBytes = checked(preview.RelayPayloadBytes + capture.Content.Length);
        }
    }

    private async Task SendRecordAsync(
        AppsPreviewSession preview,
        byte[] record,
        bool requiresHostControl = false)
    {
        if (preview.Peer is null)
        {
            throw new FileTransferWebRtcException("The Apps preview connection is unavailable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(preview.Token);
        timeout.CancelAfter(PreviewSendLifetime);
        while (true)
        {
            await _sendGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            bool authorized;
            bool sent = false;
            try
            {
                preview.Token.ThrowIfCancellationRequested();
                authorized = _status.CanPreviewOpenApps(preview.ClientId) &&
                    (!requiresHostControl || _status.CanControlHostApplication(preview.ClientId));
                if (authorized)
                {
                    sent = preview.Peer.TrySend(record);
                }
            }
            finally
            {
                _sendGate.Release();
            }

            if (!authorized)
            {
                await preview.Cancellation.CancelAsync().ConfigureAwait(false);
                preview.Token.ThrowIfCancellationRequested();
            }
            if (sent)
            {
                return;
            }
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task PublishEndedAsync(AppsPreviewSession preview, string reason, string message)
    {
        try
        {
            await _transport.SendAsync(preview.Socket, new
            {
                type = "apps.preview.ended",
                previewId = preview.Id,
                reason,
                message
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private Task SendAnswerResultAsync(
        WebSocket socket,
        string operationId,
        bool succeeded,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new
        {
            type = "apps.preview.answer.result",
            operationId,
            succeeded,
            code,
            message
        }, cancellationToken);

    private static long GetProjectedRelayUsage(long currentUsage, long previewPayloadBytes)
    {
        try
        {
            return checked(currentUsage + previewPayloadBytes * 3);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static string OfferTranscript(
        string clientId,
        string hostPublicKey,
        string operationId,
        string previewId,
        string offerHash) =>
        $"VolturaAir apps-preview:offer:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{previewId}\n{offerHash}";

    private static string AnswerTranscript(
        string clientId,
        string hostPublicKey,
        string offerOperationId,
        string answerOperationId,
        string previewId,
        string offerHash,
        string answerHash) =>
        $"VolturaAir apps-preview:answer:v1\n{clientId}\n{hostPublicKey}\n{offerOperationId}\n{answerOperationId}\n{previewId}\n{offerHash}\n{answerHash}";

    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed record PendingPreviewEnsure(
        WebSocket Socket,
        string ClientId,
        string OfferOperationId,
        bool IncludesVolturaAir);

}
