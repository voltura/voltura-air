using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Globalization;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal enum RelayConnectionState
{
    Disabled,
    Connecting,
    Connected,
    Retrying,
    Failed,
    Disconnected
}

internal sealed class RelayStatusChangedEventArgs(
    RelayConnectionState state,
    string? failureCode) : EventArgs
{
    public RelayConnectionState State { get; } = state;
    public string? FailureCode { get; } = failureCode;
}

internal sealed class RelayHostConnection : IAsyncDisposable
{
    private const int MaximumTurnResponseBytes = 64 * 1024;
    private const int MaximumPendingDeviceCloses = 64;
    private const int MaximumRelayEnvelopeBytes = RelayEnvelope.MaximumEncodedBytes;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)
    ];
    private readonly RelayEndpointDescriptor _endpoint;
    private readonly RelayRoutingIdentity _identity;
    private readonly IAppLogWriter _log;
    private readonly RelayDeviceSessions _devices;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _manualRetry = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Guid, byte> _pendingDeviceCloses = new();
    private readonly Channel<Guid> _deviceCloseQueue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(MaximumPendingDeviceCloses)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private ClientWebSocket? _socket;
    private Task? _runTask;
    private Task? _deviceCloseTask;
    private RelayUsageSnapshot? _lastUsage;
    private int _disposeState;

    public RelayHostConnection(
        RelayEndpointDescriptor endpoint,
        RelayRoutingIdentity identity,
        Func<WebSocket, string, CancellationToken, Task> handleSession,
        IAppLogWriter log)
    {
        _endpoint = endpoint;
        _identity = identity;
        _log = log;
        _devices = new RelayDeviceSessions(
            handleSession,
            () => log.Write(new AppLogEntry("relay_state", "windows_host", Action: "device_session_failed", Outcome: "failed", Code: "handler")),
            QueueDeviceClose);
    }

    public RelayConnectionState State { get; private set; } = RelayConnectionState.Disabled;
    public string RouteId => _identity.RouteId;
    public string? FailureCode { get; private set; }
    public RelayUsageSnapshot? LastUsage => Volatile.Read(ref _lastUsage);
    internal int PendingDeviceCloseCount => _pendingDeviceCloses.Count;
    public event EventHandler? StateChanged;

    public async Task<RelayTurnConfiguration?> GetTurnConfigurationAsync(
        RelayScreenQuality requestedQuality,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = ScreenViewHostIdentity.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint.HttpsBase, $"/v1/turn/{_identity.RouteId}"))
        {
            Content = JsonContent.Create(new { timestamp, nonce, signature = _identity.SignTurnRequest(timestamp, nonce) })
        };
        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode == HttpStatusCode.TooManyRequests ? "quota-blocked" : "credential-unavailable";
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await TryUpdateUsageSnapshotAsync(response, cancellationToken);
                }
                _log.Write(new AppLogEntry("relay_turn", "windows_host", Action: code == "quota-blocked" ? "turn_quota_block" : "credential_refresh_failed", Outcome: "blocked", Code: code));
                return null;
            }
            using var document = await ReadBoundedJsonAsync(response.Content, cancellationToken);
            return ParseTurnConfiguration(document.RootElement, requestedQuality);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or
            InvalidDataException or InvalidOperationException or IOException)
        {
            _log.Write(new AppLogEntry("relay_turn", "windows_host", Action: "credential_refresh_failed", Outcome: "failed", Code: exception is JsonException or InvalidDataException ? "response" : "network"));
            return null;
        }
    }

    internal RelayTurnConfiguration? ParseTurnConfiguration(JsonElement root, RelayScreenQuality requestedQuality)
    {
        var usage = ParseUsageSnapshot(root);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("allowed", out var allowed) || allowed.ValueKind != JsonValueKind.True || usage is null ||
            !root.TryGetProperty("expiresAt", out var expiresValue) || expiresValue.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(expiresValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt) ||
            !root.TryGetProperty("iceServers", out var servers) || servers.ValueKind != JsonValueKind.Array)
            return null;
        var parsed = new List<RelayIceServer>();
        foreach (var server in servers.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object ||
                !server.TryGetProperty("username", out var usernameValue) || usernameValue.ValueKind != JsonValueKind.String ||
                usernameValue.GetString() is not { Length: > 0 and <= 512 } username ||
                !server.TryGetProperty("credential", out var credentialValue) || credentialValue.ValueKind != JsonValueKind.String ||
                credentialValue.GetString() is not { Length: > 0 and <= 512 } credential ||
                !server.TryGetProperty("urls", out var urlsValue) || urlsValue.ValueKind != JsonValueKind.Array) return null;
            var urls = new List<string>();
            foreach (var urlValue in urlsValue.EnumerateArray())
            {
                if (urlValue.ValueKind != JsonValueKind.String || urlValue.GetString() is not { } url) return null;
                if (IsAllowedTurnUrl(url)) urls.Add(url);
            }
            if (urls.Count == 0) return null;
            parsed.Add(new RelayIceServer([.. urls], username, credential));
        }
        if (parsed.Count == 0) return null;
        var forcedDataSaver = false;
        if (root.TryGetProperty("forcedQuality", out var forced) && forced.ValueKind != JsonValueKind.Null)
        {
            if (forced.ValueKind != JsonValueKind.String || forced.GetString() != "data-saver") return null;
            forcedDataSaver = true;
        }
        var effective = forcedDataSaver ? RelayScreenQuality.DataSaver : requestedQuality;
        StoreUsageSnapshot(usage);
        if (forcedDataSaver)
        {
            _log.Write(new AppLogEntry("relay_turn", "windows_host", Action: "automatic_data_saver", Outcome: "enabled", Code: "quota-warning"));
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        var hostUris = parsed.SelectMany(server => server.Urls.Select(url =>
            $"{url[..url.IndexOf(':')]}:{Uri.EscapeDataString(server.Username)}:{Uri.EscapeDataString(server.Credential)}@{url[(url.IndexOf(':') + 1)..]}"))
            .ToArray();
        return new RelayTurnConfiguration(parsed, hostUris, expiresAt, usage.Bytes, usage.CheckedAt, effective);
    }

    private async Task TryUpdateUsageSnapshotAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await ReadBoundedJsonAsync(response.Content, cancellationToken);
            var root = document.RootElement;
            if (ParseUsageSnapshot(root) is { } usage)
            {
                StoreUsageSnapshot(usage);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or IOException)
        {
            // The bounded failure code is logged by the caller; a malformed optional snapshot is ignored.
        }
    }

    internal static RelayUsageSnapshot? ParseUsageSnapshot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("usageBytes", out var usage) || usage.ValueKind != JsonValueKind.Number ||
            !usage.TryGetInt64(out var usageBytes) || usageBytes < 0 ||
            !root.TryGetProperty("checkedAt", out var checkedValue) || checkedValue.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(checkedValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var checkedAt))
        {
            return null;
        }
        long? warningBytes = null;
        long? cutoffBytes = null;
        if (root.TryGetProperty("usageWarningBytes", out var warning) && warning.ValueKind == JsonValueKind.Number &&
            warning.TryGetInt64(out var parsedWarningBytes) && parsedWarningBytes > 0 &&
            root.TryGetProperty("usageCutoffBytes", out var cutoff) && cutoff.ValueKind == JsonValueKind.Number &&
            cutoff.TryGetInt64(out var parsedCutoffBytes) && parsedCutoffBytes > parsedWarningBytes)
        {
            warningBytes = parsedWarningBytes;
            cutoffBytes = parsedCutoffBytes;
        }
        return new RelayUsageSnapshot(usageBytes, checkedAt, warningBytes, cutoffBytes);
    }

    private void StoreUsageSnapshot(RelayUsageSnapshot usage)
    {
        Volatile.Write(ref _lastUsage, usage);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static async Task<JsonDocument> ReadBoundedJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumTurnResponseBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        if (length > MaximumTurnResponseBytes) throw new InvalidDataException("Relay TURN response was too large.");
        return JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 16 });
    }

    private bool IsAllowedTurnUrl(string value)
    {
        if (_endpoint.IsOfficial)
        {
            return value is "turns:turn.cloudflare.com:443?transport=tcp" or "turn:turn.cloudflare.com:3478?transport=udp";
        }

        if (value.Length > 512) return false;
        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"\Aturns?:[A-Za-z0-9.-]+(?::(?<port>\d{1,5}))?(?:\?transport=(?:tcp|udp))?\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        return match.Success && (!match.Groups["port"].Success ||
            int.TryParse(match.Groups["port"].Value, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535);
    }

    public void Retry()
    {
        _socket?.Abort();
        if (_manualRetry.CurrentCount == 0) _manualRetry.Release();
    }

    public void Start()
    {
        if (_runTask is not null) return;
        _deviceCloseTask = RunDeviceCloseSenderAsync(_shutdown.Token);
        _runTask = RunAsync(_shutdown.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            SetState(attempt == 0 ? RelayConnectionState.Connecting : RelayConnectionState.Retrying);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            _socket = socket;
            try
            {
                await socket.ConnectAsync(CreateHostUri(), cancellationToken);
                await AuthenticateAsync(socket, cancellationToken);
                attempt = 0;
                FailureCode = null;
                SetState(RelayConnectionState.Connected);
                await ReceiveLoopAsync(socket, cancellationToken);
                FailureCode = "connection-closed";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException or JsonException or InvalidDataException)
            {
                FailureCode = exception switch
                {
                    WebSocketException => "websocket",
                    HttpRequestException => "https",
                    JsonException => "protocol",
                    _ => "authentication"
                };
                SetState(RelayConnectionState.Failed);
            }
            finally
            {
                _socket = null;
                await _devices.CloseAndDrainAsync();
            }

            var delay = RetryDelays[Math.Min(attempt++, RetryDelays.Length - 1)];
            try
            {
                using var retryWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = Task.Delay(delay, retryWait.Token);
                var retryTask = _manualRetry.WaitAsync(retryWait.Token);
                await Task.WhenAny(delayTask, retryTask);
                await retryWait.CancelAsync();
            }
            catch (OperationCanceledException) { break; }
        }

        SetState(RelayConnectionState.Disconnected);
    }

    private async Task AuthenticateAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        await SendTextAsync(socket, new { type = "relay.host.hello", routeId = _identity.RouteId, publicKey = _identity.PublicKey }, cancellationToken);
        using var challenge = await ReceiveTextAsync(socket, cancellationToken);
        if (!challenge.RootElement.TryGetProperty("type", out var type) || type.GetString() != "relay.host.challenge" ||
            !challenge.RootElement.TryGetProperty("challenge", out var challengeValue) || challengeValue.GetString() is not { Length: 43 } nonce)
        {
            throw new InvalidDataException("Relay authentication challenge was invalid.");
        }

        await SendTextAsync(socket, new { type = "relay.host.proof", signature = _identity.SignChallenge(nonce) }, cancellationToken);
        using var accepted = await ReceiveTextAsync(socket, cancellationToken);
        if (!accepted.RootElement.TryGetProperty("type", out type) || type.GetString() != "relay.host.accepted")
        {
            throw new InvalidDataException("Relay authentication was rejected.");
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumRelayEnvelopeBytes];
        while (socket.State == WebSocketState.Open)
        {
            var envelope = await ReceiveRelayEnvelopeAsync(socket, buffer, cancellationToken);
            if (envelope is null) break;

            ProcessRelayEnvelope(envelope, cancellationToken);
        }
    }

    internal void ProcessRelayEnvelope(
        RelayEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.Kind == RelayEnvelopeKind.Connected)
        {
            RelayVirtualWebSocket? virtualSocket = null;
            try
            {
                virtualSocket = new RelayVirtualWebSocket(envelope.SessionId, _identity.RouteId, SendEnvelopeAsync);
                if (_devices.TryStart(envelope.SessionId, virtualSocket, envelope.Payload, cancellationToken))
                {
                    virtualSocket = null;
                }
                else
                {
                    QueueDeviceClose(envelope.SessionId);
                }
            }
            finally { virtualSocket?.Dispose(); }
        }
        else if (envelope.Kind == RelayEnvelopeKind.Disconnected)
        {
            _devices.Disconnect(envelope.SessionId);
        }
        else if (envelope.Kind is RelayEnvelopeKind.Text or RelayEnvelopeKind.Binary)
        {
            if (!_devices.TryDeliver(
                envelope.SessionId,
                envelope.Payload,
                envelope.Kind == RelayEnvelopeKind.Binary))
            {
                QueueDeviceClose(envelope.SessionId);
            }
        }
        else
        {
            throw new InvalidDataException("The relay sent an envelope in the wrong direction.");
        }
    }

    private void QueueDeviceClose(Guid sessionId)
    {
        if (!_pendingDeviceCloses.TryAdd(sessionId, 0)) return;
        if (_deviceCloseQueue.Writer.TryWrite(sessionId)) return;
        _pendingDeviceCloses.TryRemove(sessionId, out _);
        _socket?.Abort();
    }

    private async Task RunDeviceCloseSenderAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sessionId in _deviceCloseQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await SendEnvelopeAsync(
                        new RelayEnvelope(RelayEnvelopeKind.CloseDevice, sessionId, []),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
                {
                    _socket?.Abort();
                }
                finally
                {
                    _pendingDeviceCloses.TryRemove(sessionId, out _);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendEnvelopeAsync(RelayEnvelope envelope, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket?.State != WebSocketState.Open) throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        var bytes = envelope.Encode();
        await _sendGate.WaitAsync(cancellationToken);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancellationToken); }
        finally { _sendGate.Release(); }
    }

    private Uri CreateHostUri()
    {
        var builder = new UriBuilder(_endpoint.WebSocketBase)
        {
            Path = $"{_endpoint.WebSocketBase.AbsolutePath.TrimEnd('/')}/v1/host/{_identity.RouteId}"
        };
        return builder.Uri;
    }

    private static async Task SendTextAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<JsonDocument> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var bytes = new byte[2048];
        var message = await WebSocketTransport.ReceiveBoundedMessageAsync(
            socket,
            bytes,
            WebSocketMessageType.Text,
            bytes.Length,
            cancellationToken);
        if (message is null) throw new InvalidDataException();
        return JsonDocument.Parse(message.Value.Buffer.AsMemory(0, message.Value.Count), new JsonDocumentOptions { MaxDepth = 8 });
    }

    internal static async Task<RelayEnvelope?> ReceiveRelayEnvelopeAsync(
        WebSocket socket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var message = await WebSocketTransport.ReceiveBoundedMessageAsync(
            socket,
            buffer,
            WebSocketMessageType.Binary,
            MaximumRelayEnvelopeBytes,
            cancellationToken);
        if (message is null) return null;
        if (!RelayEnvelope.TryDecode(message.Value.Buffer.AsSpan(0, message.Value.Count), out var envelope) || envelope is null)
        {
            throw new InvalidDataException("Relay envelope was invalid.");
        }
        return envelope;
    }

    private void SetState(RelayConnectionState state)
    {
        if (State == state) return;
        State = state;
        _log.Write(new AppLogEntry("relay_state", "windows_host", Action: state.ToString().ToLowerInvariant(), Outcome: "ok", Code: _endpoint.IsOfficial ? "official" : "custom"));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        await _shutdown.CancelAsync();
        _deviceCloseQueue.Writer.TryComplete();
        _socket?.Abort();
        _socket?.Dispose();
        if (_runTask is not null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }
        if (_deviceCloseTask is not null)
        {
            await _deviceCloseTask;
        }
        await _devices.CloseAndDrainAsync();
        _sendGate.Dispose();
        _manualRetry.Dispose();
        _shutdown.Dispose();
        _identity.Dispose();
    }
}
