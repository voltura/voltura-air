using System.Net.WebSockets;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class TerminalCoordinator : IAsyncDisposable
{
    private readonly PairingManager _pairingManager;
    private readonly HostStatusPayloadFactory _status;
    private readonly WebSocketTransport _transport;
    private readonly ITerminalProcessFactory _processFactory;
    private readonly ITerminalWebRtcPeerFactory _peerFactory;
    private readonly bool _relayMode;
    private readonly Func<CancellationToken, Task<RelayTurnConfiguration?>> _getRelayTurnConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly HashSet<(string ClientId, string OperationId)> _operations = [];
    private readonly Queue<(string ClientId, string OperationId)> _operationOrder = [];
    private TerminalSession? _active;
    private int _disposed;

    internal TerminalCoordinator(
        PairingManager pairingManager,
        HostStatusPayloadFactory status,
        WebSocketTransport transport,
        bool relayMode,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        ITerminalProcessFactory processFactory,
        ITerminalWebRtcPeerFactory peerFactory,
        TimeProvider? timeProvider = null)
    {
        _pairingManager = pairingManager;
        _status = status;
        _transport = transport;
        _relayMode = relayMode;
        _getRelayTurnConfiguration = getRelayTurnConfiguration;
        _processFactory = processFactory;
        _peerFactory = peerFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pairingManager.PermissionsChanged += OnPermissionsChanged;
        _pairingManager.PairingRevoked += OnPairingRevoked;
        AppPermissionSettings.Changed += OnPermissionsChanged;
    }

    internal event EventHandler<TerminalActivityChangedEventArgs>? ActivityChanged;

    internal void QueueStart(WebSocket socket, string clientId, JsonElement root, CancellationToken cancellationToken)
    {
        JsonElement ownedRoot = root.Clone();
        QueueCommand(socket, clientId, () => StartAsync(socket, clientId, ownedRoot, cancellationToken), cancellationToken);
    }

    internal void QueueAttach(WebSocket socket, string clientId, JsonElement root, CancellationToken cancellationToken)
    {
        JsonElement ownedRoot = root.Clone();
        QueueCommand(socket, clientId, () => AttachAsync(socket, clientId, ownedRoot, cancellationToken), cancellationToken);
    }

    internal void QueueAnswer(WebSocket socket, string clientId, JsonElement root, CancellationToken cancellationToken)
    {
        JsonElement ownedRoot = root.Clone();
        QueueCommand(socket, clientId, () => AnswerAsync(socket, clientId, ownedRoot, cancellationToken), cancellationToken);
    }

    internal void QueueStop(WebSocket socket, string clientId, string operationId, string terminalId, CancellationToken cancellationToken) =>
        QueueCommand(socket, clientId, () => StopAsync(socket, clientId, operationId, terminalId, cancellationToken), cancellationToken);

    private void QueueCommand(WebSocket socket, string clientId, Func<Task> command, CancellationToken cancellationToken) =>
        _ = ObserveQueuedCommandAsync(socket, clientId, command, cancellationToken);

    private async Task ObserveQueuedCommandAsync(WebSocket socket, string clientId, Func<Task> command, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested) return;
        try
        {
            await command().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
        {
            ClientDisconnected(clientId, socket);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await StopFromHostAsync().ConfigureAwait(false);
        }
    }

    internal TerminalCapabilityState GetCapability(string clientId)
    {
        lock (_gate)
        {
            return new TerminalCapabilityState(
                _active is not null,
                _active?.ClientId == clientId,
                _active?.ClientId == clientId ? _active.Id : null,
                _active is null ? null : _pairingManager.GetDeviceName(_active.ClientId) ?? "paired device");
        }
    }

    internal async Task StartAsync(WebSocket socket, string clientId, JsonElement root, CancellationToken cancellationToken)
    {
        string operationId = root.GetProperty("operationId").GetString()!;
        int columns = root.GetProperty("columns").GetInt32();
        int rows = root.GetProperty("rows").GetInt32();
        string transcript = TerminalNegotiation.StartTranscript(clientId, _pairingManager.HostIdentity.PublicKey, operationId, columns, rows);
        if (!_status.CanUseTerminal(clientId))
        {
            await SendResultAsync(socket, "terminal.start.result", operationId, false, "permission-denied", "Terminal is blocked for this device.", null, cancellationToken);
            return;
        }
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), root.GetProperty("clientSignature").GetString()!))
        {
            await SendResultAsync(socket, "terminal.start.result", operationId, false, "invalid-proof", "The Terminal request could not be authenticated.", null, cancellationToken);
            return;
        }

        TerminalSession? replaced = null;
        TerminalSession? session = null;
        bool busy = false;
        try
        {
            lock (_gate)
            {
                bool replaceableDetached = false;
                if (_active is { ClientId: var activeClientId } active && activeClientId == clientId)
                {
                    lock (active.Gate)
                    {
                        replaceableDetached = active.Peer is null &&
                            active.ReconnectLifetime is not null &&
                            !active.Attached;
                    }
                }
                if (!RememberOperationLocked(clientId, operationId) ||
                    _active is not null && !replaceableDetached)
                {
                    busy = true;
                }
                else
                {
                    replaced = _active;
                    ITerminalProcess process = _processFactory.Start((ushort)columns, (ushort)rows);
                    session = new TerminalSession(NewId(), clientId, process) { ControlSocket = socket };
                    _active = session;
                }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            await SendResultAsync(socket, "terminal.start.result", operationId, false, "process-start-failed", "Windows PowerShell could not be started.", null, cancellationToken);
            return;
        }
        if (busy || session is null)
        {
            await SendResultAsync(socket, "terminal.start.result", operationId, false, "busy", "A Terminal session is already active.", null, cancellationToken);
            return;
        }
        if (replaced is not null)
        {
            await replaced.DisposeAsync().ConfigureAwait(false);
            RaiseActivity(replaced, active: false, "replaced");
        }
        session.OutputTask = ReadOutputAsync(session);
        session.InputTask = WriteInputAsync(session);
        _ = ObserveWorkerAsync(session, session.OutputTask);
        _ = ObserveWorkerAsync(session, session.InputTask);
        _ = ObserveExitAsync(session);
        RaiseActivity(session, active: true, "started");
        await SendResultAsync(socket, "terminal.start.result", operationId, true, null, "Terminal started.", session.Id, cancellationToken);
        await SendStatusAsync(socket, session, "connecting", cancellationToken);
        try
        {
            await session.Negotiation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BeginOfferAsync(session, socket, operationId, columns, rows, 0, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                session.Negotiation.Release();
            }
        }
        catch (Exception exception) when (exception is TerminalWebRtcException or OperationCanceledException or RelayQuotaReachedException)
        {
            await EndAsync(session, "negotiation-failed").ConfigureAwait(false);
        }
    }

    internal async Task AttachAsync(WebSocket socket, string clientId, JsonElement root, CancellationToken cancellationToken)
    {
        string operationId = root.GetProperty("operationId").GetString()!;
        string terminalId = root.GetProperty("terminalId").GetString()!;
        long acknowledgedOffset = root.GetProperty("acknowledgedOffset").GetInt64();
        int columns = root.GetProperty("columns").GetInt32();
        int rows = root.GetProperty("rows").GetInt32();
        string transcript = TerminalNegotiation.AttachTranscript(clientId, _pairingManager.HostIdentity.PublicKey, operationId, terminalId, acknowledgedOffset, columns, rows);
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), root.GetProperty("clientSignature").GetString()!))
        {
            await SendResultAsync(socket, "terminal.attach.result", operationId, false, "invalid-proof", "The attach request could not be authenticated.", terminalId, cancellationToken);
            return;
        }
        TerminalSession? session;
        lock (_gate)
        {
            session = _active is { } active && active.Id == terminalId && active.ClientId == clientId
                ? active
                : null;
            if (session is not null && !RememberOperationLocked(clientId, operationId)) session = null;
        }
        if (session is not null)
        {
            bool validOffset;
            bool advancedOffset;
            lock (session.Gate)
            {
                advancedOffset = acknowledgedOffset > session.AcknowledgedOutputOffset;
                validOffset = acknowledgedOffset >= session.AcknowledgedOutputOffset &&
                    acknowledgedOffset <= session.NextOutputOffset &&
                    (acknowledgedOffset == session.AcknowledgedOutputOffset ||
                        session.Output.Any(chunk => chunk.EndOffset == acknowledgedOffset));
                if (validOffset)
                {
                    session.AcknowledgedOutputOffset = acknowledgedOffset;
                    while (session.Output.First is { } first && first.Value.EndOffset <= acknowledgedOffset)
                        session.Output.RemoveFirst();
                }
            }
            if (!validOffset) session = null;
            else if (advancedOffset) session.OutputSpace.Release();
        }
        if (session is null || !_status.CanUseTerminal(clientId))
        {
            await SendResultAsync(socket, "terminal.attach.result", operationId, false, "terminal-unavailable", "The Terminal session cannot be resumed.", terminalId, cancellationToken);
            return;
        }
        using var negotiationWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
        await session.Negotiation.WaitAsync(negotiationWait.Token).ConfigureAwait(false);
        TerminalSession negotiatingSession = session;
        try
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_active, negotiatingSession)) session = null;
            }
            if (session is null || !_status.CanUseTerminal(clientId))
            {
                await SendResultAsync(socket, "terminal.attach.result", operationId, false, "terminal-unavailable", "The Terminal session cannot be resumed.", terminalId, cancellationToken);
                return;
            }
            ITerminalWebRtcPeer? previousPeer;
            lock (session.Gate) previousPeer = session.Peer;
            if (previousPeer is not null) await DetachAsync(session, previousPeer, "transport-renewal").ConfigureAwait(false);
            CancellationTokenSource? reconnectLifetime;
            lock (session.Gate)
            {
                reconnectLifetime = session.ReconnectLifetime;
                session.ReconnectLifetime = null;
            }
            if (reconnectLifetime is not null) await reconnectLifetime.CancelAsync().ConfigureAwait(false);
            reconnectLifetime?.Dispose();
            session.Process.Resize((ushort)columns, (ushort)rows);
            session.ControlSocket = socket;
            await SendResultAsync(socket, "terminal.attach.result", operationId, true, null, "Terminal resume accepted.", terminalId, cancellationToken);
            await SendStatusAsync(socket, session, "connecting", cancellationToken);
            await BeginOfferAsync(session, socket, operationId, columns, rows, acknowledgedOffset, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TerminalWebRtcException or OperationCanceledException or RelayQuotaReachedException)
        {
            if (session is not null) await EndAsync(session, "negotiation-failed").ConfigureAwait(false);
        }
        finally
        {
            negotiatingSession.Negotiation.Release();
        }
    }

    internal async Task AnswerAsync(WebSocket socket, string clientId, JsonElement root, CancellationToken cancellationToken)
    {
        string operationId = root.GetProperty("operationId").GetString()!;
        string requestedOfferOperationId = root.GetProperty("offerOperationId").GetString()!;
        string terminalId = root.GetProperty("terminalId").GetString()!;
        TerminalSession? session;
        lock (_gate) session = _active is { } active && active.Id == terminalId && active.ClientId == clientId ? active : null;
        ITerminalWebRtcPeer? peer;
        string? offerHash;
        string? offerOperationId;
        lock (session?.Gate ?? _gate)
        {
            peer = session?.Peer;
            offerHash = session?.OfferHash;
            offerOperationId = session?.OfferOperationId;
        }
        if (session is null || peer is null || offerHash is null || offerOperationId is null ||
            offerOperationId != requestedOfferOperationId)
        {
            await SendActionResultAsync(socket, "terminal.answer.result", operationId, false, "offer-expired", "The Terminal offer expired.", cancellationToken);
            return;
        }
        string answerSdp = root.GetProperty("answerSdp").GetString()!;
        string transcript = TerminalNegotiation.AnswerTranscript(clientId, _pairingManager.HostIdentity.PublicKey, offerOperationId, operationId, terminalId, offerHash, TerminalNegotiation.HashSdp(answerSdp));
        if (!_pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), root.GetProperty("clientSignature").GetString()!))
        {
            await SendActionResultAsync(socket, "terminal.answer.result", operationId, false, "invalid-proof", "The Terminal answer could not be authenticated.", cancellationToken);
            return;
        }
        bool replayed;
        lock (_gate) replayed = !RememberOperationLocked(clientId, operationId);
        if (replayed)
        {
            await SendActionResultAsync(socket, "terminal.answer.result", operationId, false, "replayed-operation", "The Terminal answer was already used.", cancellationToken);
            return;
        }
        bool currentOffer;
        CancellationTokenSource? offerLifetime = null;
        lock (session.Gate)
        {
            currentOffer = ReferenceEquals(session.Peer, peer) &&
                session.OfferOperationId == offerOperationId &&
                session.OfferHash == offerHash &&
                !session.AnswerStarted;
            if (currentOffer)
            {
                session.AnswerStarted = true;
                offerLifetime = session.OfferLifetime;
                session.OfferLifetime = null;
            }
        }
        if (!currentOffer)
        {
            await SendActionResultAsync(socket, "terminal.answer.result", operationId, false, "offer-expired", "The Terminal offer expired.", cancellationToken);
            return;
        }
        if (offerLifetime is not null) await offerLifetime.CancelAsync().ConfigureAwait(false);
        offerLifetime?.Dispose();
        try
        {
            peer.ApplyAnswer(answerSdp);
            await peer.Opened.WaitAsync(TerminalProtocol.SignalingLifetime, cancellationToken).ConfigureAwait(false);
            lock (session.Gate) session.Attached = true;
            _ = RunPeerAsync(session, peer, session.PeerLifetime!.Token);
            await SendActionResultAsync(socket, "terminal.answer.result", operationId, true, null, "Terminal connected.", cancellationToken);
            await SendStatusAsync(socket, session, "active", cancellationToken);
        }
        catch (Exception exception) when (exception is TerminalWebRtcException or TimeoutException or OperationCanceledException)
        {
            await DetachAsync(session, peer, "connection-lost").ConfigureAwait(false);
            await SendActionResultAsync(socket, "terminal.answer.result", operationId, false, "invalid-answer", "The PC rejected the Terminal answer.", cancellationToken);
        }
    }

    internal async Task StopAsync(WebSocket socket, string clientId, string operationId, string terminalId, CancellationToken cancellationToken)
    {
        TerminalSession? session;
        lock (_gate) session = _active is { } active && active.Id == terminalId && active.ClientId == clientId ? active : null;
        if (session is null)
        {
            await SendActionResultAsync(socket, "terminal.stop.result", operationId, false, "terminal-unavailable", "The Terminal session is unavailable.", cancellationToken);
            return;
        }
        await EndAsync(session, "stopped").ConfigureAwait(false);
        await SendActionResultAsync(socket, "terminal.stop.result", operationId, true, null, "Terminal stopped.", cancellationToken);
    }

    internal void ClientDisconnected(string clientId, WebSocket socket)
    {
        TerminalSession? session;
        ITerminalWebRtcPeer? peer;
        lock (_gate) session = _active is { } active && active.ClientId == clientId && ReferenceEquals(active.ControlSocket, socket) ? active : null;
        lock (session?.Gate ?? _gate) peer = session?.Peer;
        if (session is not null) _ = DetachAsync(session, peer, "connection-lost");
    }

    internal Task StopFromHostAsync() => GetActive() is { } session ? EndAsync(session, "host-stopped") : Task.CompletedTask;

    private async Task BeginOfferAsync(TerminalSession session, WebSocket socket, string operationId, int columns, int rows, long acknowledgedOffset, CancellationToken cancellationToken)
    {
        using var signaling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
        signaling.CancelAfter(TerminalProtocol.SignalingLifetime);
        RelayTurnConfiguration? relay = _relayMode ? await _getRelayTurnConfiguration(signaling.Token).ConfigureAwait(false) : null;
        if (_relayMode && relay is null) throw new TerminalWebRtcException("Relay credentials were unavailable.");
        var peer = _peerFactory.Create(relay is null ? null : new FileTransferPeerConfiguration(relay.HostIceServerUris, RelayOnly: true));
        var peerLifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token);
        var offerLifetime = CancellationTokenSource.CreateLinkedTokenSource(peerLifetime.Token);
        lock (session.Gate)
        {
            session.Peer = peer;
            session.PeerLifetime?.Dispose();
            session.PeerLifetime = peerLifetime;
            session.OfferLifetime?.Dispose();
            session.OfferLifetime = offerLifetime;
            session.OfferOperationId = operationId;
            session.OfferColumns = columns;
            session.OfferRows = rows;
            session.OfferAcknowledgedOffset = acknowledgedOffset;
            session.SentOutputOffset = acknowledgedOffset;
            session.AnswerStarted = false;
        }
        string offerSdp = await peer.CreateOfferAsync(signaling.Token).ConfigureAwait(false);
        string offerHash = TerminalNegotiation.HashSdp(offerSdp);
        lock (session.Gate) session.OfferHash = offerHash;
        string transcript = TerminalNegotiation.OfferTranscript(session.ClientId, _pairingManager.HostIdentity.PublicKey, operationId, session.Id, columns, rows, acknowledgedOffset, offerHash);
        await _transport.SendAsync(socket, new
        {
            type = "terminal.offer",
            operationId,
            terminalId = session.Id,
            columns,
            rows,
            acknowledgedOffset,
            offerSdp,
            hostSignature = _pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(transcript)),
            iceServers = relay?.IceServers,
            turnExpiresAt = relay?.ExpiresAt
        }, signaling.Token).ConfigureAwait(false);
        _ = ObserveOfferDeadlineAsync(session, peer, offerLifetime);
    }

    private async Task ObserveOfferDeadlineAsync(
        TerminalSession session,
        ITerminalWebRtcPeer peer,
        CancellationTokenSource offerLifetime)
    {
        try
        {
            await Task.Delay(TerminalProtocol.SignalingLifetime, _timeProvider, offerLifetime.Token).ConfigureAwait(false);
            bool unanswered;
            lock (session.Gate)
            {
                unanswered = ReferenceEquals(session.Peer, peer) &&
                    ReferenceEquals(session.OfferLifetime, offerLifetime) &&
                    !session.AnswerStarted;
            }
            if (unanswered) await EndAsync(session, "negotiation-timeout").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (offerLifetime.IsCancellationRequested) { }
    }

    private async Task RunPeerAsync(TerminalSession session, ITerminalWebRtcPeer peer, CancellationToken cancellationToken)
    {
        Task sender = SendOutputAsync(session, peer, cancellationToken);
        Task receiver = ReceiveRecordsAsync(session, peer, cancellationToken);
        await Task.WhenAny(sender, receiver, peer.Closed).ConfigureAwait(false);
        await DetachAsync(session, peer, "connection-lost").ConfigureAwait(false);
    }

    private async Task ReadOutputAsync(TerminalSession session)
    {
        try
        {
            while (!session.Lifetime.IsCancellationRequested)
            {
                int available;
                lock (session.Gate) available = TerminalProtocol.MaximumUnacknowledgedOutputBytes - checked((int)(session.NextOutputOffset - session.AcknowledgedOutputOffset));
                if (available <= 0) { await session.OutputSpace.WaitAsync(session.Lifetime.Token).ConfigureAwait(false); continue; }
                var bytes = new byte[Math.Min(TerminalProtocol.MaximumPayloadBytes, available)];
                int read = await session.Process.Output.ReadAsync(bytes, session.Lifetime.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    Task<int> exitCode = session.Process.ExitCode;
                    if (!exitCode.IsCompleted)
                    {
                        _ = await Task.WhenAny(
                            exitCode,
                            Task.Delay(TerminalProtocol.OutputEofExitGrace, _timeProvider, session.Lifetime.Token)).ConfigureAwait(false);
                    }
                    if (exitCode.IsCompleted) return;
                    throw new IOException("The terminal output pipe closed.");
                }
                if (read != bytes.Length) Array.Resize(ref bytes, read);
                lock (session.Gate)
                {
                    session.Output.AddLast(new TerminalOutputChunk(session.NextOutputOffset, bytes));
                    session.NextOutputOffset += bytes.Length;
                }
                session.OutputChanged.Release();
            }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested) { }
    }

    private static async Task WriteInputAsync(TerminalSession session)
    {
        try
        {
            await foreach (byte[] bytes in session.Input.Reader.ReadAllAsync(session.Lifetime.Token).ConfigureAwait(false))
            {
                await session.Process.Input.WriteAsync(bytes, session.Lifetime.Token).ConfigureAwait(false);
                await session.Process.Input.FlushAsync(session.Lifetime.Token).ConfigureAwait(false);
                lock (session.Gate) session.QueuedInputBytes -= bytes.Length;
            }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested) { }
    }

    private static async Task SendOutputAsync(TerminalSession session, ITerminalWebRtcPeer peer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TerminalOutputChunk? chunk;
            lock (session.Gate) chunk = session.Output.FirstOrDefault(item => item.EndOffset > session.SentOutputOffset);
            if (chunk is null) { await session.OutputChanged.WaitAsync(cancellationToken).ConfigureAwait(false); continue; }
            int skip = checked((int)(session.SentOutputOffset - chunk.Offset));
            byte[] record = TerminalProtocol.CreateOutput(session.SentOutputOffset, chunk.Bytes.AsSpan(skip));
            if (!peer.TrySend(record)) { await Task.Delay(10, cancellationToken).ConfigureAwait(false); continue; }
            lock (session.Gate) session.SentOutputOffset = chunk.EndOffset;
        }
    }

    private async Task ReceiveRecordsAsync(TerminalSession session, ITerminalWebRtcPeer peer, CancellationToken cancellationToken)
    {
        await foreach (byte[] bytes in peer.Messages.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!TerminalProtocol.TryParse(bytes, out var record)) { await EndAsync(session, "invalid-record").ConfigureAwait(false); return; }
            if (record.Kind == TerminalRecordKind.Input)
            {
                byte[] input = record.Payload.ToArray();
                lock (session.Gate)
                {
                    if (session.QueuedInputBytes + input.Length > TerminalProtocol.MaximumQueuedInputBytes || !session.Input.Writer.TryWrite(input)) { _ = EndAsync(session, "input-overflow"); return; }
                    session.QueuedInputBytes += input.Length;
                }
            }
            else if (record.Kind == TerminalRecordKind.Resize) session.Process.Resize(record.Columns, record.Rows);
            else if (record.Kind == TerminalRecordKind.Acknowledgement)
            {
                bool valid;
                bool advanced;
                lock (session.Gate)
                {
                    advanced = record.Offset > session.AcknowledgedOutputOffset;
                    valid = record.Offset >= session.AcknowledgedOutputOffset && record.Offset <= session.SentOutputOffset &&
                        (record.Offset == session.AcknowledgedOutputOffset || session.Output.Any(chunk => chunk.EndOffset == record.Offset));
                    if (valid)
                    {
                        session.AcknowledgedOutputOffset = record.Offset;
                        while (session.Output.First is { } first && first.Value.EndOffset <= record.Offset) session.Output.RemoveFirst();
                    }
                }
                if (!valid) { await EndAsync(session, "invalid-offset").ConfigureAwait(false); return; }
                if (advanced) session.OutputSpace.Release();
            }
            else { await EndAsync(session, "invalid-record").ConfigureAwait(false); return; }
        }
    }

    private async Task DetachAsync(TerminalSession session, ITerminalWebRtcPeer? peer, string reason)
    {
        bool detached;
        CancellationTokenSource? oldPeerLifetime = null;
        CancellationTokenSource? oldReconnectLifetime = null;
        lock (session.Gate)
        {
            detached = ReferenceEquals(session.Peer, peer) && Volatile.Read(ref session.DisposeStarted) == 0;
            if (detached)
            {
                session.Peer = null;
                session.Attached = false;
                session.ControlSocket = null;
                oldPeerLifetime = session.PeerLifetime;
                session.SentOutputOffset = session.AcknowledgedOutputOffset;
                oldReconnectLifetime = session.ReconnectLifetime;
                session.ReconnectLifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token);
            }
        }
        if (!detached) return;
        if (oldPeerLifetime is not null) await oldPeerLifetime.CancelAsync().ConfigureAwait(false);
        if (oldReconnectLifetime is not null) await oldReconnectLifetime.CancelAsync().ConfigureAwait(false);
        if (peer is not null) await peer.DisposeAsync().ConfigureAwait(false);
        RaiseActivity(session, active: true, reason);
        CancellationTokenSource reconnect = session.ReconnectLifetime!;
        CancellationToken token = reconnect.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TerminalProtocol.ReconnectLifetime, _timeProvider, token).ConfigureAwait(false);
                if (ReferenceEquals(session.ReconnectLifetime, reconnect) && !session.Lifetime.IsCancellationRequested)
                    await EndAsync(session, "reconnect-expired").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
    }

    private async Task ObserveExitAsync(TerminalSession session)
    {
        try { _ = await session.Process.ExitCode.ConfigureAwait(false); await EndAsync(session, "shell-exited").ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { await EndAsync(session, "process-failed").ConfigureAwait(false); }
    }

    private async Task ObserveWorkerAsync(TerminalSession session, Task worker)
    {
        try { await worker.ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            await EndAsync(session, "pipe-failed").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested) { }
    }

    private async Task EndAsync(TerminalSession session, string reason, bool notify = true)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, session)) return;
            _active = null;
        }
        WebSocket? socket = session.ControlSocket;
        await session.DisposeAsync().ConfigureAwait(false);
        RaiseActivity(session, active: false, reason);
        if (notify && socket?.State == WebSocketState.Open)
        {
            try { await _transport.SendAsync(socket, new { type = "terminal.ended", terminalId = session.Id, reason }, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException) { }
        }
    }

    private void OnPermissionsChanged(object? sender, EventArgs args)
    {
        TerminalSession? session = GetActive();
        if (session is not null && !_status.CanUseTerminal(session.ClientId)) _ = EndAsync(session, "permission-revoked");
    }

    private void OnPairingRevoked(object? sender, PairingRevokedEventArgs args)
    {
        TerminalSession? session = GetActive();
        if (session is not null && (args.ClientId is null || args.ClientId == session.ClientId))
        {
            _ = EndAsync(session, "pairing-revoked");
        }
    }

    private bool RememberOperationLocked(string clientId, string operationId)
    {
        if (!_operations.Add((clientId, operationId))) return false;
        _operationOrder.Enqueue((clientId, operationId));
        while (_operationOrder.Count > 512) _operations.Remove(_operationOrder.Dequeue());
        return true;
    }

    private TerminalSession? GetActive() { lock (_gate) return _active; }

    private void RaiseActivity(TerminalSession session, bool active, string reason) =>
        ActivityChanged?.Invoke(this, new TerminalActivityChangedEventArgs(active, session.ClientId, _pairingManager.GetDeviceName(session.ClientId) ?? "paired device", reason));

    private Task SendResultAsync(WebSocket socket, string type, string operationId, bool succeeded, string? code, string message, string? terminalId, CancellationToken token) =>
        _transport.SendAsync(socket, new { type, operationId, succeeded, code, message, terminalId }, token);

    private Task SendActionResultAsync(WebSocket socket, string type, string operationId, bool succeeded, string? code, string message, CancellationToken token) =>
        _transport.SendAsync(socket, new { type, operationId, succeeded, code, message }, token);

    private Task SendStatusAsync(WebSocket socket, TerminalSession session, string state, CancellationToken token)
    {
        long acknowledgedOffset;
        lock (session.Gate) acknowledgedOffset = session.AcknowledgedOutputOffset;
        return _transport.SendAsync(socket, new { type = "terminal.status", terminalId = session.Id, state, acknowledgedOffset }, token);
    }

    private static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        _pairingManager.PairingRevoked -= OnPairingRevoked;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        TerminalSession? session;
        lock (_gate) { session = _active; _active = null; }
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed record TerminalActivityChangedEventArgs(bool Active, string ClientId, string DeviceName, string Reason);
