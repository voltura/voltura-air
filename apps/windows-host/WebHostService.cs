using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace VolturaAir.Host;

public sealed class WebHostService : IAsyncDisposable
{
    private static readonly string DeveloperSessionId = Guid.NewGuid().ToString("N");
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private readonly PairingManager _pairingManager;
    private readonly InputDispatcher _inputDispatcher;
    private readonly ISystemAudioController _audioController;
    private readonly PairingAttemptRateLimiter _pairingAttemptRateLimiter = new();
    private readonly object _connectionsGate = new();
    private readonly Dictionary<string, List<WebSocket>> _activeSockets = new(StringComparer.Ordinal);
    private readonly Dictionary<WebSocket, SemaphoreSlim> _sendGates = new();
    private WebApplication? _app;

    public WebHostService(PairingManager pairingManager, InputDispatcher inputDispatcher, ISystemAudioController? audioController = null)
    {
        _pairingManager = pairingManager;
        _inputDispatcher = inputDispatcher;
        _audioController = audioController ?? new SystemAudioController();
        _pairingManager.PairingRevoked += OnPairingRevoked;
        _pairingManager.PermissionsChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
        AppDeveloperSettings.Changed += OnPermissionsChanged;
        AppRemoteSettings.Changed += OnPermissionsChanged;

        var settings = AppNetworkSettings.Load();
        var portSelection = PortSelector.Select(settings, IsPortAvailable, FindFreePort);
        if (!portSelection.Succeeded)
        {
            throw new HostPortUnavailableException(portSelection.ErrorMessage ?? "The configured Voltura Air port is unavailable.");
        }

        Port = portSelection.Port;
        PortSelectionWarning = portSelection.Warning;
        if (portSelection.IsAutomatic)
        {
            AppNetworkSettings.SetLastAutomaticPort(Port);
        }

        var addressSelection = LanAddressSelector.Select(LanAddressSelector.GetCandidates(), settings);
        AdvertisedHostAddress = addressSelection?.Address.ToString() ?? GetDnsLanAddressFallback() ?? "127.0.0.1";
        SelectedAdapterName = GetSelectedAdapterName(addressSelection?.Candidate);
        AddressSelectionWarning = addressSelection?.Warning;
        if (settings.NetworkMode == NetworkSelectionMode.Automatic)
        {
            AppNetworkSettings.SetLastAutomaticHostAddress(AdvertisedHostAddress);
        }

        ServerUrl = BuildServerUrl(AdvertisedHostAddress, Port);
    }

    public int Port { get; }

    public string ServerUrl { get; private set; }

    public string WebSocketUrl => BuildWebSocketUrl(AdvertisedHostAddress, Port);

    public string AdvertisedHostAddress { get; private set; }

    public string SelectedAdapterName { get; private set; }

    public string? AddressSelectionWarning { get; }

    public string? PortSelectionWarning { get; }

    internal void UpdateAdvertisedHostAddress(string hostAddress, LanAddressCandidate? selectedCandidate = null)
    {
        AdvertisedHostAddress = hostAddress;
        SelectedAdapterName = GetSelectedAdapterName(selectedCandidate);
        ServerUrl = BuildServerUrl(hostAddress, Port);
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        var app = builder.Build();
        app.UseWebSockets();

        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!IsAllowedWebSocketOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await HandleSocketAsync(socket, GetRateLimitKey(context), context.RequestAborted);
        });

        var staticRoot = ResolveStaticRoot();
        if (Directory.Exists(staticRoot))
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(staticRoot)
            });
            app.Use(async (context, next) =>
            {
                if (await TryServeCompressedJavaScriptAsync(context, staticRoot))
                {
                    return;
                }

                await next();
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(staticRoot),
                OnPrepareResponse = context => SetStaticCacheHeaders(context.Context.Response, context.Context.Request.Path.Value)
            });
            app.MapFallback(async context =>
            {
                SetStaticCacheHeaders(context.Response, "index.html");
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(Path.Combine(staticRoot, "index.html"));
            });
        }
        else
        {
            app.MapGet("/", () => Results.Text("Mobile web build missing. Run: npm run build --workspace apps/mobile-web", "text/plain"));
        }

        _app = app;
        await app.StartAsync();
    }

    public async Task StopAsync()
    {
        AbortActiveSockets();

        if (_app is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(ShutdownTimeout);
        try
        {
            await _app.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pairingManager.PairingRevoked -= OnPairingRevoked;
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        AppDeveloperSettings.Changed -= OnPermissionsChanged;
        AppRemoteSettings.Changed -= OnPermissionsChanged;
        AbortActiveSockets();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

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
                    await SendSocketAsync(socket, new { type = "pair.accepted", clientId, pcName, secret, paired = true, capabilities, host = CreateHostStatus() }, cancellationToken);
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
        lock (_connectionsGate)
        {
            if (!_activeSockets.TryGetValue(clientId, out var sockets))
            {
                sockets = new List<WebSocket>();
                _activeSockets[clientId] = sockets;
            }

            sockets.Add(socket);
            _sendGates.TryAdd(socket, new SemaphoreSlim(1, 1));
        }
    }

    private void UnregisterSocket(string clientId, WebSocket socket)
    {
        lock (_connectionsGate)
        {
            if (!_activeSockets.TryGetValue(clientId, out var sockets))
            {
                return;
            }

            sockets.Remove(socket);
            if (sockets.Count == 0)
            {
                _activeSockets.Remove(clientId);
            }

            if (_sendGates.Remove(socket, out var sendGate))
            {
                sendGate.Dispose();
            }
        }
    }

    private void AbortActiveSockets()
    {
        WebSocket[] sockets;
        lock (_connectionsGate)
        {
            sockets = _activeSockets.Values.SelectMany(items => items).ToArray();
            _activeSockets.Clear();
            foreach (var sendGate in _sendGates.Values)
            {
                sendGate.Dispose();
            }

            _sendGates.Clear();
        }

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
        WebSocket[] sockets;
        lock (_connectionsGate)
        {
            if (e.ClientId is null)
            {
                sockets = _activeSockets.Values.SelectMany(items => items).ToArray();
                _activeSockets.Clear();
                ClearSendGates(sockets);
            }
            else if (_activeSockets.Remove(e.ClientId, out var deviceSockets))
            {
                sockets = deviceSockets.ToArray();
                ClearSendGates(sockets);
            }
            else
            {
                return;
            }
        }

        _ = Task.Run(() => CloseSocketsAsync(sockets));
    }

    private void ClearSendGates(IEnumerable<WebSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            if (_sendGates.Remove(socket, out var sendGate))
            {
                sendGate.Dispose();
            }
        }
    }

    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        _ = Task.Run(BroadcastStatusAsync);
    }

    private async Task BroadcastStatusAsync()
    {
        (string ClientId, WebSocket Socket)[] sockets;
        lock (_connectionsGate)
        {
            sockets = _activeSockets
                .SelectMany(pair => pair.Value.Select(socket => (pair.Key, socket)))
                .ToArray();
        }

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
        SemaphoreSlim? sendGate;
        lock (_connectionsGate)
        {
            _sendGates.TryGetValue(socket, out sendGate);
        }

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
            new { type = "status", connected = false, message, pcName = Environment.MachineName, capabilities = CreateCapabilities(clientId), host = CreateHostStatus() },
            cancellationToken);
    }

    private Task SendConnectedStatusAsync(WebSocket socket, string clientId, CancellationToken cancellationToken)
    {
        return SendSocketAsync(
            socket,
            new { type = "status", connected = true, message = "Connected", pcName = Environment.MachineName, capabilities = CreateCapabilities(clientId), host = CreateHostStatus() },
            cancellationToken);
    }

    private object CreateCapabilities(string clientId)
    {
        return new { sleep = CanSleepPc(clientId), volume = CanControlVolume(clientId), gestureDebug = AppDeveloperSettings.EnableGestureDebug(), inputAck = true };
    }

    private bool CanSleepPc(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowPcSleep;
    }

    private bool CanControlVolume(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowVolumeControl;
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

    private static async Task<bool> TryServeCompressedJavaScriptAsync(HttpContext context, string staticRoot)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value;
        if (path is null || !path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filePath = ResolveStaticFilePath(staticRoot, path);
        if (filePath is null)
        {
            return false;
        }

        if (AcceptsEncoding(context.Request, "br") && File.Exists($"{filePath}.br"))
        {
            await ServeCompressedJavaScriptAsync(context, $"{filePath}.br", "br", path);
            return true;
        }

        if (AcceptsEncoding(context.Request, "gzip") && File.Exists($"{filePath}.gz"))
        {
            await ServeCompressedJavaScriptAsync(context, $"{filePath}.gz", "gzip", path);
            return true;
        }

        return false;
    }

    private static async Task ServeCompressedJavaScriptAsync(HttpContext context, string filePath, string encoding, string requestPath)
    {
        context.Response.ContentType = "application/javascript";
        context.Response.Headers["Content-Encoding"] = encoding;
        context.Response.Headers["Vary"] = "Accept-Encoding";
        SetStaticCacheHeaders(context.Response, requestPath);
        context.Response.ContentLength = new FileInfo(filePath).Length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.SendFileAsync(filePath);
    }

    private static void SetStaticCacheHeaders(HttpResponse response, string? requestPath)
    {
        var fileName = Path.GetFileName(requestPath);
        if (string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "sw.js", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";
            return;
        }

        if (requestPath?.Contains("/assets/", StringComparison.OrdinalIgnoreCase) == true)
        {
            response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
    }

    private static bool AcceptsEncoding(HttpRequest request, string expectedEncoding)
    {
        var acceptEncoding = request.Headers["Accept-Encoding"].ToString();
        return acceptEncoding
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(encoding => encoding.Equals(expectedEncoding, StringComparison.OrdinalIgnoreCase) || encoding.StartsWith($"{expectedEncoding};", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveStaticFilePath(string staticRoot, string requestPath)
    {
        if (requestPath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(staticRoot);
        var relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        return fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string ResolveStaticRoot()
    {
        var devRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mobile-web", "dist"));
        if (Directory.Exists(devRoot))
        {
            return devRoot;
        }

        var outputRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        return outputRoot;
    }

    internal static bool IsPortAvailable(int port)
    {
        return PortSelector.IsPortAvailable(port);
    }

    internal static int FindFreePort()
    {
        return PortSelector.FindFreePort();
    }

    internal static string? GetDnsLanAddressFallback()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .FirstOrDefault(address => !address.StartsWith("127.", StringComparison.Ordinal));
        }
        catch (SocketException)
        {
            return null;
        }
    }

    internal static string BuildServerUrl(string hostAddress, int port)
    {
        return $"http://{hostAddress}:{port}";
    }

    internal static string BuildWebSocketUrl(string hostAddress, int port)
    {
        return $"ws://{hostAddress}:{port}/ws";
    }

    internal static bool IsAllowedWebSocketOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
            originUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var requestHost = request.Host.Host;
        var requestPort = request.Host.Port;
        if (string.Equals(originUri.Host, requestHost, StringComparison.OrdinalIgnoreCase) &&
            (requestPort is null || originUri.Port == requestPort))
        {
            return true;
        }

        var configuredClientUrl = Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        if (Uri.TryCreate(configuredClientUrl, UriKind.Absolute, out var configuredClientUri) &&
            SameOrigin(originUri, configuredClientUri))
        {
            return true;
        }

        return IsLoopbackOrPrivateHost(originUri.Host);
    }

    private static string GetRateLimitKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool SameOrigin(Uri first, Uri second)
    {
        return string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.Host, second.Host, StringComparison.OrdinalIgnoreCase) &&
            first.Port == second.Port;
    }

    private static bool IsLoopbackOrPrivateHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 127 ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 169 && bytes[1] == 254);
    }

    private HostStatusMetadata CreateHostStatus()
    {
        var pcName = Environment.MachineName;
        var developerMode = AppDeveloperSettings.DeveloperMode();
        return new HostStatusMetadata(
            AppVersion.Display,
            pcName,
            SelectedAdapterName,
            AdvertisedHostAddress,
            Port,
            WebSocketUrl,
            AppRemoteSettings.ToProtocolId(AppRemoteSettings.GetDefaultRemoteMode()),
            developerMode,
            developerMode ? DeveloperSessionId : null);
    }

    private static string GetSelectedAdapterName(LanAddressCandidate? selectedCandidate)
    {
        return selectedCandidate is null
            ? "DNS fallback"
            : LanAddressSelector.GetAdapterDisplayName(selectedCandidate);
    }
}

internal sealed record HostStatusMetadata(
    string HostVersion,
    string PcName,
    string SelectedAdapterName,
    string SelectedIp,
    int SelectedPort,
    string WebSocketUrl,
    string DefaultRemoteMode,
    bool DeveloperMode,
    string? DeveloperSessionId);

public sealed class HostPortUnavailableException : Exception
{
    public HostPortUnavailableException(string message)
        : base(message)
    {
    }
}
