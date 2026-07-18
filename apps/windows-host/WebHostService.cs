using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace VolturaAir.Host;

public sealed partial class WebHostService : IAsyncDisposable
{
    private static readonly string DeveloperSessionId = Guid.NewGuid().ToString("N");
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan PairingHandshakeTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan AuthenticatedInactivityTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan WebSocketCloseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WebSocketSendTimeout = TimeSpan.FromSeconds(5);
    internal const int MaxWebSocketMessageBytes = 64 * 1024;
    private const int MaxConcurrentWebSocketSessions = 64;
    private readonly PairingManager _pairingManager;
    private readonly InputDispatcher _inputDispatcher;
    private readonly ISystemAudioController _audioController;
    private readonly IRemoteActionExecutor _remoteActionExecutor;
    private readonly IAppLaunchService _appLaunchService;
    private readonly IUrlOpenService _urlOpenService;
    private readonly ITextDestinationService _textDestinationService;
    private readonly IClipboardTextReader _clipboardTextReader;
    private readonly ISystemPowerController _powerController;
    private readonly IAwakeService _awakeService;
    private readonly IWorkstationLockPolicy _workstationLockPolicy;
    private readonly IAppLog _appLog;
    private readonly bool _ownsAppLog;
    private readonly PairingAttemptRateLimiter _pairingAttemptRateLimiter = new();
    private readonly WebSocketConnectionRegistry _connections = new();
    private readonly SemaphoreSlim _webSocketSessionSlots = new(MaxConcurrentWebSocketSessions, MaxConcurrentWebSocketSessions);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Channel<bool> _statusBroadcastRequests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly Action<CustomPointerSettings>? _applyCustomPointer;
    private readonly string _listenAddress;
    private int _inputBlockedByElevation;
    private int _disposeState;
    private WebApplication? _app;
    private Task _statusBroadcastTask = Task.CompletedTask;

    public WebHostService(
        PairingManager pairingManager,
        InputDispatcher inputDispatcher,
        ISystemAudioController? audioController = null,
        IRemoteActionExecutor? remoteActionExecutor = null,
        ISystemPowerController? powerController = null,
        IAwakeService? awakeService = null,
        IWorkstationLockPolicy? workstationLockPolicy = null,
        IAppLog? appLog = null,
        IAppLaunchService? appLaunchService = null,
        IUrlOpenService? urlOpenService = null,
        ITextDestinationService? textDestinationService = null,
        IClipboardTextReader? clipboardTextReader = null,
        Action<CustomPointerSettings>? applyCustomPointer = null,
        bool isolatedTestMode = false,
        Action<IWebHostBuilder>? configureWebHost = null)
    {
        _pairingManager = pairingManager;
        _inputDispatcher = inputDispatcher;
        _audioController = audioController ?? new SystemAudioController();
        _remoteActionExecutor = remoteActionExecutor ?? new RemoteActionExecutor();
        _appLaunchService = appLaunchService ?? new AppLaunchService();
        _urlOpenService = urlOpenService ?? new UrlOpenService();
        _textDestinationService = textDestinationService ?? new FocusedTextDestinationService(inputDispatcher);
        _clipboardTextReader = clipboardTextReader ?? new WindowsClipboardTextReader();
        _powerController = powerController ?? (isolatedTestMode ? new NoOpSystemPowerController() : new SystemPowerController());
        _ownsAppLog = appLog is null && !isolatedTestMode;
        _appLog = appLog ?? (isolatedTestMode ? NullAppLog.Instance : new AppLog());
        _awakeService = awakeService ?? (isolatedTestMode
            ? new NoOpAwakeService()
            : VolturaAir.Host.AwakeService.CreateWindows(_appLog));
        _workstationLockPolicy = workstationLockPolicy ?? new WorkstationLockPolicy(_appLog);
        _configureWebHost = configureWebHost;
        // An isolated browser may exercise the protocol, but it must never
        // call the native cursor API on the developer's Windows session.
        _applyCustomPointer = isolatedTestMode ? null : applyCustomPointer;
        var settings = AppNetworkSettings.Load();
        var usesInMemoryTestServer = isolatedTestMode && configureWebHost is not null;
        var portSelection = usesInMemoryTestServer
            ? new PortSelectionResult(true, PortSelector.PreferredPort, IsAutomatic: true, ErrorMessage: null)
            : PortSelector.Select(settings, IsPortAvailable, FindFreePort);
        if (!portSelection.Succeeded)
        {
            throw new HostPortUnavailableException(portSelection.ErrorMessage ?? "The configured Voltura Air port is unavailable.");
        }

        Port = portSelection.Port;
        PortSelectionWarning = portSelection.Warning;
        if (portSelection.IsAutomatic && !isolatedTestMode)
        {
            AppNetworkSettings.SetLastAutomaticPort(Port);
        }

        if (isolatedTestMode)
        {
            _listenAddress = "127.0.0.1";
            AdvertisedHostAddress = "127.0.0.1";
            SelectedAdapterName = "Loopback (isolated test)";
            AddressSelectionWarning = null;
        }
        else
        {
            _listenAddress = "0.0.0.0";
            var addressSelection = LanAddressSelector.Select(LanAddressSelector.GetCandidates(), settings);
            AdvertisedHostAddress = addressSelection?.Address.ToString() ?? GetDnsLanAddressFallback() ?? "127.0.0.1";
            SelectedAdapterName = GetSelectedAdapterName(addressSelection?.Candidate);
            AddressSelectionWarning = addressSelection?.Warning;
            if (settings.NetworkMode == NetworkSelectionMode.Automatic)
            {
                AppNetworkSettings.SetLastAutomaticHostAddress(AdvertisedHostAddress);
            }
        }

        ServerUrl = BuildServerUrl(AdvertisedHostAddress, Port);
        _pairingManager.PairingRevoked += OnPairingRevoked;
        _pairingManager.PermissionsChanged += OnPermissionsChanged;
        _pairingManager.DeviceProfileChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
        AppDeveloperSettings.Changed += OnPermissionsChanged;
        AppRemoteSettings.Changed += OnPermissionsChanged;
        AppLaunchSettings.Changed += OnPermissionsChanged;
        AppTextDestinationSettings.Changed += OnPermissionsChanged;
        AppPointerSettings.Changed += OnPermissionsChanged;
        _workstationLockPolicy.Changed += OnPermissionsChanged;
        _awakeService.StateChanged += OnAwakeStateChanged;
        _statusBroadcastTask = ProcessStatusBroadcastsAsync();
    }

    public int Port { get; }

    public string ServerUrl { get; private set; }

    public string WebSocketUrl => BuildWebSocketUrl(AdvertisedHostAddress, Port);

    public string AdvertisedHostAddress { get; private set; }

    public string SelectedAdapterName { get; private set; }

    public string? AddressSelectionWarning { get; }

    public string? PortSelectionWarning { get; }

    internal string ListenAddress => _listenAddress;

    internal WebApplication? Application => _app;

    internal IWorkstationLockPolicy WorkstationLockPolicy => _workstationLockPolicy;

    internal ISystemPowerController PowerController => _powerController;

    internal IAwakeService AwakeService => _awakeService;

    internal IAppLog AppLog => _appLog;

    internal int ActiveSocketCount => _connections.ActiveSocketCount;

    internal int SendGateCount => _connections.SendGateCount;

    public event EventHandler<ControllerSocketClosedEventArgs>? ControllerSocketClosed;

    internal event EventHandler<RemoteInputBlockedChangedEventArgs>? RemoteInputBlockedChanged;

    internal bool IsInputBlockedByElevation => Volatile.Read(ref _inputBlockedByElevation) != 0;

    internal void SetInputBlockedByElevation(bool blocked)
    {
        if (Interlocked.Exchange(ref _inputBlockedByElevation, blocked ? 1 : 0) == (blocked ? 1 : 0))
        {
            return;
        }

        RemoteInputBlockedChanged?.Invoke(this, new RemoteInputBlockedChangedEventArgs(blocked));
        QueueStatusBroadcast();
    }

    internal void UpdateAdvertisedHostAddress(string hostAddress, LanAddressCandidate? selectedCandidate = null)
    {
        AdvertisedHostAddress = hostAddress;
        SelectedAdapterName = GetSelectedAdapterName(selectedCandidate);
        ServerUrl = BuildServerUrl(hostAddress, Port);
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        if (_configureWebHost is null)
        {
            builder.WebHost.UseUrls($"http://{_listenAddress}:{Port}");
        }
        else
        {
            _configureWebHost(builder.WebHost);
        }
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

            if (!_webSocketSessionSlots.Wait(0))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await HandleSocketAsync(socket, GetRateLimitKey(context), context.RequestAborted);
            }
            finally
            {
                _webSocketSessionSlots.Release();
            }
        });

        var staticRoot = WebHostStaticFiles.ResolveStaticRoot();
        if (Directory.Exists(staticRoot))
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(staticRoot)
            });
            app.Use(async (context, next) =>
            {
                if (await WebHostStaticFiles.TryServeCompressedJavaScriptAsync(context, staticRoot))
                {
                    return;
                }

                await next();
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(staticRoot),
                OnPrepareResponse = context => WebHostStaticFiles.SetStaticCacheHeaders(context.Context.Response, context.Context.Request.Path.Value)
            });
            app.MapFallback(async context =>
            {
                WebHostStaticFiles.SetStaticCacheHeaders(context.Response, "index.html");
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _pairingManager.PairingRevoked -= OnPairingRevoked;
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        _pairingManager.DeviceProfileChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        AppDeveloperSettings.Changed -= OnPermissionsChanged;
        AppRemoteSettings.Changed -= OnPermissionsChanged;
        AppLaunchSettings.Changed -= OnPermissionsChanged;
        AppTextDestinationSettings.Changed -= OnPermissionsChanged;
        AppPointerSettings.Changed -= OnPermissionsChanged;
        _workstationLockPolicy.Changed -= OnPermissionsChanged;
        _awakeService.StateChanged -= OnAwakeStateChanged;
        _statusBroadcastRequests.Writer.TryComplete();
        await _lifetimeCancellation.CancelAsync();
        try
        {
            AbortActiveSockets();
            await _statusBroadcastTask;
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
        finally
        {
            _connections.Dispose();
            try
            {
                if (_powerController is IDisposable disposablePowerController)
                {
                    disposablePowerController.Dispose();
                }
            }
            finally
            {
                try
                {
                    _awakeService.Dispose();
                }
                finally
                {
                    try
                    {
                        _webSocketSessionSlots.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            _lifetimeCancellation.Dispose();
                        }
                        finally
                        {
                            if (_ownsAppLog && _appLog is IAsyncDisposable asyncDisposableAppLog)
                            {
                                await asyncDisposableAppLog.DisposeAsync();
                            }
                        }
                    }
                }
            }
        }
    }


}

public sealed class ControllerSocketClosedEventArgs(string clientId, string reason, WebSocketCloseStatus status) : EventArgs
{
    public string ClientId { get; } = clientId;

    public string Reason { get; } = reason;

    public WebSocketCloseStatus Status { get; } = status;
}

internal sealed record HostStatusMetadata(
    string HostVersion,
    string? WebClientBuildId,
    string PcName,
    string SelectedAdapterName,
    string SelectedIp,
    int SelectedPort,
    string WebSocketUrl,
    string DefaultRemoteMode,
    IReadOnlyList<AppLaunchActionSummary> AppLaunchActions,
    TextTransferTargetMetadata TextTransferTarget,
    int PointerSpeed,
    bool CustomPointerEnabled,
    bool DeveloperMode,
    string? DeveloperSessionId,
    bool InputBlockedByElevation);

internal sealed record TextTransferTargetMetadata(string Mode, string DisplayName, bool Available);

internal sealed record PresentationCommandResult(bool Succeeded, string? Code, string Message);

public sealed class HostPortUnavailableException(string message) : Exception(message)
{
}
