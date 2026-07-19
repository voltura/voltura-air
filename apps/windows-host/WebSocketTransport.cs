using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class WebSocketTransport : IDisposable
{
    public const int MaxMessageBytes = 64 * 1024;
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
    private readonly WebSocketConnectionRegistry _connections = new();

    public int ActiveSocketCount => _connections.ActiveSocketCount;
    public int SendGateCount => _connections.SendGateCount;

    public void Register(string clientId, WebSocket socket) => _connections.Register(clientId, socket);
    public void Unregister(string clientId, WebSocket socket) => _connections.Unregister(clientId, socket);
    public WebSocket[] TakeRevoked(string? clientId) => _connections.TakeRevoked(clientId);
    public (string ClientId, WebSocket Socket)[] Snapshot() => _connections.Snapshot();

    public static async Task<JsonDocument?> ReceiveAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await CloseAsync(socket, "Closing", cancellationToken);
            return null;
        }

        if (result.MessageType != WebSocketMessageType.Text)
        {
            await CloseAsync(socket, "Text messages are required", WebSocketCloseStatus.InvalidMessageType, cancellationToken);
            return null;
        }

        if (result.Count > MaxMessageBytes)
        {
            await CloseAsync(socket, "Message is too large", WebSocketCloseStatus.MessageTooBig, cancellationToken);
            return null;
        }

        if (result.EndOfMessage)
        {
            return JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        }

        using var stream = new MemoryStream(Math.Min(MaxMessageBytes, result.Count * 2));
        // MemoryStream writes are in-memory and cannot block on I/O.
#pragma warning disable CA1849
        stream.Write(buffer.AsSpan(0, result.Count));
#pragma warning restore CA1849
        while (!result.EndOfMessage)
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseAsync(socket, "Closing", cancellationToken);
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await CloseAsync(socket, "Text messages are required", WebSocketCloseStatus.InvalidMessageType, cancellationToken);
                return null;
            }

            if (stream.Length + result.Count > MaxMessageBytes)
            {
                await CloseAsync(socket, "Message is too large", WebSocketCloseStatus.MessageTooBig, cancellationToken);
                return null;
            }

#pragma warning disable CA1849
            stream.Write(buffer.AsSpan(0, result.Count));
#pragma warning restore CA1849
        }

        return JsonDocument.Parse(stream.GetBuffer().AsMemory(0, checked((int)stream.Length)));
    }

    public static Task CloseAsync(WebSocket socket, string reason, CancellationToken cancellationToken) =>
        CloseAsync(socket, reason, WebSocketCloseStatus.NormalClosure, cancellationToken);

    public static async Task CloseAsync(
        WebSocket socket,
        string reason,
        WebSocketCloseStatus status,
        CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CloseTimeout);
            await socket.CloseAsync(status, reason, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    public async Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        _ = await _connections.TrySendAsync(
            socket,
            () => SendDirectAsync(socket, payload, cancellationToken),
            cancellationToken);
    }

    public static Task SendUnauthenticatedAsync(WebSocket socket, object payload, CancellationToken cancellationToken) =>
        SendDirectAsync(socket, payload, cancellationToken);

    public void AbortAll()
    {
        foreach (var socket in _connections.TakeAll())
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

    public void Dispose() => _connections.Dispose();

    private static async Task SendDirectAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
    }
}
