using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class ScreenViewCommandHandler(
    ScreenViewCoordinator coordinator,
    WebSocketTransport transport,
    Func<CancellationToken, Task<RelayTurnConfiguration?>>? getRelayTurnConfiguration = null,
    IAppLogWriter? appLog = null) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PendingStart> _pendingStarts = new(StringComparer.Ordinal);
    private readonly IAppLogWriter _appLog = appLog ?? NullAppLog.Instance;
    private bool _disposed;

    public async Task ClientDisconnectedAsync(string clientId)
    {
        coordinator.Stop(clientId);
        PendingStart? pending = GetPending(clientId);
        pending?.Cancel();
        if (pending is not null)
        {
            await pending.Task.ConfigureAwait(false);
        }
    }

    public Task GetSourcesAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        ScreenViewSourcesResult result = coordinator.GetSources(clientId);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.sources.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            sources = result.Sources
        }, cancellationToken);
    }

    public Task StartAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string displayId,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        var duplicate = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }
            if (_pendingStarts.ContainsKey(clientId))
            {
                duplicate = true;
            }
            else
            {
#pragma warning disable CA2000 // PendingStart owns and disposes the linked cancellation source.
                var pending = new PendingStart(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
#pragma warning restore CA2000
                _pendingStarts.Add(clientId, pending);
                pending.Task = RunStartAsync(
                    pending,
                    socket,
                    clientId,
                    operationId,
                    displayId,
                    clientSignature);
            }
        }

        if (duplicate)
        {
            return SendStartFailureAsync(
                socket,
                operationId,
                displayId,
                "busy",
                "Another screen-view request is already being prepared.",
                cancellationToken);
        }
        return Task.CompletedTask;
    }

    private async Task RunStartAsync(
        PendingStart pending,
        WebSocket socket,
        string clientId,
        string operationId,
        string displayId,
        string clientSignature)
    {
        await Task.Yield();
        try
        {
            await StartCoreAsync(
                socket,
                clientId,
                operationId,
                displayId,
                clientSignature,
                pending.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _appLog.Write(new AppLogEntry(
                "screen_view",
                "windows_host",
                Action: "start_failed",
                Outcome: "failed",
                Code: "background"));
            try
            {
                await SendStartFailureAsync(
                    socket,
                    operationId,
                    displayId,
                    "webrtc-unavailable",
                    "The PC could not prepare screen viewing.",
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
                if (_pendingStarts.TryGetValue(clientId, out var current) && ReferenceEquals(current, pending))
                {
                    _pendingStarts.Remove(clientId);
                }
            }
            pending.Dispose();
        }
    }

    private async Task StartCoreAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string displayId,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        RelayTurnConfiguration? relay = null;
        if (socket is RelayVirtualWebSocket)
        {
            relay = getRelayTurnConfiguration is null
                ? null
                : await getRelayTurnConfiguration(cancellationToken).ConfigureAwait(false);
            if (relay is null)
            {
                await SendStartFailureAsync(
                    socket,
                    operationId,
                    displayId,
                    "turn-unavailable",
                    "Relay screen viewing is temporarily unavailable. Commands remain connected.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        var result = await coordinator.StartAsync(clientId, operationId, displayId, clientSignature, cancellationToken, relay).ConfigureAwait(false);
        await transport.SendAsync(socket, new
        {
            type = "screen.view.start.result",
            operationId,
            displayId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            offerSdp = result.OfferSdp,
            hostSignature = result.HostSignature,
            iceServers = result.IceServers,
            turnExpiresAt = result.TurnExpiresAt,
            relayUsageBytes = result.RelayUsageBytes,
            relayUsageCheckedAt = result.RelayUsageCheckedAt,
            relayScreenQuality = result.RelayScreenQuality?.ToString()
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task AnswerAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        ScreenViewOperationResult result = coordinator.CompleteAnswer(clientId, operationId, answerSdp, clientSignature);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.answer.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    public Task StopAsync(WebSocket socket, string clientId, string operationId, CancellationToken cancellationToken)
    {
        GetPending(clientId)?.Cancel();
        coordinator.Stop(clientId);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.stop.result",
            operationId,
            succeeded = true,
            code = "stopped",
            message = "Screen viewing stopped."
        }, cancellationToken);
    }

    public async Task NotifyHostStoppedAsync(string clientId, bool disallowed, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            type = "screen.view.ended",
            reason = disallowed ? "permission-revoked" : "host-stopped",
            message = disallowed
                ? "The PC stopped screen viewing and disallowed this device."
                : "The PC stopped screen viewing."
        };
        foreach (var (_, socket) in transport.Snapshot().Where(connection =>
            string.Equals(connection.ClientId, clientId, StringComparison.Ordinal)))
        {
            try
            {
                await transport.SendAsync(socket, payload, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
            }
        }
    }

    public Task SetSourceAsync(WebSocket socket, string clientId, string operationId, string displayId, CancellationToken cancellationToken)
    {
        ScreenViewOperationResult result = coordinator.SetSource(clientId, displayId);
        return transport.SendAsync(socket, new
        {
            type = "screen.view.source.result",
            operationId,
            displayId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        PendingStart[] pending;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            pending = [.. _pendingStarts.Values];
        }

        foreach (var item in pending)
        {
            item.Cancel();
        }
        await Task.WhenAll(pending.Select(item => item.Task)).ConfigureAwait(false);
    }

    private PendingStart? GetPending(string clientId)
    {
        lock (_gate)
        {
            return _pendingStarts.GetValueOrDefault(clientId);
        }
    }

    private Task SendStartFailureAsync(
        WebSocket socket,
        string operationId,
        string displayId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        transport.SendAsync(socket, new
        {
            type = "screen.view.start.result",
            operationId,
            displayId,
            succeeded = false,
            code,
            message
        }, cancellationToken);

    private sealed class PendingStart(CancellationTokenSource cancellation) : IDisposable
    {
        public CancellationToken Token => cancellation.Token;
        public Task Task { get; set; } = Task.CompletedTask;

        public void Cancel()
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
