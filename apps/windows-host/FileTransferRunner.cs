using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class FileTransferRunner : IAsyncDisposable
{
    private readonly FileManagerService files;
    private readonly PairingManager pairingManager;
    private readonly WebSocketTransport transport;
    private readonly bool relayMode;
    private readonly Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration;
    private readonly IFileTransferWebRtcPeerFactory peerFactory;
    private readonly Action<FileTransferSession> remove;
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;

    internal FileTransferRunner(
        FileManagerService files,
        PairingManager pairingManager,
        WebSocketTransport transport,
        bool relayMode,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        IFileTransferWebRtcPeerFactory peerFactory,
        Action<FileTransferSession> remove)
    {
        this.files = files;
        this.pairingManager = pairingManager;
        this.transport = transport;
        this.relayMode = relayMode;
        this.getRelayTurnConfiguration = getRelayTurnConfiguration;
        this.peerFactory = peerFactory;
        this.remove = remove;
        _lifetimeToken = _lifetime.Token;
    }

    internal bool TryAcquireSlot() => _slot.Wait(0);
    internal void ReleaseSlot() => _slot.Release();
    internal Task PublishQueuedAsync(FileTransferSession transfer, CancellationToken cancellationToken) =>
        SendStatusAsync(transfer, "queued", 0, true, cancellationToken);

    internal async Task RunDownloadAsync(FileTransferSession transfer)
    {
        try
        {
            await transfer.StartPublished.Task.WaitAsync(transfer.CancellationToken).ConfigureAwait(false);
            RelayTurnConfiguration? relay = await NegotiateAsync(transfer, transfer.CancellationToken).ConfigureAwait(false);
            await SendStatusAsync(transfer, "transferring", 0, true, transfer.CancellationToken).ConfigureAwait(false);
            using var inactivity = new CancellationTokenSource();
            inactivity.CancelAfter(FileTransferProtocol.InactivityTimeout);
            using var relayExpiry = CreateRelayExpiry(relay);
            using var stalled = inactivity.Token.Register(() => SetFailureIfUnset(transfer, "stalled", "File transfer stopped after 60 seconds without progress."));
            using var expired = relayExpiry?.Token.Register(() => SetFailureIfUnset(transfer, "relay-expired", "The Relay file-transfer session expired."));
            using var active = CancellationTokenSource.CreateLinkedTokenSource(transfer.CancellationToken, _lifetimeToken, inactivity.Token, relayExpiry?.Token ?? CancellationToken.None);
            await FileTransferDataPump.SendAsync(
                transfer.DownloadSource ?? throw new IOException("The download source was unavailable."), transfer.DeclaredSize, transfer.Peer!,
                () => inactivity.CancelAfter(FileTransferProtocol.InactivityTimeout),
                (completed, force, token) => SendStatusAsync(transfer, "transferring", completed, force, token), active.Token).ConfigureAwait(false);
            await SendResultAsync(transfer, true, null, "File ready to save.", active.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SendFailureAsync(transfer, transfer.FailureCode ?? "canceled", transfer.FailureMessage ?? "File transfer canceled.").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or FileTransferWebRtcException or ChannelClosedException or ObjectDisposedException or InvalidOperationException)
        {
            await SendFailureAsync(transfer, transfer.FailureCode ?? "transfer-failed", transfer.FailureMessage ?? "The file transfer stopped.").ConfigureAwait(false);
        }
        finally { await RemoveAndDisposeAsync(transfer).ConfigureAwait(false); }
    }

    internal async Task ReceiveUploadAsync(FileTransferSession transfer, Stream destination, Action<long> committed, CancellationToken jobCancellation)
    {
        if (Volatile.Read(ref transfer.DisposeStarted) != 0 || _lifetimeToken.IsCancellationRequested)
            throw new OperationCanceledException("File transfer canceled.", null, jobCancellation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(jobCancellation, transfer.CancellationToken, _lifetimeToken);
        Exception? failure = null;
        try
        {
            await transfer.StartPublished.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            await _slot.WaitAsync(linked.Token).ConfigureAwait(false);
            transfer.SlotHeld = true;
            RelayTurnConfiguration? relay = await NegotiateAsync(transfer, linked.Token).ConfigureAwait(false);
            await SendStatusAsync(transfer, "transferring", 0, true, linked.Token).ConfigureAwait(false);
            using var inactivity = new CancellationTokenSource();
            inactivity.CancelAfter(FileTransferProtocol.InactivityTimeout);
            using var relayExpiry = CreateRelayExpiry(relay);
            using var stalled = inactivity.Token.Register(() => SetFailureIfUnset(transfer, "stalled", "File transfer stopped after 60 seconds without progress."));
            using var expired = relayExpiry?.Token.Register(() => SetFailureIfUnset(transfer, "relay-expired", "The Relay file-transfer session expired."));
            using var active = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, inactivity.Token, relayExpiry?.Token ?? CancellationToken.None);
            await FileTransferDataPump.ReceiveAsync(
                destination, transfer.DeclaredSize, transfer.Peer!, committed,
                () => inactivity.CancelAfter(FileTransferProtocol.InactivityTimeout),
                (completed, force, token) => SendStatusAsync(transfer, "transferring", completed, force, token), active.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            SetFailureIfUnset(transfer, "canceled", "File transfer canceled.");
            failure = new OperationCanceledException(exception.Message, exception, jobCancellation);
        }
        catch (Exception ex) when (ex is WebSocketException or FileTransferWebRtcException or ChannelClosedException or ObjectDisposedException or InvalidOperationException)
        {
            SetFailureIfUnset(transfer, "transfer-failed", "The file transfer stopped.");
            failure = new IOException("The upload transport stopped.", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ex;
        }
        try { await DisposeTransportAsync(transfer).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var cleanupFailure = new IOException("The upload transport cleanup failed.", ex);
            failure = failure is null
                ? cleanupFailure
                : new IOException("The upload failed and its transport cleanup also failed.", new AggregateException(failure, cleanupFailure));
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal async Task ObserveUploadCompletionAsync(FileTransferSession transfer, Task<FileJobSnapshot> completion, bool publishResult = true)
    {
        try
        {
            FileJobSnapshot job = await completion.ConfigureAwait(false);
            if (publishResult)
            {
                if (job.State == "completed") await SendResultAsync(transfer, true, null, "File copied to the PC.", CancellationToken.None, job.CurrentName).ConfigureAwait(false);
                else await SendFailureAsync(transfer, transfer.FailureCode ?? (job.State == "canceled" ? "canceled" : "upload-failed"), transfer.FailureMessage ?? job.Message ?? "The upload failed.").ConfigureAwait(false);
            }
        }
        finally { await RemoveAndDisposeAsync(transfer).ConfigureAwait(false); }
    }

    internal void Cancel(FileTransferSession transfer, string code, string message)
    {
        if (!transfer.TryCancel(code, message)) return;
        if (transfer.JobId is { } jobId) files.ControlJob(transfer.ClientId, jobId, "cancel");
    }

    internal async Task ShutdownAsync(FileTransferSession[] transfers)
    {
        foreach (FileTransferSession transfer in transfers) Cancel(transfer, "host-stopped", "The PC stopped the file transfer.");
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(transfers.Select(transfer => transfer.RunTask).OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally
        {
            foreach (FileTransferSession transfer in transfers)
            {
                try { await DisposeTransferAsync(transfer).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _slot.Dispose();
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task<RelayTurnConfiguration?> NegotiateAsync(FileTransferSession transfer, CancellationToken cancellationToken) =>
        FileTransferNegotiation.RunAsync(
            transfer, relayMode, getRelayTurnConfiguration, peerFactory, pairingManager, transport,
            token => SendStatusAsync(transfer, "connecting", 0, true, token), cancellationToken);

    private async Task SendStatusAsync(FileTransferSession transfer, string state, long completed, bool force, CancellationToken cancellationToken)
    {
        long now = TimeProvider.System.GetTimestamp();
        if (!force && transfer.LastStatusTimestamp != 0 && TimeProvider.System.GetElapsedTime(transfer.LastStatusTimestamp, now) < TimeSpan.FromMilliseconds(200)) return;
        transfer.LastStatusTimestamp = now;
        await transport.SendAsync(transfer.Socket, new { type = "file.transfer.status", transferId = transfer.Id, direction = transfer.Direction, state, bytesCompleted = completed, bytesTotal = transfer.DeclaredSize }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendResultAsync(FileTransferSession transfer, bool succeeded, string? code, string message, CancellationToken cancellationToken, string? fileName = null)
    {
        try { await transport.SendAsync(transfer.Socket, new { type = "file.transfer.result", transferId = transfer.Id, direction = transfer.Direction, succeeded, code, message, fileName = fileName ?? transfer.FileName, declaredSize = transfer.DeclaredSize, jobId = transfer.JobId }, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }

    private Task SendFailureAsync(FileTransferSession transfer, string code, string message)
    {
        SetFailureIfUnset(transfer, code, message);
        return SendResultAsync(transfer, false, transfer.FailureCode, transfer.FailureMessage!, CancellationToken.None);
    }

    private async Task DisposeTransportAsync(FileTransferSession transfer)
    {
        IFileTransferWebRtcPeer? peer;
        bool releaseSlot;
        lock (transfer.Gate)
        {
            peer = transfer.Peer;
            transfer.Peer = null;
            releaseSlot = transfer.SlotHeld;
            transfer.SlotHeld = false;
        }
        try
        {
            if (peer is not null) await peer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (releaseSlot) _slot.Release();
        }
    }

    private async Task RemoveAndDisposeAsync(FileTransferSession transfer)
    {
        remove(transfer);
        await DisposeTransferAsync(transfer).ConfigureAwait(false);
    }

    private async Task DisposeTransferAsync(FileTransferSession transfer)
    {
        if (Interlocked.Exchange(ref transfer.DisposeStarted, 1) != 0) return;
        List<Exception>? failures = null;
        try { await DisposeTransportAsync(transfer).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { (failures ??= []).Add(ex); }
        try
        {
            if (transfer.DownloadSource is not null) await transfer.DownloadSource.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { (failures ??= []).Add(ex); }
        finally { transfer.DownloadSource = null; }
        try { transfer.Cancellation.Dispose(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { (failures ??= []).Add(ex); }
        if (failures is { Count: > 0 }) throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
    }

    private static CancellationTokenSource? CreateRelayExpiry(RelayTurnConfiguration? relay)
    {
        if (relay is null) return null;
        var result = new CancellationTokenSource();
        TimeSpan remaining = relay.ExpiresAt - DateTimeOffset.UtcNow;
        result.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        return result;
    }

    private static void SetFailureIfUnset(FileTransferSession transfer, string code, string message)
    {
        lock (transfer.Gate) { transfer.FailureCode ??= code; transfer.FailureMessage ??= message; }
    }
}
