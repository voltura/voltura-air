using System.Net.WebSockets;
using System.Text;
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

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(socket, buffer, cancellationToken);
                if (message is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(message);
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
                    RegisterSocket(clientId, socket);
                    activeConnection = _pairingManager.TrackConnection(clientId);
                    var pcName = Environment.MachineName;
                    var capabilities = CreateCapabilities(clientId);
                    await SendSocketAsync(socket, new { type = "pair.accepted", clientId, pcName, secret, paired = true, capabilities, host = CreateHostStatus(clientId) }, cancellationToken);
                    continue;
                }

                if (!ClientMessageValidator.IsValidAuthenticatedMessage(root))
                {
                    await CloseSocketAsync(socket, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                    break;
                }

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

                if (type == "remote.launch")
                {
                    if (CanLaunchRemoteApps(authenticatedClientId))
                    {
                        _remoteActionExecutor.TryExecute(GetString(root, "action"));
                    }

                    continue;
                }

                if (IsAudioMessage(root))
                {
                    if (CanControlVolume(authenticatedClientId) &&
                        AudioMessageRouter.TryHandle(root, _audioController, out var audioState) &&
                        audioState is not null)
                    {
                        await SendAudioStateAsync(socket, audioState, cancellationToken);
                    }

                    continue;
                }

                if (IsInputMessage(type))
                {
                    await HandleInputMessageAsync(socket, root, authenticatedClientId, cancellationToken);
                    continue;
                }

                await CloseSocketAsync(socket, "Unsupported message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
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
                await CloseSocketAsync(socket, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
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

    private async Task HandleInputMessageAsync(WebSocket socket, JsonElement root, string clientId, CancellationToken cancellationToken)
    {
        var sequence = TryGetInputSequence(root, out var parsedSequence) ? parsedSequence : (long?)null;

        try
        {
            if (!_inputDispatcher.Dispatch(root))
            {
                await SendInputErrorAsync(socket, sequence, "VAIR-INPUT-UNSUPPORTED", "Unsupported input message.", cancellationToken);
                await CloseSocketAsync(socket, "Unsupported input message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                return;
            }

            await SendInputAckAsync(socket, sequence, cancellationToken);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            const string message = "Windows did not accept input events.";
            await SendInputErrorAsync(socket, sequence, "VAIR-INPUT-DISPATCH-FAILED", message, cancellationToken);
            await SendDisconnectedStatusAsync(socket, clientId, $"{message} Retrying...", cancellationToken);
            await CloseSocketAsync(socket, "Input dispatch failed", WebSocketCloseStatus.InternalServerError, cancellationToken);
        }
    }

    private void RegisterSocket(string clientId, WebSocket socket)
    {
        _connections.Register(clientId, socket);
    }

    private void UnregisterSocket(string clientId, WebSocket socket)
    {
        _connections.Unregister(clientId, socket);
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

    private static async Task<string?> ReceiveMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseSocketAsync(socket, "Closing", cancellationToken);
                return null;
            }

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
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
            volume = CanControlVolume(clientId),
            remoteLaunch = CanLaunchRemoteApps(clientId),
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
