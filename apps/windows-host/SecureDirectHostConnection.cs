using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class SecureDirectHostConnection : IAsyncDisposable
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)
    ];
    private readonly RelayEndpointDescriptor _endpoint;
    private readonly RelayRoutingIdentity _identity;
    private readonly SecureDirectSessions _sessions;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private ClientWebSocket? _socket;
    private Task? _runTask;
    private int _disposeState;

    internal SecureDirectHostConnection(
        RelayEndpointDescriptor endpoint,
        RelayRoutingIdentity identity,
        IPAddress bindAddress,
        Func<WebSocket, string, Action, CancellationToken, Task> handleSession,
        IAppLogWriter log)
    {
        _endpoint = endpoint;
        _identity = identity;
        _sessions = new SecureDirectSessions(
            bindAddress,
            handleSession,
            SendEnvelopeAsync,
            () => log.Write(new AppLogEntry("secure_direct", "windows_host", Action: "device_session_failed", Outcome: "failed", Code: "handler")),
            QueueDeviceAuthenticated);
    }

    internal string RouteId => _identity.RouteId;
    internal void Start() => _runTask ??= RunAsync(_shutdown.Token);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            _socket = socket;
            try
            {
                await socket.ConnectAsync(CreateHostUri(), cancellationToken).ConfigureAwait(false);
                await AuthenticateAsync(socket, cancellationToken).ConfigureAwait(false);
                attempt = 0;
                await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException or JsonException or InvalidDataException) { }
            finally
            {
                _socket = null;
                _sessions.CancelPendingSignaling();
            }
            try { await Task.Delay(RetryDelays[Math.Min(attempt++, RetryDelays.Length - 1)], cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task AuthenticateAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        await SendTextAsync(socket, new { type = "relay.host.hello", routeId = _identity.RouteId, publicKey = _identity.PublicKey }, cancellationToken).ConfigureAwait(false);
        using var challenge = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
        if (!challenge.RootElement.TryGetProperty("type", out var type) || type.GetString() != "relay.host.challenge" ||
            !challenge.RootElement.TryGetProperty("challenge", out var value) || value.GetString() is not { Length: 43 } nonce)
            throw new InvalidDataException("Secure Direct authentication challenge was invalid.");
        await SendTextAsync(socket, new { type = "relay.host.proof", signature = _identity.SignChallenge(nonce) }, cancellationToken).ConfigureAwait(false);
        using var accepted = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
        if (!accepted.RootElement.TryGetProperty("type", out type) || type.GetString() != "relay.host.accepted")
            throw new InvalidDataException("Secure Direct authentication was rejected.");
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[RelayEnvelope.MaximumEncodedBytes];
        while (socket.State == WebSocketState.Open)
        {
            var envelope = await RelayHostConnection.ReceiveRelayEnvelopeAsync(socket, buffer, cancellationToken).ConfigureAwait(false);
            if (envelope is null) return;
            if (envelope.Kind == RelayEnvelopeKind.Connected)
            {
                if (!_sessions.TryStart(envelope.SessionId, envelope.Payload, cancellationToken))
                    await SendEnvelopeAsync(new RelayEnvelope(RelayEnvelopeKind.CloseDevice, envelope.SessionId, []), cancellationToken).ConfigureAwait(false);
            }
            else if (envelope.Kind == RelayEnvelopeKind.Text)
            {
                if (!_sessions.TryApplyAnswer(envelope.SessionId, envelope.Payload))
                    await SendEnvelopeAsync(new RelayEnvelope(RelayEnvelopeKind.CloseDevice, envelope.SessionId, []), cancellationToken).ConfigureAwait(false);
            }
            else if (envelope.Kind == RelayEnvelopeKind.Disconnected) _sessions.DisconnectSignaling(envelope.SessionId);
            else throw new InvalidDataException("Secure Direct received an envelope in the wrong direction.");
        }
    }

    private async Task SendEnvelopeAsync(RelayEnvelope envelope, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket?.State != WebSocketState.Open) throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        byte[] bytes = envelope.Encode();
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false); }
        finally { _sendGate.Release(); }
    }

    private void QueueDeviceAuthenticated(Guid sessionId)
    {
        _ = SendDeviceAuthenticatedAsync(sessionId, _shutdown.Token);
    }

    private async Task SendDeviceAuthenticatedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await SendEnvelopeAsync(new RelayEnvelope(RelayEnvelopeKind.Authenticated, sessionId, []), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private Uri CreateHostUri() => new UriBuilder(_endpoint.WebSocketBase)
    {
        Path = $"{_endpoint.WebSocketBase.AbsolutePath.TrimEnd('/')}/v1/secure/host/{_identity.RouteId}"
    }.Uri;

    private static async Task SendTextAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

    private static async Task<JsonDocument> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var message = await WebSocketTransport.ReceiveBoundedMessageAsync(socket, buffer, WebSocketMessageType.Text, buffer.Length, cancellationToken).ConfigureAwait(false);
        if (message is null) throw new InvalidDataException();
        return JsonDocument.Parse(message.Value.Buffer.AsMemory(0, message.Value.Count), new JsonDocumentOptions { MaxDepth = 8 });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _socket?.Abort();
        _socket?.Dispose();
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _sessions.CloseAndDrainAsync().ConfigureAwait(false);
        _sendGate.Dispose();
        _shutdown.Dispose();
        _identity.Dispose();
    }
}
