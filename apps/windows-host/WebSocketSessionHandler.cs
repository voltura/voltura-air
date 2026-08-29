using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using VolturaAir.Host.Features.PhoneWebcam;
using VolturaAir.Host.Features.Diagnostics;
using VolturaAir.Host.Features.UsageTelemetry;
using VolturaAir.Host.Features.AiAssistant;

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
    PowerPointPresentationLaunchHandler presentationLauncher,
    PresentationReportCommandHandler presentationReports,
    PresentationSessionCommandHandler presentationSessions,
    ExternalActionCommandHandler externalActionCommands,
    TextTransferCommandHandler textTransferCommands,
    ClipboardCommandHandler clipboardCommands,
    DiagnosticsCommandHandler diagnosticsCommands,
    FileManagerCommandHandler fileManagerCommands,
    FileTransferCoordinator fileTransfers,
    TerminalCoordinator terminal,
    AiAssistantCoordinator aiAssistant,
    InputCommandHandler inputCommands,
    CustomScreenCommandHandler customScreenCommands,
    ScreenViewCommandHandler screenViewCommands,
    PhoneWebcamCommandHandler phoneWebcamCommands,
    IUsageTelemetryRecorder usageTelemetry,
    IAppLogWriter appLog,
    Action<ControllerSocketClosedEventArgs> reportSocketClosed)
{
    public static readonly TimeSpan PairingHandshakeTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan AuthenticatedInactivityTimeout = TimeSpan.FromMinutes(5);
    private readonly PairingAttemptRateLimiter _pairingAttemptRateLimiter = new();

    internal event EventHandler? StatusRefreshRequested;

    public async Task HandleAsync(
        WebSocket socket,
        string rateLimitKey,
        UsageConnectionMethod connectionMethod,
        Func<IDisposable?>? acquireAuthenticatedAdmission = null,
        Action? authenticatedCallback = null,
        CancellationToken cancellationToken = default)
    {
        var authenticated = false;
        var authenticatedClientId = string.Empty;
        var authenticatedPairingEpoch = 0L;
        PendingReconnect? pendingReconnect = null;
        PairingBootstrapPending? pendingPairing = null;
        IDisposable? activeConnection = null;
        IDisposable? authenticatedAdmission = null;
        using var usageSession = new UsageTelemetrySession(usageTelemetry);
        var buffer = new byte[WebSocketTransport.MaxMessageBytes];
        using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                receiveTimeout.CancelAfter(
                    authenticated
                        ? socket is SecureDirectWebSocket
                            ? Timeout.InfiniteTimeSpan
                            : AuthenticatedInactivityTimeout
                        : PairingHandshakeTimeout);
                JsonDocument? receivedDocument;
                try
                {
                    receivedDocument = await WebSocketTransport.ReceiveAsync(socket, buffer, receiveTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var timeoutClientId = authenticated
                        ? authenticatedClientId
                        : pendingReconnect?.ClientId ?? pendingPairing?.ClientId;
                    LogConnectionTimeout(socket, authenticated, timeoutClientId);
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

                if (authenticated && !pairingManager.IsCurrentPairing(authenticatedClientId, authenticatedPairingEpoch))
                {
                    await CloseAuthenticatedAsync(socket, authenticatedClientId, "Device disconnected", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                    break;
                }

                if (!authenticated)
                {
                    var reconnectAttempt = pendingReconnect;
                    var pairingAttempt = pendingPairing;
                    if (type == "pair.proof")
                    {
                        pendingReconnect = null;
                    }
                    else if (type == "pair.bootstrap.proof")
                    {
                        pendingPairing = null;
                    }

                    var authentication = await TryAuthenticateAsync(socket, root, type, rateLimitKey, reconnectAttempt, pairingAttempt, cancellationToken);
                    if (authentication is null)
                    {
                        continue;
                    }

                    if (authentication.PendingReconnect is { } nextReconnect)
                    {
                        pendingReconnect = nextReconnect;
                        await WebSocketTransport.SendUnauthenticatedAsync(
                            socket,
                            new { type = "pair.challenge", clientId = nextReconnect.ClientId, challenge = nextReconnect.Challenge },
                            cancellationToken);
                        continue;
                    }

                    if (authentication.PendingPairing is { } nextPairing)
                    {
                        pendingPairing = nextPairing;
                        await WebSocketTransport.SendUnauthenticatedAsync(
                            socket,
                            new
                            {
                                type = "pair.bootstrap.challenge",
                                clientId = nextPairing.ClientId,
                                clientNonce = nextPairing.ClientNonce,
                                serverNonce = nextPairing.ServerNonce,
                                hostIdentity = new { publicKey = nextPairing.HostPublicKey, fingerprint = nextPairing.HostFingerprint },
                                proof = nextPairing.HostProof
                            },
                            cancellationToken);
                        continue;
                    }

                    bool relayEncryptionEstablished;
                    try
                    {
                        relayEncryptionEstablished = socket is not RelayVirtualWebSocket relaySocket ||
                            await EstablishRelayEncryptionAsync(
                                relaySocket,
                                authentication.ClientId,
                                authentication.PendingPairingCommit?.ReconnectPublicKey,
                                buffer,
                                receiveTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        LogConnectionTimeout(socket, authenticated: false, authentication.ClientId);
                        await WebSocketTransport.CloseAsync(
                            socket,
                            "Pairing timed out",
                            WebSocketCloseStatus.EndpointUnavailable,
                            cancellationToken);
                        break;
                    }

                    if (!relayEncryptionEstablished)
                    {
                        await RejectPairingAsync(socket, "relay-encryption-failed", cancellationToken);
                        break;
                    }

                    if (authentication.PendingPairingCommit is { } pairingToCommit)
                    {
                        var commit = pairingManager.CommitPairingBootstrap(pairingToCommit);
                        if (!commit.Accepted)
                        {
                            await RejectPairingAsync(socket, commit.Reason, cancellationToken);
                            break;
                        }
                    }

                    authenticatedAdmission = acquireAuthenticatedAdmission?.Invoke();
                    if (acquireAuthenticatedAdmission is not null && authenticatedAdmission is null)
                    {
                        await RejectPairingAsync(socket, "session-limit", cancellationToken);
                        break;
                    }

                    if (!pairingManager.TryTrackConnection(
                        authentication.ClientId,
                        () => transport.Register(authentication.ClientId, socket),
                        out activeConnection,
                        out authenticatedPairingEpoch))
                    {
                        authenticatedAdmission?.Dispose();
                        authenticatedAdmission = null;
                        await RejectPairingAsync(socket, "device-revoked", cancellationToken);
                        break;
                    }

                    authenticated = true;
                    authenticatedClientId = authentication.ClientId;
                    authenticatedCallback?.Invoke();
                    if (usageTelemetry.SessionRegistry is { } sessionRegistry)
                    {
                        _ = usageSession.TryRegister(sessionRegistry);
                    }
                    var connectionRecordingToken = usageTelemetry.CurrentRecordingToken;
                    await transport.SendAsync(
                        socket,
                        statusFactory.CreatePairAccepted(authentication.ClientId),
                        cancellationToken);
                    _ = usageTelemetry.TryRecordConnection(connectionMethod, connectionRecordingToken);
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

                if (!pairingManager.IsCurrentPairing(authenticatedClientId, authenticatedPairingEpoch))
                {
                    await CloseAuthenticatedAsync(socket, authenticatedClientId, "Device disconnected", WebSocketCloseStatus.PolicyViolation, cancellationToken);
                    break;
                }

                commandLog.Received(authenticatedClientId, type!, root, inputCommand);
                var recordingToken = usageTelemetry.CurrentRecordingToken;
                if (!await DispatchAuthenticatedAsync(
                    socket,
                    authenticatedClientId,
                    type!,
                    root,
                    inputCommand,
                    usageSession,
                    recordingToken,
                    cancellationToken))
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
            try
            {
                if (!string.IsNullOrEmpty(authenticatedClientId))
                {
                    aiAssistant.ClientDisconnected(authenticatedClientId, socket);
                    await screenViewCommands.ClientDisconnectedAsync(authenticatedClientId);
                    await phoneWebcamCommands.ClientDisconnectedAsync(authenticatedClientId, socket);
                    fileManagerCommands.ClientDisconnected(authenticatedClientId, socket);
                    fileTransfers.ClientDisconnected(authenticatedClientId, socket);
                    terminal.ClientDisconnected(authenticatedClientId, socket);
                    transport.Unregister(authenticatedClientId, socket);
                    presentationCommands.DisableLaserForClient(authenticatedClientId);
                }
            }
            finally
            {
                activeConnection?.Dispose();
                authenticatedAdmission?.Dispose();
            }
        }
    }

    private async Task<AuthenticatedClient?> TryAuthenticateAsync(
        WebSocket socket,
        JsonElement root,
        string? type,
        string rateLimitKey,
        PendingReconnect? pendingReconnect,
        PairingBootstrapPending? pendingPairing,
        CancellationToken cancellationToken)
    {
        if (type != "pair.hello" && type != "pair.proof" && type != "pair.bootstrap.proof")
        {
            await RejectPairingAsync(socket, "pair-first", cancellationToken);
            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
            return null;
        }

        if (type == "pair.proof")
        {
            if (pendingReconnect is null ||
                !ClientMessageValidator.TryValidatePairProof(root, out var proof) ||
                !string.Equals(proof.ClientId, pendingReconnect.ClientId, StringComparison.Ordinal))
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await RejectPairingAsync(socket, "invalid-message", cancellationToken);
                return null;
            }

            var proofResult = pairingManager.AcceptReconnectProof(
                proof.ClientId,
                pendingReconnect.Challenge,
                proof.Signature,
                pendingReconnect.DeviceName,
                pendingReconnect.Platform,
                pendingReconnect.Browser,
                pendingReconnect.DisplayMode);
            if (!proofResult.Accepted)
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await RejectPairingAsync(socket, proofResult.Reason, cancellationToken);
                return null;
            }

            _pairingAttemptRateLimiter.Reset(rateLimitKey);
            return new AuthenticatedClient(proof.ClientId, null, null);
        }

        if (type == "pair.bootstrap.proof")
        {
            if (pendingPairing is null ||
                !ClientMessageValidator.TryValidatePairBootstrapProof(root, out var proof) ||
                !string.Equals(proof.ClientId, pendingPairing.ClientId, StringComparison.Ordinal))
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await RejectPairingAsync(socket, "invalid-message", cancellationToken);
                return null;
            }

            var proofResult = socket is RelayVirtualWebSocket
                ? PairingManager.VerifyPairingBootstrap(pendingPairing, proof.Proof)
                : pairingManager.CompletePairingBootstrap(pendingPairing, proof.Proof);
            if (!proofResult.Accepted)
            {
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                await RejectPairingAsync(socket, proofResult.Reason, cancellationToken);
                return null;
            }

            _pairingAttemptRateLimiter.Reset(rateLimitKey);
            return new AuthenticatedClient(
                proof.ClientId,
                null,
                null,
                socket is RelayVirtualWebSocket ? pendingPairing : null);
        }

        if (!ClientMessageValidator.TryValidatePairHello(root, out var hello))
        {
            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
            await RejectPairingAsync(socket, "invalid-message", cancellationToken);
            return null;
        }

        var isRateLimited = _pairingAttemptRateLimiter.IsBlocked(rateLimitKey);
        if (hello.PairTokenId is null)
        {
            if (hello.ClientNonce is not null || hello.ReconnectPublicKey is not null)
            {
                await RejectPairingAsync(socket, "invalid-message", cancellationToken);
                _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
                return null;
            }

            var challenge = pairingManager.CreateReconnectChallenge(hello.ClientId);
            if (challenge is not null)
            {
                return new AuthenticatedClient(
                    hello.ClientId,
                    new PendingReconnect(
                        hello.ClientId,
                        challenge,
                        hello.DeviceName,
                        hello.Platform,
                        hello.Browser,
                        hello.DisplayMode),
                    null);
            }

            await RejectPairingAsync(socket, "device-revoked", cancellationToken);
            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
            return null;
        }

        if (hello.ReconnectPublicKey is null || hello.ClientNonce is null)
        {
            await RejectPairingAsync(socket, "invalid-message", cancellationToken);
            _pairingAttemptRateLimiter.RecordFailure(rateLimitKey);
            return null;
        }

        var result = pairingManager.BeginPairingBootstrap(
            hello.ClientId,
            hello.DeviceName,
            hello.PairTokenId,
            hello.ClientNonce,
            hello.ReconnectPublicKey,
            now: null,
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

        return new AuthenticatedClient(hello.ClientId, null, result.Pending, null);
    }

    private async Task<bool> EstablishRelayEncryptionAsync(
        RelayVirtualWebSocket socket,
        string clientId,
        string? freshReconnectPublicKey,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var hostKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var hostEphemeral = RelaySessionCrypto.ExportPublicKey(hostKey);
            var nonce = ScreenViewHostIdentity.Base64Url(RandomNumberGenerator.GetBytes(32));
            await WebSocketTransport.SendUnauthenticatedAsync(socket, new
            {
                type = "session.key.challenge",
                routeId = socket.RouteId,
                clientId,
                hostEphemeralPublicKey = hostEphemeral,
                nonce
            }, cancellationToken);

            using var response = await WebSocketTransport.ReceiveAsync(socket, buffer, cancellationToken);
            if (response is null || response.RootElement.ValueKind != JsonValueKind.Object ||
                !response.RootElement.TryGetProperty("type", out var type) || type.GetString() != "session.key.proof" ||
                !response.RootElement.TryGetProperty("clientEphemeralPublicKey", out var clientKeyValue) ||
                clientKeyValue.GetString() is not { Length: 87 } clientKey ||
                !response.RootElement.TryGetProperty("signature", out var signatureValue) ||
                signatureValue.GetString() is not { Length: 86 } signature ||
                response.RootElement.EnumerateObject().Count() != 3)
            {
                return false;
            }

            var transcript = RelaySessionCrypto.CreateTranscript(
                socket.RouteId,
                clientId,
                pairingManager.HostIdentity.PublicKey,
                hostEphemeral,
                clientKey,
                nonce);
            var signatureValid = freshReconnectPublicKey is null
                ? pairingManager.VerifyClientSignature(clientId, transcript, signature)
                : PairingManager.VerifyPublicKeySignature(freshReconnectPublicKey, transcript, signature);
            if (!signatureValid) return false;

#pragma warning disable CA2000 // RelayVirtualWebSocket owns the crypto after EnableEncryption.
            var crypto = RelaySessionCrypto.CreateHost(hostKey, clientKey, transcript);
#pragma warning restore CA2000
            await WebSocketTransport.SendUnauthenticatedAsync(socket, new
            {
                type = "session.key.accepted",
                signature = pairingManager.HostIdentity.Sign(transcript)
            }, cancellationToken);
            socket.EnableEncryption(crypto);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException or WebSocketException)
        {
            return false;
        }
    }

    private async Task<bool> DispatchAuthenticatedAsync(
        WebSocket socket,
        string clientId,
        string type,
        JsonElement root,
        ValidatedInputCommand? inputCommand,
        UsageTelemetrySession usageSession,
        UsageTelemetryRecordingToken recordingToken,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case "pair.disconnect":
                transport.DetachFromRevocation(clientId, socket);
                pairingManager.DisconnectDevice(clientId);
                await transport.SendAsync(socket, new { type = "pair.disconnect.accepted" }, cancellationToken);
                await WebSocketTransport.CloseAsync(socket, "Device disconnected", cancellationToken);
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
            case "appearance.mode-buttons.set":
                pairingManager.SetDeviceShowModeButtonsOverride(clientId, root.GetProperty("showModeButtons").GetBoolean());
                return true;
            case "appearance.control-depth.set":
                pairingManager.SetDeviceControlDepthOverride(clientId, root.GetProperty("controlDepth").GetBoolean());
                return true;
            case "appearance.accent-color.set":
                var accentColor = root.GetProperty("accentColor");
                pairingManager.SetDeviceAccentColorOverride(
                    clientId,
                    accentColor.ValueKind == JsonValueKind.Null ? null : accentColor.GetString());
                return true;
            case "custom.pointer.set":
                ApplyCustomPointer(clientId, root.GetProperty("enabled").GetBoolean());
                return true;
            case "device.viewport.set":
                pairingManager.SetCustomScreenViewport(
                    clientId,
                    new CustomScreenViewport(
                        ProtocolMessageFields.GetInt(root, "width"),
                        ProtocolMessageFields.GetInt(root, "height"),
                        ProtocolMessageFields.GetString(root, "orientation")));
                return true;
            case "custom.screen.get":
                if (usageSession.NeedsRecord(UsageFeature.CustomScreens, recordingToken) &&
                    statusFactory.CanUseCustomScreen(clientId, ProtocolMessageFields.GetString(root, "screenId")))
                {
                    usageSession.RecordOnce(UsageFeature.CustomScreens, recordingToken);
                }
                await customScreenCommands.GetAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "screenId"),
                    cancellationToken);
                return true;
            case "custom.screen.invoke":
                if (usageSession.NeedsRecord(UsageFeature.CustomScreens, recordingToken) &&
                    statusFactory.CanUseCustomScreen(clientId, ProtocolMessageFields.GetString(root, "screenId")))
                {
                    usageSession.RecordOnce(UsageFeature.CustomScreens, recordingToken);
                }
                await customScreenCommands.InvokeAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "screenId"),
                    ProtocolMessageFields.GetString(root, "screenRevision"),
                    ProtocolMessageFields.GetString(root, "buttonId"),
                    root.TryGetProperty("enabled", out var customScreenEnabled)
                        ? customScreenEnabled.GetBoolean()
                        : null,
                    cancellationToken);
                return true;
            case "screen.view.sources.get":
                await screenViewCommands.GetSourcesAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    cancellationToken);
                return true;
            case "screen.view.start":
                if (usageSession.NeedsRecord(UsageFeature.ScreenViewing, recordingToken) &&
                    statusFactory.CanViewScreen(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.ScreenViewing, recordingToken);
                }
                await screenViewCommands.StartAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "displayId"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "screen.view.answer":
                await screenViewCommands.AnswerAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "answerSdp"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "screen.view.quality":
                screenViewCommands.ReportQuality(
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    new ScreenViewReceiverQuality(
                        root.GetProperty("width").GetInt32(),
                        root.GetProperty("height").GetInt32(),
                        root.GetProperty("framesPerSecond").GetDouble(),
                        root.GetProperty("framesDecoded").GetInt32(),
                        root.GetProperty("framesDropped").GetInt32(),
                        root.GetProperty("freezeCount").GetInt32(),
                        root.GetProperty("packetsLost").GetInt32()));
                return true;
            case "screen.view.source.set":
                await screenViewCommands.SetSourceAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "displayId"),
                    cancellationToken);
                return true;
            case "screen.view.stop":
                await screenViewCommands.StopAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    cancellationToken);
                return true;
            case "phone.webcam.start":
                if (usageSession.NeedsRecord(UsageFeature.PhoneWebcam, recordingToken) &&
                    statusFactory.CanUsePhoneWebcam(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.PhoneWebcam, recordingToken);
                }
                await phoneWebcamCommands.StartAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    root.GetProperty("captureWidth").GetInt32(),
                    root.GetProperty("captureHeight").GetInt32(),
                    root.GetProperty("captureFps").GetInt32(),
                    root.GetProperty("useMicrophone").GetBoolean(),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "phone.webcam.answer":
                await phoneWebcamCommands.AnswerAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "answerSdp"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "phone.webcam.stop":
                await phoneWebcamCommands.StopAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    cancellationToken);
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
                if (usageSession.NeedsRecord(UsageFeature.Presentation, recordingToken) &&
                    statusFactory.CanControlPresentations(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.Presentation, recordingToken);
                }
                await presentationCommands.HandleAsync(socket, clientId, root, cancellationToken);
                return true;
            case "presentation.powerpoint.refresh":
                if (usageSession.NeedsRecord(UsageFeature.Presentation, recordingToken) &&
                    statusFactory.CanControlPresentations(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.Presentation, recordingToken);
                }
                await presentationCommands.HandlePowerPointRefreshAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    cancellationToken);
                return true;
            case "presentation.powerpoint.launch":
                if (usageSession.NeedsRecord(UsageFeature.Presentation, recordingToken) &&
                    statusFactory.CanControlPresentations(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.Presentation, recordingToken);
                }
                await presentationLauncher.HandleAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "presentationId"),
                    cancellationToken);
                return true;
            case "presentation.report.save":
                if (usageSession.NeedsRecord(UsageFeature.Presentation, recordingToken) &&
                    statusFactory.CanControlPresentations(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.Presentation, recordingToken);
                }
                await presentationReports.HandleAsync(socket, clientId, root, cancellationToken);
                return true;
            case "presentation.session":
                if (usageSession.NeedsRecord(UsageFeature.Presentation, recordingToken) &&
                    statusFactory.CanControlPresentations(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.Presentation, recordingToken);
                }
                await presentationSessions.HandleAsync(socket, clientId, root, cancellationToken);
                return true;
            case "remote.launch":
                await externalActionCommands.HandleRemoteLaunchAsync(clientId, ProtocolMessageFields.GetString(root, "action"), cancellationToken);
                return true;
            case "app.launch":
                await externalActionCommands.HandleAppLaunchAsync(socket, clientId, ProtocolMessageFields.GetString(root, "operationId"), ProtocolMessageFields.GetString(root, "actionId"), cancellationToken);
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
            case "diagnostics.get":
                await diagnosticsCommands.HandleAsync(socket, clientId, ProtocolMessageFields.GetString(root, "operationId"), cancellationToken);
                return true;
            case "file.session.open":
                if (usageSession.NeedsRecord(UsageFeature.Files, recordingToken) &&
                    statusFactory.CanBrowseFiles(clientId))
                {
                    usageSession.RecordOnce(UsageFeature.Files, recordingToken);
                }
                await fileManagerCommands.HandleAsync(socket, clientId, type, root, cancellationToken);
                return true;
            case "file.page.get":
            case "file.navigate":
            case "file.refresh":
            case "file.sort":
            case "file.properties.get":
            case "file.clipboard.set":
            case "file.open":
            case "file.jobs.get":
            case "file.job.create":
            case "file.job.control":
            case "file.job.reorder":
            case "file.job.conflict.resolve":
                await fileManagerCommands.HandleAsync(socket, clientId, type, root, cancellationToken);
                return true;
            case "file.transfer.start":
                await fileTransfers.StartAsync(socket, clientId, root, cancellationToken);
                return true;
            case "file.transfer.answer":
                await fileTransfers.AnswerAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "transferId"),
                    ProtocolMessageFields.GetString(root, "answerSdp"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "file.transfer.cancel":
                await fileTransfers.CancelAsync(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetOptionalString(root, "transferId"),
                    ProtocolMessageFields.GetOptionalString(root, "requestId"),
                    cancellationToken);
                return true;
            case "terminal.start":
                terminal.QueueStart(socket, clientId, root, cancellationToken);
                return true;
            case "terminal.attach":
                terminal.QueueAttach(socket, clientId, root, cancellationToken);
                return true;
            case "terminal.answer":
                terminal.QueueAnswer(socket, clientId, root, cancellationToken);
                return true;
            case "terminal.stop":
                terminal.QueueStop(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "terminalId"),
                    cancellationToken);
                return true;
            case "ai.assistant.open":
                aiAssistant.QueueOpen(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "ai.assistant.ask":
                aiAssistant.QueueAsk(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "question"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "ai.assistant.reset":
                aiAssistant.QueueReset(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    ProtocolMessageFields.GetString(root, "clientSignature"),
                    cancellationToken);
                return true;
            case "ai.assistant.close":
                aiAssistant.QueueClose(
                    socket,
                    clientId,
                    ProtocolMessageFields.GetString(root, "operationId"),
                    cancellationToken);
                return true;
        }

        if (IsAudioMessage(type))
        {
            await HandleAudioMessageAsync(
                socket,
                clientId,
                type,
                root,
                usageSession,
                recordingToken,
                cancellationToken);
            return true;
        }

        if (inputCommand is { } command)
        {
            var inputAccess = statusFactory.GetInputAccess(clientId);
            if (!inputAccess.CanUseRemoteInput)
            {
                await transport.SendAsync(socket, new { type = "input.error", seq = command.Sequence, code = "VAIR-INPUT-DENIED", message = "Remote input is disabled for this device on the PC." }, cancellationToken);
                commandLog.Outcome(clientId, command.Type, HostCommandLog.GetAction(command.Type, command), "permission_denied");
                return true;
            }

            if (command.Kind is InputCommandKind.ScreenPointerMove or InputCommandKind.ScreenPointerButton or InputCommandKind.ScreenPointerWheel)
            {
                usageSession.RecordInputCommand(command.Kind, command.Context, recordingToken);
                await screenViewCommands.HandlePointerAsync(
                    socket,
                    clientId,
                    command,
                    inputAccess.CanControlHostApplication,
                    cancellationToken);
                return true;
            }

            usageSession.RecordInputCommand(command.Kind, command.Context, recordingToken);
            if (await inputCommands.HandleAsync(
                socket,
                command,
                clientId,
                inputAccess.CanControlHostApplication,
                cancellationToken))
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
            StatusRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task HandleAudioMessageAsync(
        WebSocket socket,
        string clientId,
        string type,
        JsonElement root,
        UsageTelemetrySession usageSession,
        UsageTelemetryRecordingToken recordingToken,
        CancellationToken cancellationToken)
    {
        var action = type == "audio.mute.toggle" ? "toggle_mute" : "set_volume";
        var outcome = "blocked";
        if (statusFactory.CanControlVolume(clientId))
        {
            if (ClientMessageValidator.TryGetOptionalInputContext(root, out var inputContext) &&
                inputContext == InputCommandContext.MediaControls)
            {
                usageSession.RecordOnce(UsageFeature.MediaControls, recordingToken);
            }

            if (AudioMessageRouter.TryHandle(root, audioController, out var audioState) &&
                audioState is not null)
            {
                await SendAudioStateAsync(socket, audioState, cancellationToken);
                outcome = "executed";
            }
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

    private void LogConnectionTimeout(WebSocket socket, bool authenticated, string? clientId)
    {
        var timeout = authenticated ? AuthenticatedInactivityTimeout : PairingHandshakeTimeout;
        appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            ClientId: string.IsNullOrWhiteSpace(clientId) ? null : clientId,
            Action: authenticated ? "connection_inactivity_timeout" : "pairing_handshake_timeout",
            Outcome: "timed_out",
            Code: socket is RelayVirtualWebSocket ? "relay" : "direct",
            Detail: $"timeoutMs={(long)timeout.TotalMilliseconds}"));
    }

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
        type is "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" or
            "screen.pointer.move" or "screen.pointer.button" or "screen.pointer.wheel" or
            "keyboard.text" or "keyboard.special";

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

    private sealed record AuthenticatedClient(
        string ClientId,
        PendingReconnect? PendingReconnect,
        PairingBootstrapPending? PendingPairing,
        PairingBootstrapPending? PendingPairingCommit = null);

    private sealed record PendingReconnect(
        string ClientId,
        string Challenge,
        string DeviceName,
        string? Platform,
        string? Browser,
        string? DisplayMode);
}
