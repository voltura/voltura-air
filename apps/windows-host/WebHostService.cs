using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

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
    private readonly ScreenViewCoordinator _screenView;
    private readonly ScreenViewCommandHandler _screenViewCommands;
    private readonly PresentationLaserPointerController _presentationLaserPointer;
    private readonly IPowerPointAutomationService _powerPoint;
    private readonly PowerPointPresentationSessionService _presentationSession;
    private readonly PowerPointPresentationCatalog _presentationCatalog;
    private readonly bool _ownsPowerPoint;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly string _listenAddress;
    private readonly RelayHostConnection? _relay;
    private int _inputBlockedByElevation;
    private int _disposeState;
    private WebApplication? _app;

    internal void RevokeCursorOverrides() => _presentationLaserPointer.Revoke();
    internal event EventHandler<ScreenViewActivityChangedEventArgs>? ScreenViewActivityChanged
    {
        add => _screenView.ActivityChanged += value;
        remove => _screenView.ActivityChanged -= value;
    }

    internal void StopScreenViewing() => _screenView.Stop();

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
        CustomScreenService? customScreenService = null,
        IUrlOpenService? urlOpenService = null,
        ITextDestinationService? textDestinationService = null,
        IClipboardTextReader? clipboardTextReader = null,
        Action<CustomPointerSettings>? applyCustomPointer = null,
        Action<bool>? applyPresentationLaserPointer = null,
        IPowerPointAutomationService? powerPointAutomation = null,
        bool isolatedTestMode = false,
        Action<IWebHostBuilder>? configureWebHost = null,
        IScreenViewCaptureSource? screenViewCapture = null)
    {
        _configureWebHost = configureWebHost;

        var settings = AppNetworkSettings.Load();
        TransportMode = settings.TransportMode;
        var usesInMemoryTestServer = isolatedTestMode && configureWebHost is not null;
        var portSelection = SelectPort(
            settings,
            usesInMemoryTestServer,
            IsPortAvailable,
            FindFreePort,
            FindFreeLoopbackPort);
        if (!portSelection.Succeeded)
        {
            throw new HostPortUnavailableException(portSelection.ErrorMessage ?? "The configured Voltura Air port is unavailable.");
        }

        Port = portSelection.Port;
        IsPortSelectionAutomatic = portSelection.IsAutomatic;
        PortSelectionWarning = portSelection.Warning;
        if (portSelection.IsAutomatic && !isolatedTestMode && TransportMode == ConnectionTransportMode.DirectLan)
        {
            AppNetworkSettings.SetLastAutomaticPort(Port);
        }

        if (isolatedTestMode || TransportMode == ConnectionTransportMode.Relay)
        {
            _listenAddress = "127.0.0.1";
            AdvertisedHostAddress = "127.0.0.1";
            SelectedAdapterName = isolatedTestMode ? "Loopback (isolated test)" : "Cloud relay through Voltura";
            IsAdapterSelectionAutomatic = true;
            AddressSelectionWarning = null;
        }
        else
        {
            _listenAddress = "0.0.0.0";
            var addressSelection = LanAddressSelector.Select(LanAddressSelector.GetCandidates(), settings);
            AdvertisedHostAddress = addressSelection?.Address.ToString() ?? GetDnsLanAddressFallback() ?? "127.0.0.1";
            SelectedAdapterName = WebHostNetwork.GetSelectedAdapterName(addressSelection?.Candidate);
            IsAdapterSelectionAutomatic = addressSelection?.UsedManualAddress != true;
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
        CustomScreenService = customScreenService ?? new CustomScreenService(
            isolatedTestMode ? new InMemoryCustomScreenStore() : new CustomScreenStore(),
            resolvedAppLaunchService);
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
        _powerPoint = ResolvePowerPointAutomation(
            powerPointAutomation,
            isolatedTestMode,
            () => new PowerPointAutomationService(_appLog),
            out _ownsPowerPoint);
        _presentationLaserPointer = new PresentationLaserPointerController(
            isolatedTestMode ? null : applyPresentationLaserPointer,
            RestorePowerPointPointer);
        _powerPoint.SnapshotChanged += OnPowerPointSnapshotChanged;
        PresentationReportStore = isolatedTestMode
            ? new InMemoryPresentationReportStore()
            : new PresentationReportStore();
        _presentationCatalog = new(PresentationReportStore);
        _presentationSession = new(
            _powerPoint,
            PresentationReportStore,
            breakOverlay: _powerController as IPresentationBreakOverlay);
        var presentationBlankOverlay =
            _powerController as IPresentationBlankOverlay ??
            NoOpPresentationBlankOverlay.Instance;

        var statusFactory = new HostStatusPayloadFactory(
            pairingManager,
            _powerController,
            _awakeService,
            _workstationLockPolicy,
            resolvedAppLaunchService,
            CustomScreenService,
            resolvedTextDestinationService,
            GetNetworkSnapshot,
            () => IsInputBlockedByElevation,
            () => _presentationLaserPointer.IsEnabled,
            () => presentationBlankOverlay.Snapshot,
            () => _powerPoint.Snapshot,
            () => _presentationSession.Snapshot,
            _presentationCatalog);
        var commandLog = new HostCommandLog(_appLog);
        var powerCommands = new PowerCommandHandler(
            _powerController,
            _workstationLockPolicy,
            statusFactory,
            _transport,
            _appLog);
        var awakeCommands = new AwakeCommandHandler(_awakeService, statusFactory, _transport, _appLog);
        var presentationCommands = new PresentationCommandHandler(
            inputDispatcher,
            statusFactory,
            _presentationLaserPointer,
            _powerPoint,
            _presentationCatalog,
            _presentationSession,
            presentationBlankOverlay,
            pairingManager,
            _transport,
            _appLog);
        var presentationLauncher = new PowerPointPresentationLaunchHandler(
            pairingManager,
            statusFactory,
            _presentationCatalog,
            resolvedAppLaunchService,
            _powerPoint,
            _presentationSession,
            _presentationLaserPointer,
            _transport,
            _appLog);
        var presentationReports = new PresentationReportCommandHandler(
            pairingManager,
            statusFactory,
            PresentationReportStore,
            _transport,
            _appLog);
        var presentationSessions = new PresentationSessionCommandHandler(
            pairingManager,
            statusFactory,
            _presentationSession,
            _transport,
            _appLog);
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
        var customScreenCommands = new CustomScreenCommandHandler(
            CustomScreenService,
            statusFactory,
            inputDispatcher,
            _powerController,
            resolvedAppLaunchService,
            _transport,
            _appLog);
        _screenView = new ScreenViewCoordinator(
            pairingManager,
            statusFactory,
            screenViewCapture ?? (isolatedTestMode ? new UnavailableScreenViewCaptureSource() : null),
            isolatedTestMode ? new IsolatedScreenViewWebRtcPeerFactory() : null,
            _appLog);
        _screenViewCommands = new ScreenViewCommandHandler(_screenView, _transport, GetRelayTurnConfigurationAsync, _appLog);
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
            presentationLauncher,
            presentationReports,
            presentationSessions,
            externalActionCommands,
            textTransferCommands,
            clipboardCommands,
            inputCommands,
            customScreenCommands,
            _screenViewCommands,
            _appLog,
            args => ControllerSocketClosed?.Invoke(this, args));
        _statusBroadcaster = new HostStatusBroadcaster(
            pairingManager,
            _awakeService,
            _workstationLockPolicy,
            _transport,
            statusFactory,
            CustomScreenService,
            _appLog,
            _presentationLaserPointer,
            _powerPoint,
            presentationBlankOverlay);
        if (TransportMode == ConnectionTransportMode.Relay)
        {
#pragma warning disable CA2000 // RelayHostConnection owns and disposes the routing identity.
            var relayIdentity = isolatedTestMode ? RelayRoutingIdentity.CreateEphemeral() : RelayRoutingIdentity.OpenCurrentUser();
#pragma warning restore CA2000
            RelayEndpoint = RelayEndpointDescriptor.FromSettings(settings);
            _relay = new RelayHostConnection(RelayEndpoint, relayIdentity, HandleRelaySessionAsync, _appLog);
            _relay.StateChanged += OnRelayStateChanged;
        }
        _sessionHandler.StatusRefreshRequested += (_, _) => _statusBroadcaster.Queue();
        _presentationSession.StateChanged += OnPresentationSessionChanged;
        _presentationCatalog.Changed += OnPresentationCatalogChanged;
    }

    public int Port { get; }
    internal IPresentationReportStore PresentationReportStore { get; }
    internal PowerPointSessionSnapshot PresentationSessionSnapshot =>
        _presentationSession.Snapshot;
    public string ServerUrl { get; private set; }
    public string WebSocketUrl => BuildWebSocketUrl(AdvertisedHostAddress, Port);
    public string AdvertisedHostAddress { get; private set; }
    public string SelectedAdapterName { get; private set; }
    public string? AddressSelectionWarning { get; }
    public string? PortSelectionWarning { get; }
    internal ConnectionTransportMode TransportMode { get; }
    internal RelayEndpointDescriptor? RelayEndpoint { get; }
    internal string? RelayRouteId => _relay?.RouteId;
    internal RelayConnectionState RelayState => _relay?.State ?? RelayConnectionState.Disabled;
    internal string? RelayFailureCode => _relay?.FailureCode;
    internal RelayUsageSnapshot? RelayUsage => _relay?.LastUsage;
    internal long? RelayUsageBytes => RelayUsage?.Bytes;
    internal DateTimeOffset? RelayUsageCheckedAt => RelayUsage?.CheckedAt;
    internal long? RelayUsageWarningBytes => RelayUsage?.WarningBytes;
    internal long? RelayUsageCutoffBytes => RelayUsage?.CutoffBytes;
    internal bool IsAdapterSelectionAutomatic { get; }
    internal bool IsPortSelectionAutomatic { get; }
    internal string ListenAddress => _listenAddress;
    internal WebApplication? Application => _app;
    internal IWorkstationLockPolicy WorkstationLockPolicy => _workstationLockPolicy;
    internal ISystemPowerController PowerController => _powerController;
    internal IAwakeService AwakeService => _awakeService;
    internal IAppLaunchService AppLaunchService { get; }
    internal CustomScreenService CustomScreenService { get; }
    internal IAppLog AppLog => _appLog;
    internal int ActiveSocketCount => _transport.ActiveSocketCount;
    internal int SendGateCount => _transport.SendGateCount;
    internal bool IsInputBlockedByElevation => Volatile.Read(ref _inputBlockedByElevation) != 0;

    public event EventHandler<ControllerSocketClosedEventArgs>? ControllerSocketClosed;
    internal event EventHandler<RemoteInputBlockedChangedEventArgs>? RemoteInputBlockedChanged;
    internal event EventHandler? PresentationSessionChanged;
    internal event EventHandler<RelayStatusChangedEventArgs>? RelayStatusChanged;

    internal void RetryRelay() => _relay?.Retry();

    internal async Task RefreshRelayUsageAsync(CancellationToken cancellationToken = default)
    {
        await GetRelayTurnConfigurationAsync(cancellationToken);
    }

    internal Task<SessionOperationResult> CompletePresentationSessionFromHostAsync(
        bool save,
        CancellationToken cancellationToken)
    {
        var owner = _presentationSession.Snapshot.OwnerClientId;
        return owner is null
            ? Task.FromResult(new SessionOperationResult(
                false,
                "session-unavailable",
                "There is no presentation draft to finish."))
            : _presentationSession.CompleteAsync(owner, save, cancellationToken);
    }

    internal static bool IsPortAvailable(int port) => WebHostNetwork.IsPortAvailable(port);
    internal static int FindFreePort() => WebHostNetwork.FindFreePort();
    internal static int FindFreeLoopbackPort() => WebHostNetwork.FindFreeLoopbackPort();
    internal static PortSelectionResult SelectPort(
        NetworkSettingsSnapshot settings,
        bool usesInMemoryTestServer,
        Func<int, bool> isPortAvailable,
        Func<int> findFreePort,
        Func<int> findFreeLoopbackPort) =>
        usesInMemoryTestServer
            ? new PortSelectionResult(true, PortSelector.PreferredPort, IsAutomatic: true, ErrorMessage: null)
            : settings.TransportMode == ConnectionTransportMode.Relay
                ? new PortSelectionResult(true, findFreeLoopbackPort(), IsAutomatic: true, ErrorMessage: null)
                : PortSelector.Select(settings, isPortAvailable, findFreePort);
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
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
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

        MapCustomScreenPreview(app);
        MapStaticFiles(app);
        _app = app;
        await app.StartAsync();
        _relay?.Start();
    }

    public async Task StopAsync()
    {
        await _screenViewCommands.DisposeAsync();
        await _screenView.DisposeAsync();
        _transport.AbortAll();
        if (_relay is not null)
        {
            await _relay.DisposeAsync();
        }
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
        await _screenViewCommands.DisposeAsync();
        await _screenView.DisposeAsync();
        _presentationCatalog.Changed -= OnPresentationCatalogChanged;
        _presentationCatalog.Dispose();
        _presentationSession.StateChanged -= OnPresentationSessionChanged;
        _presentationSession.Dispose();
        _powerPoint.SnapshotChanged -= OnPowerPointSnapshotChanged;
        if (_presentationLaserPointer.RuntimePresentationId is { Length: > 0 } runtimeId)
        {
            try
            {
                _ = await _powerPoint.ExecuteAsync(
                    new("pointer", runtimeId, Enabled: false),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "powerpoint_pointer_shutdown_restore",
                    Outcome: "failed",
                    Detail: exception.Message));
            }

            _presentationLaserPointer.Revoke(restorePowerPoint: false);
        }

        _presentationLaserPointer.Dispose();
        if (_ownsPowerPoint)
        {
            await _powerPoint.DisposeAsync();
        }

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
            if (_relay is not null)
            {
                _relay.StateChanged -= OnRelayStateChanged;
                await _relay.DisposeAsync();
            }
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

    private async Task HandleRelaySessionAsync(WebSocket socket, string rateLimitKey, CancellationToken cancellationToken)
    {
        if (!await _webSocketSessionSlots.WaitAsync(0, cancellationToken))
        {
            await WebSocketTransport.CloseAsync(
                socket,
                "Relay session limit reached",
                WebSocketCloseStatus.EndpointUnavailable,
                cancellationToken);
            return;
        }

        try
        {
            await _sessionHandler.HandleAsync(socket, rateLimitKey, cancellationToken);
        }
        finally
        {
            _webSocketSessionSlots.Release();
        }
    }

    private void OnRelayStateChanged(object? sender, EventArgs e)
    {
        _statusBroadcaster.Queue();
        RelayStatusChanged?.Invoke(this, new RelayStatusChangedEventArgs(RelayState, RelayFailureCode));
    }

    private Task<RelayTurnConfiguration?> GetRelayTurnConfigurationAsync(CancellationToken cancellationToken)
    {
        var relay = _relay;
        if (relay is null) return Task.FromResult<RelayTurnConfiguration?>(null);
        var quality = AppNetworkSettings.Load().RelayScreenQuality;
        return relay.GetTurnConfigurationAsync(quality, cancellationToken);
    }

    internal static IPowerPointAutomationService ResolvePowerPointAutomation(
        IPowerPointAutomationService? supplied,
        bool isolatedTestMode,
        Func<IPowerPointAutomationService> createActive,
        out bool ownsPowerPoint)
    {
        if (supplied is not null)
        {
            ownsPowerPoint = false;
            return supplied;
        }

        if (isolatedTestMode)
        {
            ownsPowerPoint = false;
            return InertPowerPointAutomationService.Instance;
        }

        ownsPowerPoint = true;
        return createActive();
    }

    private void OnPowerPointSnapshotChanged(object? sender, EventArgs eventArgs)
    {
        if (_presentationLaserPointer.RuntimePresentationId is not { Length: > 0 } runtimeId)
        {
            return;
        }

        var presentation = _powerPoint.Snapshot.Presentations.FirstOrDefault(
            item => string.Equals(
                item.RuntimePresentationId,
                runtimeId,
                StringComparison.Ordinal));
        if (presentation is null || !presentation.IsPresenting)
        {
            _presentationLaserPointer.DisableForPresentation(runtimeId);
        }
    }

    private void OnPresentationSessionChanged(object? sender, EventArgs eventArgs)
    {
        _statusBroadcaster.Queue();
        PresentationSessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPresentationCatalogChanged(object? sender, EventArgs eventArgs) =>
        _statusBroadcaster.Queue();

    private void RestorePowerPointPointer(string runtimePresentationId)
    {
        _ = RestorePowerPointPointerAsync(runtimePresentationId);
    }

    private async Task RestorePowerPointPointerAsync(string runtimePresentationId)
    {
        try
        {
            var result = await _powerPoint.ExecuteAsync(
                new("pointer", runtimePresentationId, Enabled: false),
                CancellationToken.None).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "powerpoint_pointer_restore",
                    Outcome: "degraded",
                    Detail: result.Code));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "powerpoint_pointer_restore",
                Outcome: "failed",
                Detail: exception.Message));
        }
    }

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

    private void MapCustomScreenPreview(WebApplication app)
    {
        app.MapGet("/api/custom-screens/preview/{screenId}", (
            HttpContext context,
            string screenId) =>
        {
            if (!IsLoopbackAddress(context.Connection.RemoteIpAddress))
            {
                return Results.NotFound();
            }

            var definition = CustomScreenService.GetPreviewDefinition(screenId);
            if (definition is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new
            {
                type = "custom.screen.get.result",
                operationId = "preview",
                succeeded = true,
                screen = definition
            }, JsonOptions.Default);
        });
    }

    internal static bool IsLoopbackAddress(IPAddress? address) =>
        address is not null &&
        (IPAddress.IsLoopback(address) ||
         address.IsIPv4MappedToIPv6 &&
         IPAddress.IsLoopback(address.MapToIPv4()));
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
    bool ControlDepth,
    bool DeveloperMode,
    string? DeveloperSessionId,
    bool InputBlockedByElevation);

internal sealed record TextTransferTargetMetadata(string Mode, string DisplayName, bool Available);

public sealed class HostPortUnavailableException(string message) : Exception(message);
