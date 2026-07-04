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
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private readonly PairingManager _pairingManager;
    private readonly InputDispatcher _inputDispatcher;
    private readonly ISystemAudioController _audioController;
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
        AddressSelectionWarning = addressSelection?.Warning;
        if (settings.NetworkMode == NetworkSelectionMode.Automatic)
        {
            AppNetworkSettings.SetLastAutomaticHostAddress(AdvertisedHostAddress);
        }

        ServerUrl = BuildServerUrl(AdvertisedHostAddress, Port);
    }

    public int Port { get; }

    public string ServerUrl { get; private set; }

    public string AdvertisedHostAddress { get; private set; }

    public string? AddressSelectionWarning { get; }

    public string? PortSelectionWarning { get; }

    public void UpdateAdvertisedHostAddress(string hostAddress)
    {
        AdvertisedHostAddress = hostAddress;
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

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await HandleSocketAsync(socket, context.RequestAborted);
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
                FileProvider = new PhysicalFileProvider(staticRoot)
            });
            app.MapFallback(async context =>
            {
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
        AbortActiveSockets();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task HandleSocketAsync(WebSocket socket, CancellationToken cancellationToken)
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
                var type = root.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() : null;

                if (!authenticated)
                {
                    if (type != "pair.hello")
                    {
                        await SendSocketAsync(socket, new { type = "pair.rejected", reason = "pair-first" }, cancellationToken);
                        continue;
                    }

                    var clientId = GetString(root, "clientId");
                    var deviceName = GetString(root, "deviceName");
                    var result = _pairingManager.Accept(
                        clientId,
                        deviceName,
                        GetOptionalString(root, "pairToken"),
                        GetOptionalString(root, "secret"),
                        platform: GetOptionalString(root, "platform"),
                        browser: GetOptionalString(root, "browser"),
                        displayMode: GetOptionalString(root, "displayMode"));
                    if (!result.Accepted)
                    {
                        await SendSocketAsync(socket, new { type = "pair.rejected", reason = result.Reason }, cancellationToken);
                        continue;
                    }

                    var secret = result.Secret ?? _pairingManager.RotateSecret(clientId, deviceName);
                    authenticated = true;
                    authenticatedClientId = clientId;
                    RegisterSocket(clientId, socket);
                    activeConnection = _pairingManager.TrackConnection(clientId);
                    var pcName = Environment.MachineName;
                    var capabilities = CreateCapabilities(clientId);
                    await SendSocketAsync(socket, new { type = "pair.accepted", clientId, pcName, secret, paired = true, capabilities }, cancellationToken);
                    await SendSocketAsync(socket, new { type = "status", connected = true, message = "Connected", pcName, capabilities }, cancellationToken);
                    await SendAudioStateAsync(socket, clientId, cancellationToken);
                    continue;
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

                if (type == "status.ping")
                {
                    await SendSocketAsync(socket, new { type = "status.pong", pcName = Environment.MachineName, capabilities = CreateCapabilities(authenticatedClientId) }, cancellationToken);
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

                _inputDispatcher.Dispatch(root);
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

        var pcName = Environment.MachineName;
        foreach (var (clientId, socket) in sockets)
        {
            try
            {
                await SendSocketAsync(
                    socket,
                    new { type = "status", connected = true, message = "Connected", pcName, capabilities = CreateCapabilities(clientId) },
                    CancellationToken.None);
                await SendAudioStateAsync(socket, clientId, CancellationToken.None);
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

    private static async Task CloseSocketAsync(WebSocket socket, string reason, CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
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

    private object CreateCapabilities(string clientId)
    {
        return new { sleep = CanSleepPc(clientId), volume = CanControlVolume(clientId) };
    }

    private bool CanSleepPc(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowPcSleep;
    }

    private bool CanControlVolume(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowVolumeControl;
    }

    private static bool IsAudioMessage(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        return typeProperty.GetString() is "audio.mute.toggle" or "audio.volume.set";
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
            await ServeCompressedJavaScriptAsync(context, $"{filePath}.br", "br");
            return true;
        }

        if (AcceptsEncoding(context.Request, "gzip") && File.Exists($"{filePath}.gz"))
        {
            await ServeCompressedJavaScriptAsync(context, $"{filePath}.gz", "gzip");
            return true;
        }

        return false;
    }

    private static async Task ServeCompressedJavaScriptAsync(HttpContext context, string filePath, string encoding)
    {
        context.Response.ContentType = "application/javascript";
        context.Response.Headers["Content-Encoding"] = encoding;
        context.Response.Headers["Vary"] = "Accept-Encoding";
        context.Response.ContentLength = new FileInfo(filePath).Length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.SendFileAsync(filePath);
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

    private static string GetString(JsonElement root, string propertyName)
    {
        return GetOptionalString(root, propertyName) ?? string.Empty;
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
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
}

public sealed class HostPortUnavailableException : Exception
{
    public HostPortUnavailableException(string message)
        : base(message)
    {
    }
}
