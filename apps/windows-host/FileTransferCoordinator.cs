using System.Net.WebSockets;
using System.Text;

namespace VolturaAir.Host;

internal sealed class FileTransferCoordinator : IAsyncDisposable
{
    private readonly FileManagerService _files;
    private readonly HostStatusPayloadFactory _status;
    private readonly PairingManager _pairingManager;
    private readonly WebSocketTransport _transport;
    private readonly FileTransferRunner _runner;
    private readonly SemaphoreSlim _admissionSlot = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<(string ClientId, string OperationId), FileTransferPendingStart> _pendingStarts = [];
    private readonly Dictionary<string, FileTransferSession> _transfers = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    internal FileTransferCoordinator(
        FileManagerService files,
        HostStatusPayloadFactory status,
        PairingManager pairingManager,
        WebSocketTransport transport,
        bool relayMode,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        IFileTransferWebRtcPeerFactory? peerFactory = null)
    {
        _files = files;
        _status = status;
        _pairingManager = pairingManager;
        _transport = transport;
        _runner = new FileTransferRunner(files, pairingManager, transport, relayMode, getRelayTurnConfiguration,
            peerFactory ?? new FileTransferWebRtcPeerFactory(), Remove);
        _pairingManager.PermissionsChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
    }

    internal Task StartAsync(WebSocket socket, string clientId, System.Text.Json.JsonElement root, CancellationToken cancellationToken)
    {
        string operationId = String(root, "operationId");
        string direction = String(root, "direction");
#pragma warning disable CA2000 // PendingStart owns and disposes the linked cancellation source after its admission task ends.
        var pending = new FileTransferPendingStart(clientId, direction, socket, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token));
#pragma warning restore CA2000
        lock (_gate)
        {
            if (_pendingStarts.ContainsKey((clientId, operationId)))
            {
                pending.Dispose();
                Task duplicate = SendStartResultAsync(socket, operationId, false, "duplicate-request", "The file-transfer request is already pending.", null, null, cancellationToken);
                _ = duplicate.ContinueWith(completed => _ = completed.Exception,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return Task.CompletedTask;
            }
            _pendingStarts.Add((clientId, operationId), pending);
            pending.Task = StartCoreAsync(socket, clientId, root, pending);
        }
        _ = pending.Task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (_gate)
            {
                if (_pendingStarts.TryGetValue((clientId, operationId), out var current) && ReferenceEquals(current, pending))
                    _pendingStarts.Remove((clientId, operationId));
            }
            pending.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private async Task StartCoreAsync(WebSocket socket, string clientId, System.Text.Json.JsonElement root, FileTransferPendingStart pending)
    {
        CancellationToken cancellationToken = pending.Token;
        string operationId = String(root, "operationId");
        string direction = String(root, "direction");
        if (!HasPermissions(clientId, direction))
        {
            await SendStartResultAsync(socket, operationId, false, "permission-denied", "File transfer is disabled for this device on the PC.", null, null, cancellationToken);
            return;
        }
        string sessionId = String(root, "sessionId");
        string panel = String(root, "panel");
        string revision = String(root, "revision");
        string entryId = OptionalString(root, "entryId") ?? string.Empty;
        string fileName = OptionalString(root, "fileName") ?? string.Empty;
        long declaredSize = root.TryGetProperty("declaredSize", out var sizeValue) ? sizeValue.GetInt64() : 0;
        string startTranscript = FileTransferNegotiation.StartTranscript(clientId, _pairingManager.HostIdentity.PublicKey, operationId, direction, sessionId, panel, revision, entryId, fileName, direction == "upload" ? declaredSize : null);
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(startTranscript), String(root, "clientSignature")))
        {
            await SendStartResultAsync(socket, operationId, false, "invalid-proof", "The file-transfer request could not be authenticated.", null, null, cancellationToken);
            return;
        }
        if (!_admissionSlot.Wait(0, cancellationToken))
        {
            await SendStartResultAsync(socket, operationId, false, "busy", "Another file transfer is being prepared.", null, null, cancellationToken);
            return;
        }
        try
        {
            FileTransferSession? transfer = null;
            FileJobSnapshot? job = null;
            Task<FileJobSnapshot>? uploadCompletion = null;
            string? code = null;
            string message;
            if (direction == "download")
            {
                (bool Succeeded, string? Code, string Message, FileTransferDownloadSource? Source) source;
                source = await Task.Run(() => _files.OpenDownload(clientId, sessionId, panel, revision, entryId, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (source.Source is not null) await source.Source.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                if (!source.Succeeded || source.Source is null)
                {
                    await SendStartResultAsync(socket, operationId, false, source.Code, source.Message, null, null, cancellationToken);
                    return;
                }
                if (!_runner.TryAcquireSlot())
                {
                    await source.Source.DisposeAsync().ConfigureAwait(false);
                    await SendStartResultAsync(socket, operationId, false, "busy", "Another file transfer is already active.", null, null, cancellationToken);
                    return;
                }
                transfer = new FileTransferSession(NewId(), clientId, operationId, socket, direction, source.Source.Name, source.Source.Size)
                {
                    DownloadSource = source.Source,
                    SlotHeld = true
                };
                message = "Download ready.";
            }
            else
            {
                transfer = new FileTransferSession(NewId(), clientId, operationId, socket, direction, fileName, declaredSize);
                var capturedTransfer = transfer;
                (bool Succeeded, string? Code, string Message, FileUploadAdmission? Admission) admission;
                try
                {
                    admission = await Task.Run(() => _files.CreateUploadJob(
                        clientId,
                        sessionId,
                        panel,
                        revision,
                        fileName,
                        declaredSize,
                        (destination, committed, token) => _runner.ReceiveUploadAsync(capturedTransfer, destination, committed, token),
                        cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    transfer.Cancellation.Dispose();
                    throw;
                }
                if (!admission.Succeeded || admission.Admission is null)
                {
                    transfer.Cancellation.Dispose();
                    await SendStartResultAsync(socket, operationId, false, admission.Code, admission.Message, null, null, cancellationToken);
                    return;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    _files.ControlJob(clientId, admission.Admission.Snapshot.JobId, "cancel");
                    await admission.Admission.Completion.ConfigureAwait(false);
                    transfer.Cancellation.Dispose();
                    return;
                }
                job = admission.Admission.Snapshot;
                uploadCompletion = admission.Admission.Completion;
                transfer.JobId = job.JobId;
                message = "Upload queued.";
            }

            lock (_gate) _transfers[transfer.Id] = transfer;
            if (direction == "download") transfer.RunTask = _runner.RunDownloadAsync(transfer);
            if (!HasPermissions(clientId, direction))
            {
                CancelTransfer(transfer, "permission-revoked", "File-transfer permission was revoked on the PC.");
                if (uploadCompletion is not null) transfer.RunTask = _runner.ObserveUploadCompletionAsync(transfer, uploadCompletion, publishResult: false);
                await SendStartResultAsync(socket, operationId, false, "permission-denied", "File transfer is disabled for this device on the PC.", null, null, cancellationToken);
                return;
            }
            try
            {
                await SendStartResultAsync(socket, operationId, true, code, message, transfer.Id, job, cancellationToken);
                transfer.StartPublished.TrySetResult();
                if (uploadCompletion is not null)
                {
                    transfer.RunTask = _runner.ObserveUploadCompletionAsync(transfer, uploadCompletion);
                    await _runner.PublishQueuedAsync(transfer, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                var failure = pending.Failure;
                CancelTransfer(transfer, failure.Code ?? "connection-lost", failure.Message ?? "The control connection was lost.");
                if (uploadCompletion is not null && transfer.RunTask is null)
                    transfer.RunTask = _runner.ObserveUploadCompletionAsync(transfer, uploadCompletion, publishResult: false);
                throw;
            }
        }
        finally { _admissionSlot.Release(); }
    }

    internal async Task AnswerAsync(WebSocket socket, string clientId, string operationId, string transferId, string answerSdp, string clientSignature, CancellationToken cancellationToken)
    {
        FileTransferSession? transfer = Find(clientId, transferId);
        if (transfer is null)
        {
            await SendActionResultAsync(socket, "file.transfer.answer.result", operationId, false, "offer-expired", "The file-transfer offer expired.", cancellationToken);
            return;
        }
        IFileTransferWebRtcPeer? peer;
        string? offerHash;
        lock (transfer.Gate)
        {
            peer = transfer.Peer;
            offerHash = transfer.OfferHash;
        }
        if (peer is null || offerHash is null)
        {
            await SendActionResultAsync(socket, "file.transfer.answer.result", operationId, false, "offer-expired", "The file-transfer offer expired.", cancellationToken);
            return;
        }
        string answerHash = FileTransferNegotiation.HashSdp(answerSdp);
        string transcript = FileTransferNegotiation.AnswerTranscript(clientId, _pairingManager.HostIdentity.PublicKey, transfer.OperationId, transfer.Id, transfer.Direction, transfer.FileName, transfer.DeclaredSize, offerHash, answerHash);
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), clientSignature))
        {
            CancelTransfer(transfer, "invalid-proof", "The file-transfer answer could not be authenticated.");
            await SendActionResultAsync(socket, "file.transfer.answer.result", operationId, false, "invalid-proof", "The file-transfer answer could not be authenticated.", cancellationToken);
            return;
        }
        try
        {
            peer.ApplyAnswer(answerSdp);
            transfer.AnswerApplied.TrySetResult();
            await SendActionResultAsync(socket, "file.transfer.answer.result", operationId, true, null, "File transfer connected.", cancellationToken);
        }
        catch (Exception ex) when (ex is FileTransferWebRtcException or ObjectDisposedException)
        {
            CancelTransfer(transfer, "invalid-answer", "The PC rejected the file-transfer answer.");
            await SendActionResultAsync(socket, "file.transfer.answer.result", operationId, false, "invalid-answer", "The PC rejected the file-transfer answer.", cancellationToken);
        }
    }

    internal async Task CancelAsync(WebSocket socket, string clientId, string operationId, string? transferId, string? requestId, CancellationToken cancellationToken)
    {
        FileTransferPendingStart? pending = null;
        FileTransferSession? transfer;
        lock (_gate)
        {
            if (requestId is not null) _pendingStarts.TryGetValue((clientId, requestId), out pending);
            transfer = transferId is not null
                ? FindLocked(clientId, transferId)
                : requestId is not null
                    ? _transfers.Values.FirstOrDefault(candidate => candidate.ClientId == clientId && candidate.OperationId == requestId)
                    : null;
        }
        if (pending is null && transfer is null)
        {
            await SendActionResultAsync(socket, "file.transfer.cancel.result", operationId, false, "transfer-unavailable", "The file transfer is unavailable.", cancellationToken);
            return;
        }
        pending?.TryCancel("canceled", "File transfer canceled.");
        if (transfer is not null) CancelTransfer(transfer, "canceled", "File transfer canceled.");
        await SendActionResultAsync(socket, "file.transfer.cancel.result", operationId, true, null, "File transfer canceled.", cancellationToken);
    }

    internal void ClientDisconnected(string clientId, WebSocket socket)
    {
        FileTransferPendingStart[] pending;
        lock (_gate) pending = [.. _pendingStarts.Where(item => item.Key.ClientId == clientId && ReferenceEquals(item.Value.Socket, socket)).Select(item => item.Value)];
        foreach (FileTransferPendingStart item in pending) item.TryCancel("connection-lost", "The controller connection closed.");
        foreach (FileTransferSession transfer in Snapshot().Where(transfer => transfer.ClientId == clientId && ReferenceEquals(transfer.Socket, socket)))
            CancelTransfer(transfer, "connection-lost", "The controller connection closed.");
    }

    public async ValueTask DisposeAsync()
    {
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        FileTransferPendingStart[] pendingOwners;
        lock (_gate) pendingOwners = [.. _pendingStarts.Values];
        foreach (FileTransferPendingStart pending in pendingOwners) pending.TryCancel("host-stopped", "The PC stopped the file transfer.");
        await _lifetime.CancelAsync().ConfigureAwait(false);
        Task[] pendingStarts;
        lock (_gate) pendingStarts = [.. _pendingStarts.Values.Select(pending => pending.Task)];
        Task pendingCompletion = Task.WhenAll(pendingStarts);
        try { await pendingCompletion.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        FileTransferSession[] transfers = Snapshot();
        await _runner.ShutdownAsync(transfers).ConfigureAwait(false);
        if (pendingCompletion.IsCompleted) await DisposeOwnedResourcesAsync().ConfigureAwait(false);
        else _ = DisposeOwnedResourcesWhenPendingCompletesAsync(pendingCompletion);
    }

    private void CancelTransfer(FileTransferSession transfer, string code, string message)
        => _runner.Cancel(transfer, code, message);

    private FileTransferSession? Find(string clientId, string transferId)
    {
        lock (_gate) return FindLocked(clientId, transferId);
    }

    private FileTransferSession? FindLocked(string clientId, string transferId) =>
        _transfers.TryGetValue(transferId, out var transfer) && transfer.ClientId == clientId ? transfer : null;

    private FileTransferSession[] Snapshot()
    {
        lock (_gate) return [.. _transfers.Values];
    }

    private void Remove(FileTransferSession transfer)
    {
        lock (_gate) _transfers.Remove(transfer.Id);
    }

    private void OnPermissionsChanged(object? sender, EventArgs eventArgs)
    {
        FileTransferPendingStart[] pending;
        lock (_gate) pending = [.. _pendingStarts.Values];
        foreach (FileTransferPendingStart start in pending)
        {
            if (!HasPermissions(start.ClientId, start.Direction))
                start.TryCancel("permission-revoked", "File-transfer permission was revoked on the PC.");
        }
        foreach (FileTransferSession transfer in Snapshot())
        {
            if (!_status.CanTransferFiles(transfer.ClientId) || !_status.CanBrowseFiles(transfer.ClientId) || transfer.Direction == "upload" && !_status.CanChangeFiles(transfer.ClientId))
                CancelTransfer(transfer, "permission-revoked", "File-transfer permission was revoked on the PC.");
        }
    }

    private bool HasPermissions(string clientId, string direction) =>
        _status.CanTransferFiles(clientId) && _status.CanBrowseFiles(clientId) &&
        (direction != "upload" || _status.CanChangeFiles(clientId));

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static string String(System.Text.Json.JsonElement root, string name) => root.GetProperty(name).GetString()!;
    private static string? OptionalString(System.Text.Json.JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private Task SendStartResultAsync(WebSocket socket, string operationId, bool succeeded, string? code, string message, string? transferId, FileJobSnapshot? job, CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new { type = "file.transfer.start.result", operationId, succeeded, code, message, transferId, job }, cancellationToken);
    private Task SendActionResultAsync(WebSocket socket, string type, string operationId, bool succeeded, string? code, string message, CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new { type, operationId, succeeded, code, message }, cancellationToken);

    private async Task DisposeOwnedResourcesWhenPendingCompletesAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally { await DisposeOwnedResourcesAsync().ConfigureAwait(false); }
    }

    private async ValueTask DisposeOwnedResourcesAsync()
    {
        await _runner.DisposeAsync().ConfigureAwait(false);
        _admissionSlot.Dispose();
        _lifetime.Dispose();
    }
}
