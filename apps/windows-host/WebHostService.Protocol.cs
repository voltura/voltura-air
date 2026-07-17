using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private async Task HandleSocketAsync(WebSocket socket, string rateLimitKey, CancellationToken cancellationToken)
    {
        var authenticated = false;
        var authenticatedClientId = string.Empty;
        IDisposable? activeConnection = null;
        var buffer = new byte[64 * 1024];
        using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                receiveTimeout.CancelAfter(authenticated ? AuthenticatedInactivityTimeout : PairingHandshakeTimeout);
                JsonDocument? receivedDocument;
                try
                {
                    receivedDocument = await ReceiveMessageAsync(socket, buffer, receiveTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await CloseSocketAsync(
                        socket,
                        authenticated ? "Connection timed out" : "Pairing timed out",
                        WebSocketCloseStatus.EndpointUnavailable,
                        cancellationToken);
                    break;
                }

                receiveTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (receivedDocument is null)
                {
                    break;
                }

                using var document = receivedDocument;
                var root = document.RootElement;
                var type = ClientMessageValidator.TryReadType(root, out var messageType) ? messageType : null;

                if (!authenticated)
                {
                    if (type != "pair.hello")
                    {
                        await SendUnauthenticatedSocketAsync(socket, new { type = "pair.rejected", reason = "pair-first" }, cancellationToken);
                        _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                        continue;
                    }

                    if (!ClientMessageValidator.TryValidatePairHello(root, out var hello))
                    {
                        _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                        await SendUnauthenticatedSocketAsync(socket, new { type = "pair.rejected", reason = "invalid-message" }, cancellationToken);
                        continue;
                    }

                    var isRateLimited = _pairingAttemptRateLimiter.IsBlocked(rateLimitKey);
                    var result = _pairingManager.Accept(
                        hello.ClientId,
                        hello.DeviceName,
                        hello.PairToken,
                        hello.Secret,
                        platform: hello.Platform,
                        browser: hello.Browser,
                        displayMode: hello.DisplayMode);
                    if (!result.Accepted)
                    {
                        if (isRateLimited)
                        {
                            await SendUnauthenticatedSocketAsync(socket, new { type = "pair.rejected", reason = "rate-limited" }, cancellationToken);
                        }
                        else
                        {
                            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                            await SendUnauthenticatedSocketAsync(socket, new { type = "pair.rejected", reason = result.Reason }, cancellationToken);
                        }

                        continue;
                    }

                    _pairingAttemptRateLimiter.Reset(rateLimitKey);
                    var clientId = hello.ClientId;
                    var deviceName = hello.DeviceName;
                    var secret = result.Secret ?? _pairingManager.RotateSecret(clientId, deviceName);
                    authenticated = true;
                    authenticatedClientId = clientId;
                    _connections.Register(clientId, socket);
                    activeConnection = _pairingManager.TrackConnection(clientId);
                    var pcName = Environment.MachineName;
                    var capabilities = CreateCapabilities(clientId);
                    await SendSocketAsync(socket, new { type = "pair.accepted", clientId, pcName, secret, paired = true, capabilities, host = CreateHostStatus(clientId) }, cancellationToken);
                    continue;
                }

                ValidatedInputCommand? inputCommand = null;
                if (IsInputMessage(type))
                {
                    if (!ClientMessageValidator.TryDecodeInputMessage(root, type!, out var decodedInput))
                    {
                        await CloseAuthenticatedSocketAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                        break;
                    }

                    inputCommand = decodedInput;
                }
                else if (type is null || !ClientMessageValidator.IsValidAuthenticatedMessage(root, type))
                {
                    await CloseAuthenticatedSocketAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                    break;
                }

                LogReceivedClientCommand(authenticatedClientId, type!, root, inputCommand);

                if (type == "pair.disconnect")
                {
                    _pairingManager.DisconnectDevice(authenticatedClientId);
                    break;
                }

                if (type == "device.rename")
                {
                    _pairingManager.RenameDevice(authenticatedClientId, GetString(root, "deviceName"));
                    continue;
                }

                if (type == "health.ping")
                {
                    await SendSocketAsync(socket, new { type = "health.pong" }, cancellationToken);
                    continue;
                }

                if (type == "status.get")
                {
                    await SendConnectedStatusAsync(socket, authenticatedClientId, cancellationToken);
                    continue;
                }

                if (type == "pointer.speed.set")
                {
                    _pairingManager.SetDevicePointerSpeedOverride(authenticatedClientId, GetInt(root, "pointerSpeed"));
                    continue;
                }

                if (type == "custom.pointer.set")
                {
                    var next = AppPointerSettings.GetCustomPointer() with { Enabled = root.GetProperty("enabled").GetBoolean() };
                    try
                    {
                        _applyCustomPointer?.Invoke(next);
                        AppPointerSettings.SetCustomPointer(next);
                        LogCommandOutcome(authenticatedClientId, type, next.Enabled ? "enable" : "disable", "executed");
                    }
                    catch (Exception exception)
                    {
                        LogCommandOutcome(authenticatedClientId, type, next.Enabled ? "enable" : "disable", "failed");
                        _appLog.Write(new AppLogEntry(Event: "host_action", Source: "windows_host", ClientId: authenticatedClientId, Action: "custom_pointer", Outcome: "failed", Detail: exception.Message));
                    }

                    continue;
                }

                if (type == "audio.get")
                {
                    await SendAudioStateAsync(socket, authenticatedClientId, cancellationToken);
                    continue;
                }

                if (type == "system.sleep")
                {
                    if (CanSleepPc(authenticatedClientId))
                    {
                        TrySleepPc();
                    }

                    continue;
                }

                if (type == "system.power")
                {
                    await HandlePowerActionAsync(socket, authenticatedClientId, GetString(root, "action"), cancellationToken);
                    continue;
                }

                if (type == "awake.set")
                {
                    await HandleAwakeSetAsync(socket, authenticatedClientId, root.GetProperty("enabled").GetBoolean(), cancellationToken);
                    continue;
                }

                if (type == "presentation.command")
                {
                    await HandlePresentationCommandAsync(socket, authenticatedClientId, root, cancellationToken);
                    continue;
                }

                if (type == "remote.launch")
                {
                    var action = GetString(root, "action");
                    var outcome = "blocked";
                    if (CanLaunchRemoteApps(authenticatedClientId))
                    {
                        outcome = _remoteActionExecutor.TryExecute(action) ? "executed" : "failed";
                    }

                    LogCommandOutcome(authenticatedClientId, type, action, outcome);

                    continue;
                }

                if (type == "app.launch")
                {
                    await HandleAppLaunchAsync(socket, authenticatedClientId, GetString(root, "actionId"), cancellationToken);
                    continue;
                }

                if (type == "url.open")
                {
                    await HandleUrlOpenAsync(socket, authenticatedClientId, GetString(root, "operationId"), GetString(root, "url"), cancellationToken);
                    continue;
                }

                if (type == "text.send")
                {
                    await HandleTextTransferAsync(socket, authenticatedClientId, root, cancellationToken);
                    continue;
                }

                if (type == "clipboard.get")
                {
                    await HandleClipboardReadAsync(socket, authenticatedClientId, GetString(root, "operationId"), cancellationToken);
                    continue;
                }

                if (IsAudioMessage(root))
                {
                    var action = type == "audio.mute.toggle" ? "toggle_mute" : "set_volume";
                    var outcome = "blocked";
                    if (CanControlVolume(authenticatedClientId) &&
                        AudioMessageRouter.TryHandle(root, _audioController, out var audioState) &&
                        audioState is not null)
                    {
                        await SendAudioStateAsync(socket, audioState, cancellationToken);
                        outcome = "executed";
                    }

                    LogCommandOutcome(authenticatedClientId, type!, action, outcome);

                    continue;
                }

                if (inputCommand is { } command)
                {
                    await HandleInputMessageAsync(socket, command, authenticatedClientId, cancellationToken);
                    continue;
                }

                await CloseAuthenticatedSocketAsync(socket, authenticatedClientId, "Unsupported message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                break;
            }
        }
        catch (JsonException)
        {
            if (!authenticated)
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await SendUnauthenticatedSocketAsync(socket, new { type = "pair.rejected", reason = "invalid-message" }, cancellationToken);
            }
            else
            {
                await CloseAuthenticatedSocketAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            if (!string.IsNullOrEmpty(authenticatedClientId))
            {
                _connections.Unregister(authenticatedClientId, socket);
            }

            activeConnection?.Dispose();
        }
    }

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

    private static bool IsInputMessage(string? type)
    {
        return type is "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" or "keyboard.text" or "keyboard.special";
    }

    private static bool IsAudioMessage(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        return typeProperty.GetString() is "audio.mute.toggle" or "audio.volume.set";
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private Task HandleAppLaunchAsync(WebSocket socket, string clientId, string actionId, CancellationToken cancellationToken)
    {
        var result = CanLaunchRemoteApps(clientId)
            ? _appLaunchService.Execute(actionId)
            : new AppLaunchExecutionResult(false, "permission-denied", "Application launch is disabled for this device on the PC.");

        _appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "app.launch",
            Action: actionId,
            Outcome: result.Succeeded ? "succeeded" : result.Code));

        return SendSocketAsync(socket, new
        {
            type = "app.launch.result",
            actionId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    private Task HandleUrlOpenAsync(WebSocket socket, string clientId, string operationId, string url, CancellationToken cancellationToken)
    {
        var result = CanOpenUrls(clientId)
            ? _urlOpenService.Execute(url)
            : new UrlOpenExecutionResult(false, "permission-denied", "Opening web addresses is disabled for this device on the PC.");

        _appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "url.open",
            Action: "open_url",
            Outcome: result.Succeeded ? "accepted" : result.Code));

        return SendSocketAsync(socket, new
        {
            type = "url.open.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            normalizedUrl = result.NormalizedUrl
        }, cancellationToken);
    }

    private async Task CloseAuthenticatedSocketAsync(WebSocket socket, string clientId, string reason, WebSocketCloseStatus status, CancellationToken cancellationToken)
    {
        ControllerSocketClosed?.Invoke(this, new ControllerSocketClosedEventArgs(clientId, reason, status));
        await CloseSocketAsync(socket, reason, status, cancellationToken);
    }

    private static int GetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static void TrySleepPc()
    {
        try
        {
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, force: false, disableWakeEvent: false);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
