using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace VolturaAir.Host;

public sealed class WebHostService : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan PairingHandshakeTimeout = WebSocketSessionHandler.PairingHandshakeTimeout;
    internal static readonly TimeSpan AuthenticatedInactivityTimeout = WebSocketSessionHandler.AuthenticatedInactivityTimeout;
    internal const int MaxWebSocketMessageBytes = WebSocketTransport.MaxMessageBytes;
    private const int MaxConcurrentWebSocketSessions = 64;

    private readonly ISystemPowerController _powerController;
    private readonly IAwakeService _awakeService;
    private readonly IWorkstationLockPolicy _workstationLockPolicy;
    private readonly IAppLog _appLog;
    private readonly bool _ownsAppLog;
    private readonly WebSocketTransport _transport = new();
    private readonly SemaphoreSlim _webSocketSessionSlots = new(MaxConcurrentWebSocketSessions, MaxConcurrentWebSocketSessions);
    private readonly HostStatusBroadcaster _statusBroadcaster;
    private readonly WebSocketSessionHandler _sessionHandler;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly string _listenAddress;
    private int _inputBlockedByElevation;
    private int _disposeState;
    private WebApplication? _app;

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
        _configureWebHost = configureWebHost;

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
            SelectedAdapterName = WebHostNetwork.GetSelectedAdapterName(addressSelection?.Candidate);
            AddressSelectionWarning = addressSelection?.Warning;
            if (settings.NetworkMode == NetworkSelectionMode.Automatic)
            {
                AppNetworkSettings.SetLastAutomaticHostAddress(AdvertisedHostAddress);
            }
        }

        ServerUrl = BuildServerUrl(AdvertisedHostAddress, Port);

        var resolvedAudioController = audioController ?? new SystemAudioController();
        var resolvedRemoteActionExecutor = remoteActionExecutor ?? new RemoteActionExecutor();
        var resolvedAppLaunchService = appLaunchService ?? new AppLaunchService();
        AppLaunchService = resolvedAppLaunchService;
        var resolvedUrlOpenService = urlOpenService ?? new UrlOpenService();
        var resolvedTextDestinationService = textDestinationService ?? new FocusedTextDestinationService(inputDispatcher);
        var resolvedClipboardTextReader = clipboardTextReader ?? new WindowsClipboardTextReader();
        _powerController = powerController ?? (isolatedTestMode ? new NoOpSystemPowerController() : new SystemPowerController());
        _ownsAppLog = appLog is null && !isolatedTestMode;
        _appLog = appLog ?? (isolatedTestMode ? NullAppLog.Instance : new AppLog());
        _awakeService = awakeService ?? (isolatedTestMode
            ? new NoOpAwakeService()
            : throw new ArgumentNullException(nameof(awakeService), "Production host composition must provide the Awake service."));
        _workstationLockPolicy = workstationLockPolicy ?? new WorkstationLockPolicy(_appLog);

        var statusFactory = new HostStatusPayloadFactory(
            pairingManager,
            _powerController,
            _awakeService,
            _workstationLockPolicy,
            resolvedAppLaunchService,
            resolvedTextDestinationService,
            GetNetworkSnapshot,
            () => IsInputBlockedByElevation);
        var commandLog = new HostCommandLog(_appLog);
        var powerCommands = new PowerCommandHandler(
            _powerController,
            _workstationLockPolicy,
            statusFactory,
            _transport,
            _appLog);
        var awakeCommands = new AwakeCommandHandler(_awakeService, statusFactory, _transport, _appLog);
        var presentationCommands = new PresentationCommandHandler(inputDispatcher, statusFactory, _transport, _appLog);
        var externalActionCommands = new ExternalActionCommandHandler(
            resolvedRemoteActionExecutor,
            resolvedAppLaunchService,
            resolvedUrlOpenService,
            statusFactory,
            commandLog,
            _transport,
            _appLog);
        var textTransferCommands = new TextTransferCommandHandler(
            resolvedTextDestinationService,
            _powerController,
            statusFactory,
            commandLog,
            _transport);
        var clipboardCommands = new ClipboardCommandHandler(
            resolvedClipboardTextReader,
            statusFactory,
            commandLog,
            _transport);
        var inputCommands = new InputCommandHandler(inputDispatcher, _powerController, commandLog, _transport);
        // An isolated browser may exercise the protocol, but it must never call
        // the native cursor API on the developer's Windows session.
        var resolvedApplyCustomPointer = isolatedTestMode ? null : applyCustomPointer;
        _sessionHandler = new WebSocketSessionHandler(
            pairingManager,
            resolvedAudioController,
            resolvedApplyCustomPointer,
            statusFactory,
            commandLog,
            _transport,
            powerCommands,
            awakeCommands,
            presentationCommands,
            externalActionCommands,
            textTransferCommands,
            clipboardCommands,
            inputCommands,
            _appLog,
            args => ControllerSocketClosed?.Invoke(this, args));
        _statusBroadcaster = new HostStatusBroadcaster(
            pairingManager,
            _awakeService,
            _workstationLockPolicy,
            _transport,
            statusFactory,
            _appLog);
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
    internal IAppLaunchService AppLaunchService { get; }
    internal IAppLog AppLog => _appLog;
    internal int ActiveSocketCount => _transport.ActiveSocketCount;
    internal int SendGateCount => _transport.SendGateCount;
    internal bool IsInputBlockedByElevation => Volatile.Read(ref _inputBlockedByElevation) != 0;

    public event EventHandler<ControllerSocketClosedEventArgs>? ControllerSocketClosed;
    internal event EventHandler<RemoteInputBlockedChangedEventArgs>? RemoteInputBlockedChanged;

    internal static bool IsPortAvailable(int port) => WebHostNetwork.IsPortAvailable(port);
    internal static int FindFreePort() => WebHostNetwork.FindFreePort();
    internal static string? GetDnsLanAddressFallback() => WebHostNetwork.GetDnsLanAddressFallback();
    internal static string BuildServerUrl(string hostAddress, int port) => WebHostNetwork.BuildServerUrl(hostAddress, port);
    internal static string BuildWebSocketUrl(string hostAddress, int port) => WebHostNetwork.BuildWebSocketUrl(hostAddress, port);
    internal static bool IsAllowedWebSocketOrigin(HttpRequest request) => WebHostNetwork.IsAllowedWebSocketOrigin(request);

    internal void SetInputBlockedByElevation(bool blocked)
    {
        if (Interlocked.Exchange(ref _inputBlockedByElevation, blocked ? 1 : 0) == (blocked ? 1 : 0))
        {
            return;
        }

        RemoteInputBlockedChanged?.Invoke(this, new RemoteInputBlockedChangedEventArgs(blocked));
        _statusBroadcaster.Queue();
    }

    internal void UpdateAdvertisedHostAddress(string hostAddress, LanAddressCandidate? selectedCandidate = null)
    {
        AdvertisedHostAddress = hostAddress;
        SelectedAdapterName = WebHostNetwork.GetSelectedAdapterName(selectedCandidate);
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
                await _sessionHandler.HandleAsync(socket, WebHostNetwork.GetRateLimitKey(context), context.RequestAborted);
            }
            finally
            {
                _webSocketSessionSlots.Release();
            }
        });

        MapStaticFiles(app);
        _app = app;
        await app.StartAsync();
    }

    public async Task StopAsync()
    {
        _transport.AbortAll();
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

        await _statusBroadcaster.DisposeAsync();
        _transport.AbortAll();
        try
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
        finally
        {
            _transport.Dispose();
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
                    await _awakeService.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        _webSocketSessionSlots.Dispose();
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

    private HostNetworkSnapshot GetNetworkSnapshot() => new(
        SelectedAdapterName,
        AdvertisedHostAddress,
        Port,
        WebSocketUrl);

    private static void MapStaticFiles(WebApplication app)
    {
        var staticRoot = WebHostStaticFiles.ResolveStaticRoot();
        if (!Directory.Exists(staticRoot))
        {
            app.MapGet("/", () => Results.Text("Mobile web build missing. Run: npm run build --workspace apps/mobile-web", "text/plain"));
            return;
        }

        var fileProvider = new PhysicalFileProvider(staticRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.Use(async (context, next) =>
        {
            if (!await WebHostStaticFiles.TryServeCompressedJavaScriptAsync(context, staticRoot))
            {
                await next();
            }
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            OnPrepareResponse = context => WebHostStaticFiles.SetStaticCacheHeaders(
                context.Context.Response,
                context.Context.Request.Path.Value)
        });
        app.MapFallback(async context =>
        {
            WebHostStaticFiles.SetStaticCacheHeaders(context.Response, "index.html");
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(Path.Combine(staticRoot, "index.html"));
        });
    }
}

public sealed class ControllerSocketClosedEventArgs(
    string clientId,
    string reason,
    WebSocketCloseStatus status) : EventArgs
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
    bool ShowModeButtons,
    bool DeveloperMode,
    string? DeveloperSessionId,
    bool InputBlockedByElevation);

internal sealed record TextTransferTargetMetadata(string Mode, string DisplayName, bool Available);
internal sealed record PresentationCommandResult(bool Succeeded, string? Code, string Message);

public sealed class HostPortUnavailableException(string message) : Exception(message);
