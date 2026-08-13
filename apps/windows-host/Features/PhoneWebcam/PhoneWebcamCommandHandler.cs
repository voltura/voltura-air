using System.Net.WebSockets;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamCommandHandler : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<WebSocket, PendingStart> _pendingStarts = [];
    private readonly PhoneWebcamCoordinator _coordinator;
    private readonly WebSocketTransport _transport;
    private readonly Func<CancellationToken, Task<RelayTurnConfiguration?>> _getRelayTurnConfiguration;
    private int _disposed;

    internal PhoneWebcamCommandHandler(
        PhoneWebcamCoordinator coordinator,
        WebSocketTransport transport,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration)
    {
        _coordinator = coordinator;
        _transport = transport;
        _getRelayTurnConfiguration = getRelayTurnConfiguration;
        coordinator.Ended += OnEnded;
    }

    internal event EventHandler<PhoneWebcamActivityChangedEventArgs>? ActivityChanged
    {
        add => _coordinator.ActivityChanged += value;
        remove => _coordinator.ActivityChanged -= value;
    }

    internal Task StopFromHostAsync(string clientId) => _coordinator.StopAsync(clientId, "host-stopped");

    internal Task StartAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        var duplicate = false;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            if (_pendingStarts.ContainsKey(socket))
            {
                duplicate = true;
            }
            else
            {
#pragma warning disable CA2000 // PendingStart owns the linked cancellation source.
                var pending = new PendingStart(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
#pragma warning restore CA2000
                _pendingStarts.Add(socket, pending);
                pending.Task = RunStartAsync(
                    pending,
                    socket,
                    clientId,
                    operationId,
                    captureWidth,
                    captureHeight,
                    captureFps,
                    clientSignature);
            }
        }

        return duplicate
            ? SendStartResultAsync(
                socket,
                operationId,
                new PhoneWebcamStartResult(false, "busy", "Another Phone webcam request is already being prepared."),
                cancellationToken)
            : Task.CompletedTask;
    }

    private async Task RunStartAsync(
        PendingStart pending,
        WebSocket socket,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        string clientSignature)
    {
        await Task.Yield();
        try
        {
            await StartCoreAsync(
                socket,
                clientId,
                operationId,
                captureWidth,
                captureHeight,
                captureFps,
                clientSignature,
                pending.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                await SendStartResultAsync(
                    socket,
                    operationId,
                    new PhoneWebcamStartResult(false, "webrtc-unavailable", "The PC could not prepare Phone webcam."),
                    pending.Token).ConfigureAwait(false);
            }
            catch (Exception sendException) when (sendException is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_pendingStarts.TryGetValue(socket, out PendingStart? current) && ReferenceEquals(current, pending))
                {
                    _pendingStarts.Remove(socket);
                }
            }

            pending.Dispose();
        }
    }

    private async Task StartCoreAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        RelayTurnConfiguration? relay = null;
        if (socket is RelayVirtualWebSocket)
        {
            relay = await _getRelayTurnConfiguration(cancellationToken).ConfigureAwait(false);
            if (relay is null)
            {
                await SendStartResultAsync(
                    socket,
                    operationId,
                    new PhoneWebcamStartResult(false, "turn-unavailable", "Relay Phone webcam is temporarily unavailable."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        PhoneWebcamStartResult result = await _coordinator.StartAsync(
            socket,
            clientId,
            operationId,
            captureWidth,
            captureHeight,
            captureFps,
            clientSignature,
            relay,
            cancellationToken).ConfigureAwait(false);
        await SendStartResultAsync(socket, operationId, result, cancellationToken).ConfigureAwait(false);
    }

    internal async Task AnswerAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        PhoneWebcamOperationResult result = await _coordinator.CompleteAnswerAsync(
            socket,
            clientId,
            operationId,
            answerSdp,
            clientSignature).ConfigureAwait(false);
        await _transport.SendAsync(socket, new
        {
            type = "phone.webcam.answer.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task StopAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        PendingStart? pending = GetPending(socket);
        pending?.Cancel();
        if (pending is not null)
        {
            await pending.Task.ConfigureAwait(false);
        }

        await _coordinator.StopAsync(clientId, owner: socket).ConfigureAwait(false);
        await _transport.SendAsync(socket, new
        {
            type = "phone.webcam.stop.result",
            operationId,
            succeeded = true,
            code = "stopped",
            message = "Phone webcam stopped."
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ClientDisconnectedAsync(string clientId, WebSocket socket)
    {
        PendingStart? pending = GetPending(socket);
        pending?.Cancel();
        if (pending is not null)
        {
            await pending.Task.ConfigureAwait(false);
        }

        await _coordinator.StopAsync(clientId, "connection-lost", socket).ConfigureAwait(false);
    }

    private Task SendStartResultAsync(
        WebSocket socket,
        string operationId,
        PhoneWebcamStartResult result,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new
        {
            type = "phone.webcam.start.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            offerSdp = result.OfferSdp,
            hostSignature = result.HostSignature,
            iceServers = result.IceServers,
            turnExpiresAt = result.TurnExpiresAt,
            relayUsageBytes = result.RelayUsageBytes,
            relayUsageCheckedAt = result.RelayUsageCheckedAt,
            relayQuality = result.RelayQuality?.ToString(),
            maximumBitrate = result.MaximumBitrate
        }, cancellationToken);

    private void OnEnded(object? sender, PhoneWebcamActivityChangedEventArgs args) => _ = NotifyEndedAsync(args);

    private async Task NotifyEndedAsync(PhoneWebcamActivityChangedEventArgs args)
    {
        if (args.ClientId is null || args.OperationId is null || args.Owner is not WebSocket owner)
        {
            return;
        }

        var payload = new
        {
            type = "phone.webcam.ended",
            operationId = args.OperationId,
            reason = args.State,
            message = args.State == "permission-revoked"
                ? "The PC stopped Phone webcam because permission was revoked."
                : "The Phone webcam session ended."
        };
        if (!_transport.Snapshot().Any(connection =>
                ReferenceEquals(connection.Socket, owner) &&
                string.Equals(connection.ClientId, args.ClientId, StringComparison.Ordinal)))
        {
            return;
        }

        try
        {
            await _transport.SendAsync(owner, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PendingStart[] pending;
        lock (_gate)
        {
            pending = [.. _pendingStarts.Values];
        }

        foreach (PendingStart item in pending)
        {
            item.Cancel();
        }

        await Task.WhenAll(pending.Select(item => item.Task)).ConfigureAwait(false);
        _coordinator.Ended -= OnEnded;
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }

    private PendingStart? GetPending(WebSocket socket)
    {
        lock (_gate)
        {
            return _pendingStarts.GetValueOrDefault(socket);
        }
    }

    private sealed class PendingStart(CancellationTokenSource cancellation) : IDisposable
    {
        internal CancellationToken Token => cancellation.Token;
        internal Task Task { get; set; } = Task.CompletedTask;

        internal void Cancel()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose() => cancellation.Dispose();
    }
}
