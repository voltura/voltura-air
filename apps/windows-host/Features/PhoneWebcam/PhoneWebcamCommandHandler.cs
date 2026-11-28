using System.Net.WebSockets;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamCommandHandler : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<WebSocket, PendingStart> _pendingStarts = [];
    private readonly Dictionary<WebSocket, Task> _pendingAnswers = [];
    private readonly Dictionary<WebSocket, Task> _pendingStops = [];
    private readonly PhoneWebcamCoordinator _coordinator;
    private readonly WebSocketTransport _transport;
    private readonly IAppLogWriter _appLog;
    private readonly Func<CancellationToken, Task<RelayTurnConfiguration?>> _getRelayTurnConfiguration;
    private int _disposed;

    internal PhoneWebcamCommandHandler(
        PhoneWebcamCoordinator coordinator,
        WebSocketTransport transport,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        IAppLogWriter appLog)
    {
        _coordinator = coordinator;
        _transport = transport;
        _appLog = appLog;
        _getRelayTurnConfiguration = getRelayTurnConfiguration;
        coordinator.Ended += OnEnded;
    }

    internal event EventHandler<PhoneWebcamActivityChangedEventArgs>? ActivityChanged
    {
        add => _coordinator.ActivityChanged += value;
        remove => _coordinator.ActivityChanged -= value;
    }

    internal Task StopFromHostAsync(string clientId) => _coordinator.StopAsync(clientId, "host-stopped");

    internal Task StartAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        bool useMicrophone,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        var duplicate = false;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            if (_pendingStarts.ContainsKey(socket))
            {
                duplicate = true;
            }
            else
            {
#pragma warning disable CA2000 // PendingStart owns the linked cancellation source.
                var pending = new PendingStart(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
#pragma warning restore CA2000
                _pendingStarts.Add(socket, pending);
                pending.Task = RunStartAsync(
                    pending,
                    _pendingStops.GetValueOrDefault(socket),
                    socket,
                    clientId,
                    operationId,
                    captureWidth,
                    captureHeight,
                    captureFps,
                    useMicrophone,
                    clientSignature);
            }
        }

        return duplicate
            ? SendStartResultAsync(
                socket,
                operationId,
                new PhoneWebcamStartResult(false, "busy", "Another Phone webcam request is already being prepared."),
                cancellationToken)
            : Task.CompletedTask;
    }

    private async Task RunStartAsync(
        PendingStart pending,
        Task? precedingStop,
        WebSocket socket,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        bool useMicrophone,
        string clientSignature)
    {
        await Task.Yield();
        try
        {
            if (precedingStop is not null)
            {
                await precedingStop.WaitAsync(pending.Token).ConfigureAwait(false);
            }
            pending.Token.ThrowIfCancellationRequested();
            await StartCoreAsync(
                socket,
                clientId,
                operationId,
                captureWidth,
                captureHeight,
                captureFps,
                useMicrophone,
                clientSignature,
                pending.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                await SendStartResultAsync(
                    socket,
                    operationId,
                    new PhoneWebcamStartResult(false, "webrtc-unavailable", "The PC could not prepare Phone webcam."),
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
                if (_pendingStarts.TryGetValue(socket, out PendingStart? current) && ReferenceEquals(current, pending))
                {
                    _pendingStarts.Remove(socket);
                }
            }

            pending.Dispose();
        }
    }

    private async Task StartCoreAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        int captureWidth,
        int captureHeight,
        int captureFps,
        bool useMicrophone,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        PhoneWebcamStartResult result = await _coordinator.StartAsync(
            socket,
            clientId,
            operationId,
            captureWidth,
            captureHeight,
            captureFps,
            useMicrophone,
            clientSignature,
            socket is RelayVirtualWebSocket ? _getRelayTurnConfiguration : null,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            _appLog.Write(new AppLogEntry("phone_webcam", "windows_host", Action: "start_failed", Outcome: "failed", Code: result.Code));
        await SendStartResultAsync(socket, operationId, result, cancellationToken).ConfigureAwait(false);
    }

    internal Task AnswerAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        CancellationToken cancellationToken)
    {
        Task? answer = null;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            if (_pendingAnswers.GetValueOrDefault(socket)?.IsCompleted is not false)
            {
                answer = RunAnswerAsync(socket, clientId, operationId, answerSdp, clientSignature,
                    _pendingStops.GetValueOrDefault(socket), cancellationToken);
                _pendingAnswers[socket] = answer;
            }
        }

        if (answer is null)
        {
            return SendAnswerResultAsync(socket, operationId,
                new(false, "offer-expired", "The Phone webcam offer is already being answered."), cancellationToken);
        }

        ForgetCompletedWork(socket, answer, _pendingAnswers);
        return Task.CompletedTask;
    }

    private async Task RunAnswerAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string answerSdp,
        string clientSignature,
        Task? precedingStop,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            if (precedingStop is not null)
                await precedingStop.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            PhoneWebcamOperationResult result = await _coordinator.CompleteAnswerAsync(
                socket, clientId, operationId, answerSdp, clientSignature).ConfigureAwait(false);
            await SendAnswerResultAsync(socket, operationId, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                await SendAnswerResultAsync(socket, operationId,
                    new(false, "webrtc-unavailable", "The PC could not answer the Phone webcam connection."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sendException) when (sendException is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
            }
        }
    }

    private Task SendAnswerResultAsync(WebSocket socket, string operationId,
        PhoneWebcamOperationResult result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
            _appLog.Write(new AppLogEntry("phone_webcam", "windows_host", Action: "answer_failed", Outcome: "failed", Code: result.Code));
        return _transport.SendAsync(socket, new
        {
            type = "phone.webcam.answer.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    internal Task StopAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        Task stop;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            PendingStart? pending = _pendingStarts.GetValueOrDefault(socket);
            _pendingStarts.Remove(socket);
            stop = RunStopAsync(socket, clientId, operationId, pending, _pendingAnswers.GetValueOrDefault(socket),
                _pendingStops.GetValueOrDefault(socket), cancellationToken);
            _pendingStops[socket] = stop;
        }

        ForgetCompletedWork(socket, stop, _pendingStops);
        return Task.CompletedTask;
    }

    private void ForgetCompletedWork(WebSocket socket, Task work, Dictionary<WebSocket, Task> pendingWork)
    {
        _ = work.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(pendingWork.GetValueOrDefault(socket), completed))
                    {
                        pendingWork.Remove(socket);
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunStopAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        PendingStart? pending,
        Task? precedingAnswer,
        Task? precedingStop,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        pending?.Cancel();
        try
        {
            if (precedingStop is not null)
            {
                await precedingStop.ConfigureAwait(false);
            }
            if (pending is not null)
            {
                await pending.Task.ConfigureAwait(false);
            }

            await _coordinator.StopAsync(clientId, owner: socket).ConfigureAwait(false);
            if (precedingAnswer is not null)
                await precedingAnswer.ConfigureAwait(false);
            await SendStopResultAsync(socket, operationId, true, "stopped", "Phone webcam stopped.", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                await SendStopResultAsync(socket, operationId, false, "stop-failed", "Phone webcam could not be stopped cleanly.", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sendException) when (sendException is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
            }
        }
    }

    private Task SendStopResultAsync(
        WebSocket socket,
        string operationId,
        bool succeeded,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new
        {
            type = "phone.webcam.stop.result",
            operationId,
            succeeded,
            code,
            message
        }, cancellationToken);

    internal async Task ClientDisconnectedAsync(string clientId, WebSocket socket)
    {
        PendingStart? pending;
        Task? stop;
        Task? answer;
        lock (_gate)
        {
            pending = _pendingStarts.GetValueOrDefault(socket);
            stop = _pendingStops.GetValueOrDefault(socket);
            answer = _pendingAnswers.GetValueOrDefault(socket);
        }
        pending?.Cancel();
        if (pending is not null)
        {
            await pending.Task.ConfigureAwait(false);
        }
        if (stop is not null)
        {
            await stop.ConfigureAwait(false);
        }

        await _coordinator.StopAsync(clientId, "connection-lost", socket).ConfigureAwait(false);
        if (answer is not null)
            await answer.ConfigureAwait(false);
    }

    private Task SendStartResultAsync(
        WebSocket socket,
        string operationId,
        PhoneWebcamStartResult result,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new
        {
            type = "phone.webcam.start.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            offerSdp = result.OfferSdp,
            hostSignature = result.HostSignature,
            iceServers = result.IceServers,
            turnExpiresAt = result.TurnExpiresAt,
            relayUsageBytes = result.RelayUsageBytes,
            relayUsageCheckedAt = result.RelayUsageCheckedAt,
            relayQuality = result.RelayQuality?.ToString(),
            maximumBitrate = result.MaximumBitrate
        }, cancellationToken);

    private void OnEnded(object? sender, PhoneWebcamActivityChangedEventArgs args)
    {
        _appLog.Write(new AppLogEntry("phone_webcam", "windows_host", Action: "ended", Code: args.State));
        _ = NotifyEndedAsync(args);
    }

    private async Task NotifyEndedAsync(PhoneWebcamActivityChangedEventArgs args)
    {
        if (args.ClientId is null || args.OperationId is null || args.Owner is not WebSocket owner)
        {
            return;
        }

        var payload = new
        {
            type = "phone.webcam.ended",
            operationId = args.OperationId,
            reason = args.State,
            message = args.State switch
            {
                "transport-lost" => "The webcam video connection was interrupted.",
                "decoder-failed" => "The PC stopped receiving usable webcam video.",
                "audio-failed" => "The PC microphone output stopped.",
                "offer-expired" => "The webcam connection offer expired.",
                "permission-revoked" => "The PC stopped Phone webcam because permission was revoked.",
                "pairing-revoked" => "The PC removed this phone pairing.",
                "host-stopped" => "Phone webcam was stopped on the PC.",
                _ => "The Phone webcam session ended."
            }
        };
        if (!_transport.Snapshot().Any(connection =>
                ReferenceEquals(connection.Socket, owner) &&
                string.Equals(connection.ClientId, args.ClientId, StringComparison.Ordinal)))
        {
            return;
        }

        try
        {
            await _transport.SendAsync(owner, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PendingStart[] pending;
        Task[] stops;
        Task[] answers;
        lock (_gate)
        {
            pending = [.. _pendingStarts.Values];
            stops = [.. _pendingStops.Values];
            answers = [.. _pendingAnswers.Values];
        }

        foreach (PendingStart item in pending)
        {
            item.Cancel();
        }

        _coordinator.Ended -= OnEnded;
        Task sessions = _coordinator.DisposeAsync().AsTask();
        await Task.WhenAll(pending.Select(item => item.Task).Concat(stops).Concat(answers).Append(sessions)).ConfigureAwait(false);
    }

    private sealed class PendingStart(CancellationTokenSource cancellation) : IDisposable
    {
        internal CancellationToken Token => cancellation.Token;
        internal Task Task { get; set; } = Task.CompletedTask;

        internal void Cancel()
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
