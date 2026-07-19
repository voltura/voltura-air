using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class WebSocketSessionHandler(
    PairingManager pairingManager,
    ISystemAudioController audioController,
    Action<CustomPointerSettings>? applyCustomPointer,
    HostStatusPayloadFactory statusFactory,
    HostCommandLog commandLog,
    WebSocketTransport transport,
    PowerCommandHandler powerCommands,
    AwakeCommandHandler awakeCommands,
    PresentationCommandHandler presentationCommands,
    ExternalActionCommandHandler externalActionCommands,
    TextTransferCommandHandler textTransferCommands,
    ClipboardCommandHandler clipboardCommands,
    InputCommandHandler inputCommands,
    IAppLogWriter appLog,
    Action<ControllerSocketClosedEventArgs> reportSocketClosed)
{
    public static readonly TimeSpan PairingHandshakeTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan AuthenticatedInactivityTimeout = TimeSpan.FromMinutes(2);
    private readonly PairingAttemptRateLimiter _pairingAttemptRateLimiter = new();

    public async Task HandleAsync(WebSocket socket, string rateLimitKey, CancellationToken cancellationToken)
    {
        var authenticated = false;
        var authenticatedClientId = string.Empty;
        IDisposable? activeConnection = null;
        var buffer = new byte[WebSocketTransport.MaxMessageBytes];
        using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                receiveTimeout.CancelAfter(authenticated ? AuthenticatedInactivityTimeout : PairingHandshakeTimeout);
                JsonDocument? receivedDocument;
                try
                {
                    receivedDocument = await WebSocketTransport.ReceiveAsync(socket, buffer, receiveTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await WebSocketTransport.CloseAsync(
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
                    var authentication = await TryAuthenticateAsync(socket, root, type, rateLimitKey, cancellationToken);
                    if (authentication is null)
                    {
                        continue;
                    }

                    authenticated = true;
                    authenticatedClientId = authentication.ClientId;
                    transport.Register(authentication.ClientId, socket);
                    activeConnection = pairingManager.TrackConnection(authentication.ClientId);
                    await transport.SendAsync(
                        socket,
                        statusFactory.CreatePairAccepted(authentication.ClientId, authentication.Secret),
                        cancellationToken);
                    continue;
                }

                ValidatedInputCommand? inputCommand = null;
                if (IsInputMessage(type))
                {
                    if (!ClientMessageValidator.TryDecodeInputMessage(root, type!, out var decodedInput))
                    {
                        await CloseAuthenticatedAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                        break;
                    }

                    inputCommand = decodedInput;
                }
                else if (type is null || !ClientMessageValidator.IsValidAuthenticatedMessage(root, type))
                {
                    await CloseAuthenticatedAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                    break;
                }

                commandLog.Received(authenticatedClientId, type!, root, inputCommand);
                if (!await DispatchAuthenticatedAsync(socket, authenticatedClientId, type!, root, inputCommand, cancellationToken))
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            if (!authenticated)
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await WebSocketTransport.SendUnauthenticatedAsync(
                    socket,
                    new { type = "pair.rejected", reason = "invalid-message" },
                    cancellationToken);
            }
            else
            {
                await CloseAuthenticatedAsync(socket, authenticatedClientId, "Invalid message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            if (!string.IsNullOrEmpty(authenticatedClientId))
            {
                transport.Unregister(authenticatedClientId, socket);
            }

            activeConnection?.Dispose();
        }
    }

    private async Task<AuthenticatedClient?> TryAuthenticateAsync(
        WebSocket socket,
        JsonElement root,
        string? type,
        string rateLimitKey,
        CancellationToken cancellationToken)
    {
        if (type != "pair.hello")
        {
            await RejectPairingAsync(socket, "pair-first", cancellationToken);
            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
            return null;
        }

        if (!ClientMessageValidator.TryValidatePairHello(root, out var hello))
        {
            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
            await RejectPairingAsync(socket, "invalid-message", cancellationToken);
            return null;
        }

        var isRateLimited = _pairingAttemptRateLimiter.IsBlocked(rateLimitKey);
        var result = pairingManager.Accept(
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
                await RejectPairingAsync(socket, "rate-limited", cancellationToken);
            }
            else
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await RejectPairingAsync(socket, result.Reason, cancellationToken);
            }

            return null;
        }

        _pairingAttemptRateLimiter.Reset(rateLimitKey);
        var secret = result.Secret ?? pairingManager.RotateSecret(hello.ClientId, hello.DeviceName);
        return new AuthenticatedClient(hello.ClientId, secret);
    }

    private async Task<bool> DispatchAuthenticatedAsync(
        WebSocket socket,
        string clientId,
        string type,
        JsonElement root,
        ValidatedInputCommand? inputCommand,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case "pair.disconnect":
                pairingManager.DisconnectDevice(clientId);
                return false;
            case "device.rename":
                pairingManager.RenameDevice(clientId, ProtocolMessageFields.GetString(root, "deviceName"));
                return true;
            case "health.ping":
                await transport.SendAsync(socket, new { type = "health.pong" }, cancellationToken);
                return true;
            case "status.get":
                await transport.SendAsync(socket, statusFactory.CreateConnectedStatus(clientId), cancellationToken);
                return true;
            case "pointer.speed.set":
                pairingManager.SetDevicePointerSpeedOverride(clientId, ProtocolMessageFields.GetInt(root, "pointerSpeed"));
                return true;
            case "custom.pointer.set":
                ApplyCustomPointer(clientId, root.GetProperty("enabled").GetBoolean());
                return true;
            case "audio.get":
                await SendAudioStateAsync(socket, clientId, cancellationToken);
                return true;
            case "system.sleep":
                if (statusFactory.CanSleepPc(clientId))
                {
                    TrySleepPc();
                }

                return true;
            case "system.power":
                await powerCommands.HandleAsync(socket, clientId, ProtocolMessageFields.GetString(root, "operationId"), ProtocolMessageFields.GetString(root, "action"), cancellationToken);
                return true;
            case "awake.set":
                await awakeCommands.HandleAsync(socket, clientId, ProtocolMessageFields.GetString(root, "operationId"), root.GetProperty("enabled").GetBoolean(), cancellationToken);
                return true;
            case "presentation.command":
                await presentationCommands.HandleAsync(socket, clientId, root, cancellationToken);
                return true;
            case "remote.launch":
                await externalActionCommands.HandleRemoteLaunchAsync(clientId, ProtocolMessageFields.GetString(root, "action"), cancellationToken);
                return true;
            case "app.launch":
                await externalActionCommands.HandleAppLaunchAsync(socket, clientId, ProtocolMessageFields.GetString(root, "actionId"), cancellationToken);
                return true;
            case "url.open":
                await externalActionCommands.HandleUrlOpenAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "url"),
                    cancellationToken);
                return true;
            case "text.send":
                await textTransferCommands.HandleAsync(socket, clientId, root, cancellationToken);
                return true;
            case "clipboard.get":
                await clipboardCommands.HandleAsync(socket, clientId, ProtocolMessageFields.GetString(root, "operationId"), cancellationToken);
                return true;
        }

        if (IsAudioMessage(type))
        {
            await HandleAudioMessageAsync(socket, clientId, type, root, cancellationToken);
            return true;
        }

        if (inputCommand is { } command)
        {
            if (await inputCommands.HandleAsync(socket, command, clientId, cancellationToken))
            {
                return true;
            }

            await CloseAuthenticatedAsync(socket, clientId, "Unsupported input message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
            return false;
        }

        await CloseAuthenticatedAsync(socket, clientId, "Unsupported message", WebSocketCloseStatus.PolicyViolation, cancellationToken);
        return false;
    }

    private void ApplyCustomPointer(string clientId, bool enabled)
    {
        var next = AppPointerSettings.GetCustomPointer() with { Enabled = enabled };
        try
        {
            applyCustomPointer?.Invoke(next);
            AppPointerSettings.SetCustomPointer(next);
            commandLog.Outcome(clientId, "custom.pointer.set", enabled ? "enable" : "disable", "executed");
        }
        catch (Exception exception)
        {
            commandLog.Outcome(clientId, "custom.pointer.set", enabled ? "enable" : "disable", "failed");
            appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                ClientId: clientId,
                Action: "custom_pointer",
                Outcome: "failed",
                Detail: exception.Message));
        }
    }

    private async Task HandleAudioMessageAsync(
        WebSocket socket,
        string clientId,
        string type,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var action = type == "audio.mute.toggle" ? "toggle_mute" : "set_volume";
        var outcome = "blocked";
        if (statusFactory.CanControlVolume(clientId) &&
            AudioMessageRouter.TryHandle(root, audioController, out var audioState) &&
            audioState is not null)
        {
            await SendAudioStateAsync(socket, audioState, cancellationToken);
            outcome = "executed";
        }

        commandLog.Outcome(clientId, type, action, outcome);
    }

    private Task SendAudioStateAsync(WebSocket socket, string clientId, CancellationToken cancellationToken)
    {
        if (!statusFactory.CanControlVolume(clientId))
        {
            return Task.CompletedTask;
        }

        var state = AudioMessageRouter.TryGetState(audioController);
        return state is null ? Task.CompletedTask : SendAudioStateAsync(socket, state, cancellationToken);
    }

    private Task SendAudioStateAsync(WebSocket socket, AudioState state, CancellationToken cancellationToken) =>
        transport.SendAsync(socket, new { type = "audio.state", volume = state.Volume, muted = state.Muted }, cancellationToken);

    private async Task CloseAuthenticatedAsync(
        WebSocket socket,
        string clientId,
        string reason,
        WebSocketCloseStatus status,
        CancellationToken cancellationToken)
    {
        reportSocketClosed(new ControllerSocketClosedEventArgs(clientId, reason, status));
        await WebSocketTransport.CloseAsync(socket, reason, status, cancellationToken);
    }

    private static Task RejectPairingAsync(WebSocket socket, string reason, CancellationToken cancellationToken) =>
        WebSocketTransport.SendUnauthenticatedAsync(socket, new { type = "pair.rejected", reason }, cancellationToken);

    private static bool IsInputMessage(string? type) =>
        type is "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" or "keyboard.text" or "keyboard.special";

    private static bool IsAudioMessage(string type) => type is "audio.mute.toggle" or "audio.volume.set";

    private static void TrySleepPc()
    {
        try
        {
            System.Windows.Forms.Application.SetSuspendState(
                System.Windows.Forms.PowerState.Suspend,
                force: false,
                disableWakeEvent: false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record AuthenticatedClient(string ClientId, string Secret);
}
