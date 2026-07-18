using System.Net.WebSockets;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private void AbortActiveSockets()
    {
        var sockets = _connections.TakeAll();

        foreach (var socket in sockets)
        {
            try
            {
                socket.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void OnPairingRevoked(object? sender, PairingRevokedEventArgs e)
    {
        var sockets = _connections.TakeRevoked(e.ClientId);
        if (sockets.Length == 0)
        {
            return;
        }

        _ = CloseSocketsAsync(sockets, _lifetimeCancellation.Token);
    }

    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        QueueStatusBroadcast();
    }

    private void QueueStatusBroadcast()
    {
        if (Volatile.Read(ref _disposeState) == 0)
        {
            _statusBroadcastRequests.Writer.TryWrite(true);
        }
    }

    private async Task ProcessStatusBroadcastsAsync()
    {
        try
        {
            while (await _statusBroadcastRequests.Reader.WaitToReadAsync(_lifetimeCancellation.Token))
            {
                while (_statusBroadcastRequests.Reader.TryRead(out _))
                {
                }

                try
                {
                    await BroadcastStatusAsync(_lifetimeCancellation.Token);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _appLog.Write(new AppLogEntry(
                        Event: "host_lifecycle",
                        Source: "websocket",
                        Action: "broadcast_status",
                        Outcome: "failed",
                        Detail: ex.Message));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task BroadcastStatusAsync(CancellationToken cancellationToken)
    {
        var sockets = _connections.Snapshot();

        foreach (var (clientId, socket) in sockets)
        {
            try
            {
                await SendConnectedStatusAsync(socket, clientId, cancellationToken);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task CloseSocketsAsync(IEnumerable<WebSocket> sockets, CancellationToken cancellationToken)
    {
        foreach (var socket in sockets)
        {
            try
            {
                await CloseSocketAsync(socket, "Device disconnected", cancellationToken);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }
    }

}
