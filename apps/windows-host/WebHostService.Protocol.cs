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

}
