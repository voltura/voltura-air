using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private static async Task<JsonDocument?> ReceiveMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await CloseSocketAsync(socket, "Closing", cancellationToken);
            return null;
        }

        if (result.MessageType != WebSocketMessageType.Text)
        {
            await CloseSocketAsync(socket, "Text messages are required", WebSocketCloseStatus.InvalidMessageType, cancellationToken);
            return null;
        }

        if (result.Count > MaxWebSocketMessageBytes)
        {
            await CloseSocketAsync(socket, "Message is too large", WebSocketCloseStatus.MessageTooBig, cancellationToken);
            return null;
        }

        if (result.EndOfMessage)
        {
            return JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        }

        using var stream = new MemoryStream(Math.Min(MaxWebSocketMessageBytes, result.Count * 2));
        // MemoryStream writes are in-memory and cannot block on I/O; the synchronous
        // span overload avoids allocating an async operation for every fragment.
#pragma warning disable CA1849
        stream.Write(buffer.AsSpan(0, result.Count));
#pragma warning restore CA1849
        while (!result.EndOfMessage)
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseSocketAsync(socket, "Closing", cancellationToken);
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await CloseSocketAsync(socket, "Text messages are required", WebSocketCloseStatus.InvalidMessageType, cancellationToken);
                return null;
            }

            if (stream.Length + result.Count > MaxWebSocketMessageBytes)
            {
                await CloseSocketAsync(socket, "Message is too large", WebSocketCloseStatus.MessageTooBig, cancellationToken);
                return null;
            }

#pragma warning disable CA1849
            stream.Write(buffer.AsSpan(0, result.Count));
#pragma warning restore CA1849
        }

        return JsonDocument.Parse(stream.GetBuffer().AsMemory(0, checked((int)stream.Length)));
    }

    private static Task CloseSocketAsync(WebSocket socket, string reason, CancellationToken cancellationToken)
    {
        return CloseSocketAsync(socket, reason, WebSocketCloseStatus.NormalClosure, cancellationToken);
    }

    private static async Task CloseSocketAsync(WebSocket socket, string reason, WebSocketCloseStatus status, CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WebSocketCloseTimeout);
            await socket.CloseAsync(status, reason, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static async Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WebSocketSendTimeout);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
    }

    private async Task SendSocketAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        _ = await _connections.TrySendAsync(
            socket,
            () => SendAsync(socket, payload, cancellationToken),
            cancellationToken);
    }

    private static Task SendUnauthenticatedSocketAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        return SendAsync(socket, payload, cancellationToken);
    }

    private Task SendAudioStateAsync(WebSocket socket, string clientId, CancellationToken cancellationToken)
    {
        if (!CanControlVolume(clientId))
        {
            return Task.CompletedTask;
        }

        var state = AudioMessageRouter.TryGetState(_audioController);
        return state is null ? Task.CompletedTask : SendAudioStateAsync(socket, state, cancellationToken);
    }

    private Task SendAudioStateAsync(WebSocket socket, AudioState state, CancellationToken cancellationToken)
    {
        return SendSocketAsync(socket, new { type = "audio.state", volume = state.Volume, muted = state.Muted }, cancellationToken);
    }

    private Task SendInputAckAsync(WebSocket socket, long? sequence, CancellationToken cancellationToken)
    {
        return sequence.HasValue
            ? SendSocketAsync(socket, new { type = "input.ack", seq = sequence.Value }, cancellationToken)
            : Task.CompletedTask;
    }

    private Task SendInputErrorAsync(WebSocket socket, long? sequence, string code, string message, CancellationToken cancellationToken)
    {
        return sequence.HasValue
            ? SendSocketAsync(socket, new { type = "input.error", seq = sequence.Value, code, message }, cancellationToken)
            : SendSocketAsync(socket, new { type = "input.error", code, message }, cancellationToken);
    }

    private Task SendDisconnectedStatusAsync(WebSocket socket, string clientId, string message, CancellationToken cancellationToken)
    {
        return SendSocketAsync(
            socket,
            new { type = "status", connected = false, message, pcName = Environment.MachineName, capabilities = CreateCapabilities(clientId), host = CreateHostStatus(clientId) },
            cancellationToken);
    }

    private Task SendConnectedStatusAsync(WebSocket socket, string clientId, CancellationToken cancellationToken)
    {
        return SendSocketAsync(
            socket,
            new { type = "status", connected = true, message = "Connected", pcName = Environment.MachineName, capabilities = CreateCapabilities(clientId), host = CreateHostStatus(clientId) },
            cancellationToken);
    }

}
