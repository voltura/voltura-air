using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private async Task HandleSocketAsync(WebSocket socket, string rateLimitKey, CancellationToken cancellationToken)
    {
        var authenticated = false;
        var authenticatedClientId = string.Empty;
        PointerHighlightConnectionState? pointerHighlightState = null;
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
                    using var closeTimeout = new CancellationTokenSource(WebSocketCloseTimeout);
                    await CloseSocketAsync(
                        socket,
                        authenticated ? "Connection timed out" : "Pairing timed out",
                        WebSocketCloseStatus.EndpointUnavailable,
                        closeTimeout.Token);
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
                        await SendSocketAsync(socket, new { type = "pair.rejected", reason = "pair-first" }, cancellationToken);
                        _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                        continue;
                    }

                    if (!ClientMessageValidator.TryValidatePairHello(root, out var hello))
                    {
                        _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                        await SendSocketAsync(socket, new { type = "pair.rejected", reason = "invalid-message" }, cancellationToken);
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
                            await SendSocketAsync(socket, new { type = "pair.rejected", reason = "rate-limited" }, cancellationToken);
                        }
                        else
                        {
                            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                            await SendSocketAsync(socket, new { type = "pair.rejected", reason = result.Reason }, cancellationToken);
                        }

                        continue;
                    }

                    _pairingAttemptRateLimiter.Reset(rateLimitKey);
                    var clientId = hello.ClientId;
                    var deviceName = hello.DeviceName;
                    var secret = result.Secret ?? _pairingManager.RotateSecret(clientId, deviceName);
                    authenticated = true;
                    authenticatedClientId = clientId;
                    pointerHighlightState = RegisterSocket(clientId, socket);
                    activeConnection = _pairingManager.TrackConnection(clientId);
                    var pcName = Environment.MachineName;
                    var capabilities = CreateCapabilities(clientId);
                    await SendSocketAsync(socket, new { type = "pair.accepted", clientId, pcName, secret, paired = true, capabilities, host = CreateHostStatus(clientId) }, cancellationToken);
                    continue;
                }

                if (!ClientMessageValidator.IsValidAuthenticatedMessage(root))
                {
                    await CloseAuthenticatedSocketAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                    break;
                }

                LogReceivedClientCommand(authenticatedClientId, type!, root);

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

                if (type == "pointer.highlight.set")
                {
                    HandlePointerHighlightSet(authenticatedClientId, root, pointerHighlightState!);
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

                if (type == "text.send")
                {
                    await HandleTextTransferAsync(socket, authenticatedClientId, root, cancellationToken);
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

                if (IsInputMessage(type))
                {
                    await HandleInputMessageAsync(socket, root, authenticatedClientId, pointerHighlightState!.Enabled, cancellationToken);
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
                await SendSocketAsync(socket, new { type = "pair.rejected", reason = "invalid-message" }, cancellationToken);
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
                UnregisterSocket(authenticatedClientId, socket);
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

        _ = Task.Run(() => CloseSocketsAsync(sockets));
    }

    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        RefreshPointerHighlightStates();
        _ = Task.Run(BroadcastStatusAsync);
    }

    private async Task BroadcastStatusAsync()
    {
        var sockets = _connections.Snapshot();

        foreach (var (clientId, socket) in sockets)
        {
            try
            {
                await SendConnectedStatusAsync(socket, clientId, CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task CloseSocketsAsync(IEnumerable<WebSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            try
            {
                await CloseSocketAsync(socket, "Device disconnected", CancellationToken.None);
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
        stream.Write(buffer, 0, result.Count);
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

            stream.Write(buffer, 0, result.Count);
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
            await socket.CloseAsync(status, reason, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task SendSocketAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var sendGate = _connections.GetSendGate(socket);
        if (sendGate is null)
        {
            await SendAsync(socket, payload, cancellationToken);
            return;
        }

        await sendGate.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(socket, payload, cancellationToken);
        }
        finally
        {
            sendGate.Release();
        }
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

    private object CreateCapabilities(string clientId)
    {
        return new
        {
            sleep = CanSleepPc(clientId),
            power = CreatePowerCapabilities(clientId),
            awake = CreateAwakeCapability(clientId),
            volume = CanControlVolume(clientId),
            remoteLaunch = CanLaunchRemoteApps(clientId),
            textTransfer = true,
            gestureDebug = AppDeveloperSettings.EnableGestureDebug(),
            inputAck = true
        };
    }

    private bool CanSleepPc(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowPcSleep;
    }

    private bool CanControlVolume(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowVolumeControl;
    }

    private bool CanLaunchRemoteApps(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowRemoteAppLaunch;
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

    private static bool TryGetInputSequence(JsonElement root, out long sequence)
    {
        sequence = 0;
        return root.TryGetProperty("seq", out var sequenceProperty) &&
            sequenceProperty.ValueKind == JsonValueKind.Number &&
            sequenceProperty.TryGetInt64(out sequence) &&
            sequence > 0;
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

    private async Task CloseAuthenticatedSocketAsync(WebSocket socket, string clientId, string reason, WebSocketCloseStatus status, CancellationToken cancellationToken)
    {
        ControllerSocketClosed?.Invoke(this, new ControllerSocketClosedEventArgs(clientId, reason, status));
        await CloseSocketAsync(socket, reason, status, cancellationToken);
    }

    private static string GetModifierDiagnostic(JsonElement root)
    {
        if (!root.TryGetProperty("modifiers", out var modifiers) || modifiers.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join("+", modifiers.EnumerateArray().Select(modifier => modifier.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)));
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
