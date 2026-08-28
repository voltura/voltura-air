using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace VolturaAir.Host.Features.AiAssistant;

internal sealed class AiAssistantCoordinator : IAsyncDisposable
{
    private readonly PairingManager pairingManager;
    private readonly HostStatusPayloadFactory status;
    private readonly WebSocketTransport transport;
    private readonly IAiAssistantClientFactory _clientFactory;
    private readonly Lock _gate = new();
    private const int MaximumQueuedCommands = 32;
    private readonly Channel<QueuedCommand> _commands = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(MaximumQueuedCommands + 1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _commandWorker;
    private readonly HashSet<(string ClientId, string OperationId)> _operations = [];
    private readonly Queue<(string ClientId, string OperationId)> _operationOrder = [];
    private readonly Dictionary<(string ClientId, WebSocket Socket), int> _closing = [];
    private readonly ConditionalWeakTable<WebSocket, object> _disconnectedSockets = [];
    private PendingOpen? _pendingOpen;
    private AssistantSession? _active;
    private int _queuedCommands;
    private int _disposed;

    internal AiAssistantCoordinator(
        PairingManager pairingManager,
        HostStatusPayloadFactory status,
        WebSocketTransport transport,
        IAiAssistantClientFactory? clientFactory = null)
    {
        this.pairingManager = pairingManager;
        this.status = status;
        this.transport = transport;
        _clientFactory = clientFactory ?? CodexAiAssistantClientFactory.Instance;
        pairingManager.PermissionsChanged += OnPermissionsChanged;
        pairingManager.PairingRevoked += OnPairingRevoked;
        AppPermissionSettings.Changed += OnPermissionsChanged;
        _commandWorker = Task.Run(RunCommandsAsync);
    }

    internal event EventHandler? StateChanged;

    internal AiAssistantCapabilityState GetCapability(string clientId)
    {
        bool available = _clientFactory.IsAvailable;
        lock (_gate)
        {
            const bool enabled = true;
            return new(
                enabled,
                available,
                _active is not null,
                _active?.ClientId == clientId,
                _active?.TurnRunning == true,
                _active?.FailureCode);
        }
    }

    internal void QueueOpen(WebSocket socket, string clientId, string operationId, string signature, CancellationToken cancellationToken) =>
        Queue(socket, operationId, "ai.assistant.open.result", token => OpenAsync(socket, clientId, operationId, signature, token), control: false, cancellationToken);

    internal void QueueAsk(WebSocket socket, string clientId, string operationId, string question, string signature, CancellationToken cancellationToken) =>
        Queue(socket, operationId, "ai.assistant.ask.result", token => AskAsync(socket, clientId, operationId, question, signature, token), control: false, cancellationToken);

    internal void QueueReset(WebSocket socket, string clientId, string operationId, string signature, CancellationToken cancellationToken) =>
        Queue(socket, operationId, "ai.assistant.reset.result", token => ResetAsync(socket, clientId, operationId, signature, token), control: false, cancellationToken);

    internal void QueueClose(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        PendingOpen? pendingOpen;
        AssistantSession? session;
        var closeKey = (clientId, socket);
        lock (_gate)
        {
            _closing.TryGetValue(closeKey, out int closeCount);
            _closing[closeKey] = closeCount + 1;
            pendingOpen = _pendingOpen is { } pending && pending.ClientId == clientId && ReferenceEquals(pending.Socket, socket)
                ? pending
                : null;
            session = _active is { } active && active.ClientId == clientId && ReferenceEquals(active.Socket, socket)
                ? active
                : null;
        }
        pendingOpen?.Cancel(PendingOpenCancellationReason.Close);
        session?.Cancel();
        if (!Queue(socket, operationId, "ai.assistant.close.result", token => CloseAsync(socket, clientId, operationId, token), control: true, cancellationToken))
        {
            ReleaseClosing(closeKey);
            if (session is not null) _ = EndAsync(session, notify: false);
        }
    }

    private bool Queue(
        WebSocket socket,
        string operationId,
        string resultType,
        Func<CancellationToken, Task> action,
        bool control,
        CancellationToken cancellationToken)
    {
        bool reserved = false;
        if (!control)
        {
            reserved = Interlocked.Increment(ref _queuedCommands) <= MaximumQueuedCommands;
            if (!reserved) Interlocked.Decrement(ref _queuedCommands);
        }
        if (Volatile.Read(ref _disposed) != 0 || (!reserved && !control) || !_commands.Writer.TryWrite(new(socket, action, reserved, cancellationToken)))
        {
            if (reserved) Interlocked.Decrement(ref _queuedCommands);
            _ = SendResultAsync(socket, resultType, operationId, false, "busy", "The AI Assistant is handling too many requests. Try again shortly.", cancellationToken)
                .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }
        return true;
    }

    private async Task RunCommandsAsync()
    {
        try
        {
            await foreach (QueuedCommand command in _commands.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (command.Reserved) Interlocked.Decrement(ref _queuedCommands);
                if (_disconnectedSockets.TryGetValue(command.Socket, out _)) continue;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(command.CancellationToken, _lifetime.Token);
                try { await command.Action(linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
                catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
                {
                    AssistantSession? session;
                    lock (_gate)
                    {
                        session = _active is { } active && ReferenceEquals(active.Socket, command.Socket)
                            ? active
                            : null;
                    }
                    if (session is not null) await EndAsync(session, notify: false).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // The command path must not surface prompt or transcript content through exceptions.
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task OpenAsync(WebSocket socket, string clientId, string operationId, string signature, CancellationToken cancellationToken)
    {
        if (!Verify(clientId, signature, AiAssistantProtocol.OpenTranscript(clientId, pairingManager.HostIdentity.PublicKey, operationId)))
        {
            await SendResultAsync(socket, "ai.assistant.open.result", operationId, false, "invalid-proof", "The Assistant request could not be authenticated.", cancellationToken).ConfigureAwait(false);
            return;
        }

        PendingOpen? pendingOpen = null;
        lock (_gate)
        {
            if (_closing.ContainsKey((clientId, socket)) || _disconnectedSockets.TryGetValue(socket, out _))
            {
                return;
            }
            if (RememberOperation(clientId, operationId))
            {
                pendingOpen = new(clientId, socket, operationId);
                _pendingOpen = pendingOpen;
            }
        }
        if (pendingOpen is null)
        {
            await SendResultAsync(
                socket,
                "ai.assistant.open.result",
                operationId,
                false,
                "busy",
                "The AI Assistant is active on another paired device or this request was already used.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        using (pendingOpen)
        using (var openCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pendingOpen.CancellationToken))
        {
            try
            {
                await OpenAcceptedAsync(socket, clientId, operationId, pendingOpen, openCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                openCancellation.IsCancellationRequested &&
                pendingOpen.CancellationReason == PendingOpenCancellationReason.AccessRevoked &&
                !cancellationToken.IsCancellationRequested)
            {
                await pendingOpen.WaitForAccessRevocationAsync().ConfigureAwait(false);
                AssistantSession? lateSession = GetOwned(clientId, socket);
                if (lateSession is not null)
                    await EndAsync(lateSession, notify: false).ConfigureAwait(false);
            }
            finally
            {
                await pendingOpen.WaitForAccessRevocationAsync().ConfigureAwait(false);
                lock (_gate)
                {
                    if (ReferenceEquals(_pendingOpen, pendingOpen)) _pendingOpen = null;
                }
            }
        }
    }

    private async Task OpenAcceptedAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        PendingOpen pendingOpen,
        CancellationToken cancellationToken)
    {
        bool openedResultSent = false;
        if (!CanUse(clientId))
        {
            await SendResultAsync(socket, "ai.assistant.open.result", operationId, false, "unavailable", AvailabilityMessage(clientId), cancellationToken).ConfigureAwait(false);
            return;
        }

        AssistantSession session;
        bool ownerIsWorking;
        while (true)
        {
            Task? endingSession = null;
            ownerIsWorking = false;
            lock (_gate)
            {
                if (_closing.ContainsKey((clientId, socket)) || _disconnectedSockets.TryGetValue(socket, out _))
                {
                    return;
                }
                if (_active is null)
                {
                    session = new(clientId, socket);
                    _active = session;
                }
                else if (_active.Ending)
                {
                    endingSession = _active.Ended.Task;
                    session = null!;
                }
                else if (_active.ClientId == clientId)
                {
                    if (_active.TurnRunning)
                    {
                        ownerIsWorking = true;
                        session = null!;
                    }
                    else
                    {
                        session = _active;
                        session.Socket = socket;
                    }
                }
                else session = null!;
            }

            if (endingSession is null) break;
            await endingSession.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        if (session is null)
        {
            await SendResultAsync(
                socket,
                "ai.assistant.open.result",
                operationId,
                false,
                "busy",
                ownerIsWorking
                    ? "Wait for the current answer to finish before reopening the AI Assistant."
                    : "The AI Assistant is active on another paired device or this request was already used.",
                cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!CanUse(clientId))
        {
            await SendResultAsync(socket, "ai.assistant.open.result", operationId, false, "permission-denied", AvailabilityMessage(clientId), cancellationToken).ConfigureAwait(false);
            await EndAsync(session, notify: false).ConfigureAwait(false);
            return;
        }

        try
        {
            if (session.Client is null)
            {
                session.Client = await _clientFactory.ConnectAsync(cancellationToken).ConfigureAwait(false);
                session.Client.AgentMessageCompleted += (threadId, turnId, itemId, text) => OnAssistantMessage(session, threadId, turnId, itemId, text);
                session.Client.TurnCompleted += (threadId, turnId, turnStatus) => OnTurnCompleted(session, threadId, turnId, turnStatus);
                session.Client.ConnectionClosed += () => OnConnectionClosed(session);
                CodexThreadSummary? thread = await session.Client.FindAssistantAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfNotLive(session);
                if (thread is null) thread = await session.Client.StartAssistantAsync(cancellationToken).ConfigureAwait(false);
                else await session.Client.ResumeAssistantAsync(thread.Id, cancellationToken).ConfigureAwait(false);
                ThrowIfNotLive(session);
                session.ThreadId = thread.Id;
            }

            CodexThreadDetail snapshot = await session.Client.ReadThreadAsync(session.ThreadId!, cancellationToken).ConfigureAwait(false);
            ThrowIfNotLive(session);
            await pendingOpen.PublishOpenResultAsync(
                token => SendResultAsync(socket, "ai.assistant.open.result", operationId, true, null, "AI Assistant ready.", token),
                cancellationToken).ConfigureAwait(false);
            openedResultSent = true;
            await SendSnapshotAsync(session, snapshot, cancellationToken).ConfigureAwait(false);
            ThrowIfNotLive(session);
            await SendStateAsync(session, session.TurnRunning ? "working" : "ready", null, cancellationToken).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is CodexCompatibilityException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            session.FailureCode = "codex-unavailable";
            if (!openedResultSent)
                await SendResultAsync(socket, "ai.assistant.open.result", operationId, false, session.FailureCode, BoundError(exception.Message), cancellationToken).ConfigureAwait(false);
            await EndAsync(session, notify: openedResultSent).ConfigureAwait(false);
        }
    }

    private async Task AskAsync(WebSocket socket, string clientId, string operationId, string question, string signature, CancellationToken cancellationToken)
    {
        AssistantSession? session = GetOwned(clientId, socket);
        if (session is null || session.Client is null || session.ThreadId is null)
        {
            await SendResultAsync(socket, "ai.assistant.ask.result", operationId, false, "not-open", "Open the AI Assistant first.", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!CanUse(clientId))
        {
            await SendResultAsync(socket, "ai.assistant.ask.result", operationId, false, "permission-denied", "AI Assistant is blocked for this device.", cancellationToken).ConfigureAwait(false);
            await EndAsync(session).ConfigureAwait(false);
            return;
        }
        string normalized = question.Trim();
        if (session.TurnRunning || !RememberOperationThreadSafe(clientId, operationId))
        {
            await SendResultAsync(socket, "ai.assistant.ask.result", operationId, false, session.TurnRunning ? "busy" : "replayed-operation", session.TurnRunning ? "Wait for the current answer to finish." : "This question was already submitted.", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!Verify(clientId, signature, AiAssistantProtocol.AskTranscript(clientId, pairingManager.HostIdentity.PublicKey, operationId, normalized)))
        {
            await SendResultAsync(socket, "ai.assistant.ask.result", operationId, false, "invalid-proof", "The question could not be authenticated.", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            CodexTurnHandle turn = await session.Client.StartTurnAsync(session.ThreadId, normalized, session.Lifetime.Token).ConfigureAwait(false);
            ThrowIfNotLive(session);
            session.TurnId = turn.TurnId;
            session.TurnRunning = true;
            try
            {
                await SendMessageAsync(session, operationId, "user", normalized, session.Lifetime.Token).ConfigureAwait(false);
                ThrowIfNotLive(session);
                await SendResultAsync(socket, "ai.assistant.ask.result", operationId, true, null, "Question sent.", session.Lifetime.Token).ConfigureAwait(false);
                ThrowIfNotLive(session);
                await SendStateAsync(session, "working", null, session.Lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                // Codex can complete a very short turn before turn/start returns. Hold those
                // notifications until the user's message and working state are ordered first.
                session.Client.ReleaseTurnNotifications(session.ThreadId);
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is CodexCompatibilityException or IOException or InvalidOperationException)
        {
            session.TurnRunning = false;
            session.FailureCode = "turn-uncertain";
            await SendResultAsync(
                socket,
                "ai.assistant.ask.result",
                operationId,
                false,
                session.FailureCode,
                BoundError($"{exception.Message} Reopen the Assistant before asking again."),
                cancellationToken).ConfigureAwait(false);
            await EndAsync(session).ConfigureAwait(false);
        }
    }

    private async Task ResetAsync(WebSocket socket, string clientId, string operationId, string signature, CancellationToken cancellationToken)
    {
        bool resetResultSent = false;
        AssistantSession? session = GetOwned(clientId, socket);
        if (session is null || session.Client is null || session.TurnRunning)
        {
            await SendResultAsync(socket, "ai.assistant.reset.result", operationId, false, "busy", session?.TurnRunning == true ? "Wait for the current answer to finish." : "Open the AI Assistant first.", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!RememberOperationThreadSafe(clientId, operationId) || !Verify(clientId, signature, AiAssistantProtocol.ResetTranscript(clientId, pairingManager.HostIdentity.PublicKey, operationId)))
        {
            await SendResultAsync(socket, "ai.assistant.reset.result", operationId, false, "invalid-proof", "The new-conversation request could not be authenticated.", cancellationToken).ConfigureAwait(false);
            return;
        }
        try
        {
            string previousThreadId = session.ThreadId!;
            CodexThreadSummary replacement = await session.Client.ReplaceAssistantAsync(previousThreadId, session.Lifetime.Token).ConfigureAwait(false);
            ThrowIfNotLive(session);
            session.ThreadId = replacement.Id;
            session.TurnId = null;
            ResetSequence(session);
            CodexThreadDetail snapshot = await session.Client.ReadThreadAsync(session.ThreadId, session.Lifetime.Token).ConfigureAwait(false);
            ThrowIfNotLive(session);
            await SendResultAsync(socket, "ai.assistant.reset.result", operationId, true, null, "New Assistant conversation ready.", session.Lifetime.Token).ConfigureAwait(false);
            resetResultSent = true;
            await SendSnapshotAsync(session, snapshot, session.Lifetime.Token).ConfigureAwait(false);
            ThrowIfNotLive(session);
            await SendStateAsync(session, "ready", null, session.Lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is CodexCompatibilityException or IOException or InvalidOperationException)
        {
            session.FailureCode = "reset-uncertain";
            if (!resetResultSent)
                await SendResultAsync(socket, "ai.assistant.reset.result", operationId, false, session.FailureCode, BoundError(exception.Message), cancellationToken).ConfigureAwait(false);
            await EndAsync(session, notify: resetResultSent).ConfigureAwait(false);
        }
    }

    private async Task CloseAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        var closeKey = (clientId, socket);
        try
        {
            AssistantSession? session = GetOwned(clientId, socket);
            if (session is not null) await EndAsync(session, notify: false).ConfigureAwait(false);
            await SendResultAsync(socket, "ai.assistant.close.result", operationId, true, null, "AI Assistant closed.", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseClosing(closeKey);
        }
    }

    private void ReleaseClosing((string ClientId, WebSocket Socket) closeKey)
    {
        lock (_gate)
        {
            if (!_closing.TryGetValue(closeKey, out int closeCount)) return;
            if (closeCount == 1) _closing.Remove(closeKey);
            else _closing[closeKey] = closeCount - 1;
        }
    }

    private async Task SendSnapshotAsync(AssistantSession session, CodexThreadDetail detail, CancellationToken cancellationToken)
    {
        ResetSequence(session);
        await transport.SendAsync(session.Socket, new { type = "ai.assistant.snapshot.start" }, cancellationToken).ConfigureAwait(false);
        foreach (CodexTranscriptEntry entry in detail.Entries)
        {
            await SendMessageAsync(session, entry.Id, entry.Sender, entry.Text, cancellationToken).ConfigureAwait(false);
        }
        await transport.SendAsync(session.Socket, new { type = "ai.assistant.snapshot.complete", messageCount = detail.Entries.Count }, cancellationToken).ConfigureAwait(false);
    }

    private void OnAssistantMessage(AssistantSession session, string threadId, string turnId, string itemId, string text)
    {
        if (session.ThreadId != threadId || session.TurnId != turnId || session.Lifetime.IsCancellationRequested) return;
        if (!session.EnqueueOutbound(async () =>
        {
            try
            {
                await SendMessageAsync(session, itemId, "assistant", text, session.Lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
                session.FailureCode = "transport-closed";
                _ = EndAsync(session, notify: false);
            }
        }))
        {
            session.FailureCode = "output-overflow";
            _ = EndAsync(session);
        }
    }

    private void OnTurnCompleted(AssistantSession session, string threadId, string turnId, string turnStatus)
    {
        if (session.ThreadId != threadId || session.TurnId != turnId) return;
        if (!session.EnqueueOutbound(async () =>
        {
            session.TurnRunning = false;
            session.TurnId = null;
            try { await SendStateAsync(session, turnStatus == "completed" ? "ready" : "failed", turnStatus == "completed" ? null : "The Assistant answer did not complete.", session.Lifetime.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
                session.FailureCode = "transport-closed";
                _ = EndAsync(session, notify: false);
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }))
        {
            session.FailureCode = "output-overflow";
            _ = EndAsync(session);
        }
    }

    private void OnConnectionClosed(AssistantSession session)
    {
        session.FailureCode = "codex-closed";
        _ = EndAsync(session);
    }

    internal void ClientDisconnected(string clientId, WebSocket socket)
    {
        _disconnectedSockets.GetValue(socket, static _ => new object());
        PendingOpen? pendingOpen;
        AssistantSession? session;
        lock (_gate)
        {
            _closing.Remove((clientId, socket));
            pendingOpen = _pendingOpen is { } pending && pending.ClientId == clientId && ReferenceEquals(pending.Socket, socket)
                ? pending
                : null;
            session = _active is { } active && active.ClientId == clientId && ReferenceEquals(active.Socket, socket)
                ? active
                : null;
        }
        pendingOpen?.Cancel(PendingOpenCancellationReason.Disconnected);
        if (session is not null) _ = EndAsync(session, notify: false);
    }

    private void OnPermissionsChanged(object? sender, EventArgs args)
    {
        PendingOpen? candidate;
        lock (_gate)
        {
            candidate = _pendingOpen;
        }
        bool candidateLostAccess = candidate is not null && !status.CanUseAiAssistant(candidate.ClientId);
        PendingOpen? revokedPending = null;
        AssistantSession? session;
        bool pendingRevocationStarted = false;
        lock (_gate)
        {
            if (candidateLostAccess && ReferenceEquals(_pendingOpen, candidate))
            {
                PendingOpen ownedPending = candidate!;
                revokedPending = ownedPending;
                pendingRevocationStarted = ownedPending.RevokeAccess(
                    () => HandlePendingAccessRevokedAsync(ownedPending));
            }
            session = _active;
        }
        if (session is not null && !CanUse(session.ClientId))
        {
            if (!pendingRevocationStarted || revokedPending?.Owns(session.ClientId, session.Socket) != true)
                _ = EndAsync(session);
        }
    }

    private void OnPairingRevoked(object? sender, PairingRevokedEventArgs args)
    {
        PendingOpen? revokedPending = null;
        AssistantSession? session;
        bool pendingRevocationStarted = false;
        lock (_gate)
        {
            if (_pendingOpen is { } pendingOpen && (args.ClientId is null || args.ClientId == pendingOpen.ClientId))
            {
                revokedPending = pendingOpen;
                pendingRevocationStarted = pendingOpen.RevokeAccess(
                    () => HandlePendingAccessRevokedAsync(pendingOpen));
            }
            session = _active;
        }
        if (session is not null && (args.ClientId is null || args.ClientId == session.ClientId))
        {
            if (!pendingRevocationStarted || revokedPending?.Owns(session.ClientId, session.Socket) != true)
                _ = EndAsync(session);
        }
    }

    private async Task HandlePendingAccessRevokedAsync(PendingOpen pendingOpen)
    {
        try
        {
            await pendingOpen.PublishAccessRevocationAsync(
                async () =>
                {
                    AssistantSession? session = GetOwned(pendingOpen.ClientId, pendingOpen.Socket);
                    if (session is not null) await EndAsync(session, notify: false).ConfigureAwait(false);
                },
                () => pendingOpen.Socket.State == WebSocketState.Open
                    ? SendResultAsync(
                        pendingOpen.Socket,
                        "ai.assistant.open.result",
                        pendingOpen.OperationId,
                        false,
                        "permission-denied",
                        "AI Assistant is blocked for this device.",
                        CancellationToken.None)
                    : Task.CompletedTask,
                () => pendingOpen.Socket.State == WebSocketState.Open
                    ? transport.SendAsync(
                        pendingOpen.Socket,
                        new { type = "ai.assistant.closed", reason = "permission-denied" },
                        CancellationToken.None)
                    : Task.CompletedTask).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or WebSocketException or ObjectDisposedException or OperationCanceledException) { }
    }

    private bool CanUse(string clientId) =>
        _clientFactory.IsAvailable && status.CanUseAiAssistant(clientId);

    private string AvailabilityMessage(string clientId) => !_clientFactory.IsAvailable
            ? "Install Codex on this PC before using the AI Assistant."
            : !status.CanUseAiAssistant(clientId)
                ? "AI Assistant is available only to a paired device using the My device profile."
                : "Codex is unavailable.";

    private bool Verify(string clientId, string signature, string transcript) =>
        pairingManager.VerifyClientSignature(clientId, Encoding.UTF8.GetBytes(transcript), signature);

    private AssistantSession? GetOwned(string clientId, WebSocket socket)
    {
        lock (_gate) return _active is { } session && session.ClientId == clientId && ReferenceEquals(session.Socket, socket) ? session : null;
    }

    private void ThrowIfNotLive(AssistantSession session)
    {
        session.Lifetime.Token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (ReferenceEquals(_active, session) && !session.Ending) return;
        }
        throw new OperationCanceledException(session.Lifetime.Token);
    }

    private bool RememberOperationThreadSafe(string clientId, string operationId)
    {
        lock (_gate) return RememberOperation(clientId, operationId);
    }

    private bool RememberOperation(string clientId, string operationId)
    {
        if (!_operations.Add((clientId, operationId))) return false;
        _operationOrder.Enqueue((clientId, operationId));
        while (_operationOrder.Count > AiAssistantProtocol.MaximumOperations) _operations.Remove(_operationOrder.Dequeue());
        return true;
    }

    private async Task SendMessageAsync(AssistantSession session, string messageId, string sender, string text, CancellationToken cancellationToken)
    {
        string protocolMessageId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageId)));
        string[] chunks = [.. AiAssistantProtocol.ChunkMessage(text)];
        for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            await transport.SendAsync(session.Socket, new
            {
                type = "ai.assistant.message",
                sequence = NextSequence(session),
                messageId = protocolMessageId,
                chunkIndex,
                finalChunk = chunkIndex == chunks.Length - 1,
                sender,
                text = chunks[chunkIndex]
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static long NextSequence(AssistantSession session)
    {
        lock (session.Gate) return ++session.NextMessageSequence;
    }

    private static void ResetSequence(AssistantSession session)
    {
        lock (session.Gate) session.NextMessageSequence = 0;
    }

    private Task SendStateAsync(AssistantSession session, string state, string? message, CancellationToken cancellationToken) =>
        transport.SendAsync(session.Socket, new { type = "ai.assistant.state", state, message }, cancellationToken);

    private Task SendResultAsync(WebSocket socket, string type, string operationId, bool succeeded, string? code, string message, CancellationToken cancellationToken) =>
        transport.SendAsync(socket, new { type, operationId, succeeded, code, message }, cancellationToken);

    private async Task EndAsync(AssistantSession session, bool notify = true)
    {
        bool ownsCleanup = false;
        lock (_gate)
        {
            if (!ReferenceEquals(_active, session)) return;
            if (!session.Ending)
            {
                session.Ending = true;
                ownsCleanup = true;
            }
        }
        if (!ownsCleanup)
        {
            await session.Ended.Task.ConfigureAwait(false);
            return;
        }
        WebSocket socket = session.Socket;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
            if (notify && socket.State == WebSocketState.Open)
            {
                try { await transport.SendAsync(socket, new { type = "ai.assistant.closed", reason = session.FailureCode ?? "closed" }, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException) { }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, session)) _active = null;
            }
            session.Ended.TrySetResult();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string BoundError(string message) => AiAssistantProtocol.BoundWithEllipsis(message, 240);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        pairingManager.PermissionsChanged -= OnPermissionsChanged;
        pairingManager.PairingRevoked -= OnPairingRevoked;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        _commands.Writer.TryComplete();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        PendingOpen? pendingOpen;
        AssistantSession? session;
        lock (_gate)
        {
            pendingOpen = _pendingOpen;
            session = _active;
        }
        pendingOpen?.Cancel(PendingOpenCancellationReason.Shutdown);
        if (session is not null) await EndAsync(session, notify: false).ConfigureAwait(false);
        try { await _commandWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        lock (_gate)
        {
            pendingOpen = _pendingOpen;
            _pendingOpen = null;
        }
        pendingOpen?.Dispose();
        _lifetime.Dispose();
    }

    private enum PendingOpenCancellationReason
    {
        None,
        Close,
        Disconnected,
        AccessRevoked,
        Shutdown
    }

    private sealed class PendingOpen(string clientId, WebSocket socket, string operationId) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly AiAssistantPendingOpenPublication _publication = new();
        private readonly Lock _accessRevocationGate = new();
        private Task? _accessRevocation;
        private int _cancellationReason;

        internal string ClientId { get; } = clientId;
        internal WebSocket Socket { get; } = socket;
        internal string OperationId { get; } = operationId;
        internal CancellationToken CancellationToken => _cancellation.Token;
        internal PendingOpenCancellationReason CancellationReason =>
            (PendingOpenCancellationReason)Volatile.Read(ref _cancellationReason);
        internal bool Owns(string clientId, WebSocket socket) =>
            ClientId == clientId && ReferenceEquals(Socket, socket);
        internal Task PublishOpenResultAsync(Func<CancellationToken, Task> publish, CancellationToken cancellationToken) =>
            _publication.PublishOpenResultAsync(publish, cancellationToken);
        internal Task PublishAccessRevocationAsync(
            Func<Task> cleanup,
            Func<Task> publishOpenFailure,
            Func<Task> publishClosed) =>
            _publication.PublishAccessRevocationAsync(cleanup, publishOpenFailure, publishClosed);

        internal bool Cancel(PendingOpenCancellationReason reason)
        {
            _ = Interlocked.CompareExchange(ref _cancellationReason, (int)reason, (int)PendingOpenCancellationReason.None);
            try { _cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            return CancellationReason == reason;
        }

        internal bool RevokeAccess(Func<Task> start)
        {
            TaskCompletionSource? completion = null;
            lock (_accessRevocationGate)
            {
                _ = Interlocked.CompareExchange(
                    ref _cancellationReason,
                    (int)PendingOpenCancellationReason.AccessRevoked,
                    (int)PendingOpenCancellationReason.None);
                if (CancellationReason == PendingOpenCancellationReason.AccessRevoked && _accessRevocation is null)
                {
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _accessRevocation = completion.Task;
                }
            }
            try { _cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            if (completion is not null)
                _ = Task.Run(() => CompleteAccessRevocationAsync(start, completion), CancellationToken.None);
            return CancellationReason == PendingOpenCancellationReason.AccessRevoked;
        }

        private static async Task CompleteAccessRevocationAsync(Func<Task> start, TaskCompletionSource completion)
        {
            try
            {
                await start().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        internal Task WaitForAccessRevocationAsync()
        {
            lock (_accessRevocationGate) return _accessRevocation ?? Task.CompletedTask;
        }

        public void Dispose()
        {
            _publication.Dispose();
            _cancellation.Dispose();
        }
    }

    private sealed class AssistantSession : IAsyncDisposable
    {
        private readonly Channel<Func<Task>> _outbound = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        private readonly Task _outboundWorker;

        internal AssistantSession(string clientId, WebSocket socket)
        {
            ClientId = clientId;
            Socket = socket;
            _outboundWorker = Task.Run(RunOutboundAsync);
        }

        internal Lock Gate { get; } = new();
        internal string ClientId { get; }
        internal WebSocket Socket { get; set; }
        internal CancellationTokenSource Lifetime { get; } = new();
        internal IAiAssistantClient? Client { get; set; }
        internal string? ThreadId { get; set; }
        internal string? TurnId { get; set; }
        internal bool TurnRunning { get; set; }
        internal bool Ending { get; set; }
        internal TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long NextMessageSequence { get; set; }
        internal string? FailureCode { get; set; }
        internal bool EnqueueOutbound(Func<Task> action) => _outbound.Writer.TryWrite(action);
        internal void Cancel()
        {
            try { Lifetime.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private async Task RunOutboundAsync()
        {
            try
            {
                await foreach (Func<Task> action in _outbound.Reader.ReadAllAsync(Lifetime.Token).ConfigureAwait(false))
                {
                    try { await action().ConfigureAwait(false); }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { }
                }
            }
            catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _outbound.Writer.TryComplete();
            await Lifetime.CancelAsync().ConfigureAwait(false);
            try { await _outboundWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (Client is not null) await Client.DisposeAsync().ConfigureAwait(false);
            Lifetime.Dispose();
        }
    }

    private sealed record QueuedCommand(WebSocket Socket, Func<CancellationToken, Task> Action, bool Reserved, CancellationToken CancellationToken);
}

internal sealed record AiAssistantCapabilityState(
    bool Enabled,
    bool Available,
    bool Active,
    bool OwnedByClient,
    bool Working,
    string? FailureCode);
