using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class SecureDirectSessions(
    IPAddress bindAddress,
    Func<IDisposable?> acquireAdmission,
    Func<WebSocket, string, CancellationToken, Task> handleSession,
    Func<RelayEnvelope, CancellationToken, Task> sendEnvelope,
    Action sessionFailed,
    Func<IPAddress, SecureDirectWebSocket>? createSocket = null)
{
    private static readonly TimeSpan PeerEstablishmentTimeout = TimeSpan.FromSeconds(10);
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Session> _sessions = [];
    private int _draining;

    internal bool TryStart(Guid sessionId, byte[] sourceKey, CancellationToken ownerCancellationToken)
    {
        if (!RelayDeviceSessions.TryCreateRateLimitKey(sessionId, sourceKey, out var rateLimitKey)) return false;
        lock (_gate)
        {
            if (_draining != 0 || _sessions.ContainsKey(sessionId)) return false;
        }
        IDisposable? admission = acquireAdmission();
        if (admission is null) return false;
        SecureDirectWebSocket? socket = null;
        Session? pendingSession = null;
        try
        {
            socket = (createSocket ?? (static address => new SecureDirectWebSocket(address)))(bindAddress);
            pendingSession = new Session(sessionId, socket, rateLimitKey, admission, ownerCancellationToken);
            socket = null;
            admission = null;
            bool accepted;
            lock (_gate)
            {
                accepted = _draining == 0 && !_sessions.ContainsKey(sessionId);
                if (accepted) _sessions.Add(sessionId, pendingSession);
            }
            if (!accepted) return false;
            var session = pendingSession;
            pendingSession = null;
            _ = RunAsync(session);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException)
        {
            sessionFailed();
            return false;
        }
        finally
        {
            pendingSession?.Dispose();
            socket?.Dispose();
            admission?.Dispose();
        }
    }

    internal bool TryApplyAnswer(Guid sessionId, byte[] payload)
    {
        if (!TryParseDescription(payload, "secure.answer", out var sdp)) return false;
        Session? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session) || session.AnswerApplied) return false;
            session.AnswerApplied = true;
        }
        session.Answer.TrySetResult(sdp);
        return true;
    }

    internal void DisconnectSignaling(Guid sessionId)
    {
        Session? session;
        lock (_gate) _sessions.TryGetValue(sessionId, out session);
        if (session is not null && !session.AnswerApplied) session.StopSignaling();
    }

    internal void CancelPendingSignaling()
    {
        Session[] sessions;
        lock (_gate) sessions = [.. _sessions.Values.Where(session => !session.AnswerApplied)];
        foreach (var session in sessions) session.StopSignaling();
    }

    internal async Task CloseAndDrainAsync()
    {
        Session[] sessions;
        lock (_gate)
        {
            _draining++;
            sessions = [.. _sessions.Values];
        }
        try
        {
            foreach (var session in sessions) session.StopAll();
            await Task.WhenAll(sessions.Select(session => session.Completion.Task)).ConfigureAwait(false);
        }
        finally { lock (_gate) _draining--; }
    }

    private async Task RunAsync(Session session)
    {
        try
        {
            string offer = await session.Socket.CreateOfferAsync(session.Signaling.Token).ConfigureAwait(false);
            byte[] offerPayload = JsonSerializer.SerializeToUtf8Bytes(new { type = "secure.offer", sdp = offer }, JsonOptions.Default);
            await sendEnvelope(new RelayEnvelope(RelayEnvelopeKind.Text, session.Id, offerPayload), session.Signaling.Token).ConfigureAwait(false);
            string answer = await session.Answer.Task.WaitAsync(session.Signaling.Token).ConfigureAwait(false);
            session.Socket.ApplyAnswer(answer);

            using var peerTimeout = CancellationTokenSource.CreateLinkedTokenSource(session.Owner.Token);
            peerTimeout.CancelAfter(PeerEstablishmentTimeout);
            await session.Socket.WaitForOpenAndValidateAsync(peerTimeout.Token).ConfigureAwait(false);
            await handleSession(session.Socket, session.RateLimitKey, session.Owner.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Signaling.IsCancellationRequested || session.Owner.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            sessionFailed();
        }
        finally
        {
            if (!session.AnswerApplied)
            {
                try { await sendEnvelope(new RelayEnvelope(RelayEnvelopeKind.CloseDevice, session.Id, []), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException) { }
            }
            lock (_gate)
            {
                if (_sessions.TryGetValue(session.Id, out var current) && ReferenceEquals(current, session)) _sessions.Remove(session.Id);
            }
            session.Dispose();
            session.Completion.TrySetResult(null);
        }
    }

    internal static bool TryParseDescription(byte[] payload, string expectedType, out string sdp)
    {
        sdp = string.Empty;
        if (payload.Length == 0 || payload.Length > WebSocketTransport.MaxMessageBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != expectedType ||
                !root.TryGetProperty("sdp", out var value) || value.ValueKind != JsonValueKind.String ||
                value.GetString() is not { Length: > 0 } parsed || System.Text.Encoding.UTF8.GetByteCount(parsed) > 32 * 1024) return false;
            sdp = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private sealed class Session : IDisposable
    {
        internal Session(Guid id, SecureDirectWebSocket socket, string rateLimitKey, IDisposable admission, CancellationToken ownerToken)
        {
            Id = id;
            Socket = socket;
            RateLimitKey = rateLimitKey;
            Admission = admission;
            Owner = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
            Signaling = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        }
        internal Guid Id { get; }
        internal SecureDirectWebSocket Socket { get; }
        internal string RateLimitKey { get; }
        internal IDisposable Admission { get; }
        internal CancellationTokenSource Owner { get; }
        internal CancellationTokenSource Signaling { get; }
        internal TaskCompletionSource<string> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool AnswerApplied { get; set; }
        internal void StopSignaling() { try { Signaling.Cancel(); } catch (ObjectDisposedException) { } }
        internal void StopAll()
        {
            try { Owner.Cancel(); } catch (ObjectDisposedException) { }
            StopSignaling();
            Socket.Abort();
        }
        public void Dispose()
        {
            Socket.Dispose();
            Signaling.Dispose();
            Owner.Dispose();
            Admission.Dispose();
        }
    }
}
