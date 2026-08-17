using Microsoft.Win32;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host;

internal sealed class WpfHostRuntime : IAsyncDisposable
{
    private readonly SendInputInjector _inputInjector;
    private readonly IActivitySimulationService _activitySimulationService;
    private readonly CursorOverrideCoordinator _cursorOverrides;
    private readonly EventHandler _cursorOverridesRevoked;
    private readonly PointerHighlightForegroundMonitor _pointerHighlightForegroundMonitor;
    private readonly IAsyncDisposable _textDestinationDraftCleanup;
    private readonly IAsyncDisposable _presentationEmailDraftCleanup;
    private readonly IAsyncDisposable? _phoneWebcamFeature;
    private readonly WebHostService _webHost;
    private readonly WpfTrayApplicationContext _trayContext;
    private readonly IAppLog _appLog;
    private readonly SessionSwitchEventHandler? _screenViewSessionSwitch;
    private int _disposeState;

    private WpfHostRuntime(
        SendInputInjector inputInjector,
        IActivitySimulationService activitySimulationService,
        CursorOverrideCoordinator cursorOverrides,
        EventHandler cursorOverridesRevoked,
        PointerHighlightForegroundMonitor pointerHighlightForegroundMonitor,
        IAsyncDisposable textDestinationDraftCleanup,
        IAsyncDisposable presentationEmailDraftCleanup,
        IAsyncDisposable? phoneWebcamFeature,
        WebHostService webHost,
        PairingManager pairingManager,
        MainWindow mainWindow,
        WpfTrayApplicationContext trayContext,
        IAppLog appLog,
        SessionSwitchEventHandler? screenViewSessionSwitch)
    {
        _inputInjector = inputInjector;
        _activitySimulationService = activitySimulationService;
        _cursorOverrides = cursorOverrides;
        _cursorOverridesRevoked = cursorOverridesRevoked;
        _pointerHighlightForegroundMonitor = pointerHighlightForegroundMonitor;
        _textDestinationDraftCleanup = textDestinationDraftCleanup;
        _presentationEmailDraftCleanup = presentationEmailDraftCleanup;
        _phoneWebcamFeature = phoneWebcamFeature;
        _webHost = webHost;
        PairingManager = pairingManager;
        MainWindow = mainWindow;
        _trayContext = trayContext;
        _appLog = appLog;
        _screenViewSessionSwitch = screenViewSessionSwitch;
    }

    public PairingManager PairingManager { get; }

    public MainWindow MainWindow { get; }

    public static async Task<WpfHostRuntime> StartAsync(string[] args, Action requestShutdown, Action requestRestart)
    {
        var isolatedTestMode = HasOption(args, "--isolated-test-mode");
#if DEBUG
        var requestedPairingStoreRoot = GetOption(args, "--pairing-store-root");
        var pairingStoreRoot = string.IsNullOrWhiteSpace(requestedPairingStoreRoot)
            ? null
            : ResolveIsolatedAutomationPath(
                args,
                requestedPairingStoreRoot,
                "appdata");
        var clientUrl = GetOption(args, "--client-url") ?? Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        var usePublicScreenshotPairingUrl = HasOption(args, "--site-screenshot-mode");
#else
        string? pairingStoreRoot = null;
        string? clientUrl = null;
        const bool usePublicScreenshotPairingUrl = false;
#endif
        IAppLog appLog = isolatedTestMode ? NullAppLog.Instance : new AppLog();
        SendInputInjector? inputInjector = null;
        IActivitySimulationService? activitySimulationService = null;
        CursorWatchdogService? cursorWatchdogService = null;
        CustomPointerService? customPointerService = null;
        CursorOverrideCoordinator? cursorOverrides = null;
        IAsyncDisposable? textDestinationDraftCleanup = null;
        IAsyncDisposable? presentationEmailDraftCleanup = null;
        PhoneWebcamFeature? phoneWebcamFeature = null;
        ISystemPowerController? powerController = null;
        IAwakeService? awakeService = null;
        WebHostService? webHost = null;
        PointerHighlightForegroundMonitor? pointerHighlightForegroundMonitor = null;
        MainWindow? mainWindow = null;
        WpfTrayApplicationContext? trayContext = null;
        PairingManager? pairingManager = null;
        SessionSwitchEventHandler? screenViewSessionSwitch = null;
        try
        {
            pairingManager = new PairingManager(new PairingStore(pairingStoreRoot));
            inputInjector = new SendInputInjector();
            activitySimulationService = new ActivitySimulationService(
                isolatedTestMode ? NoOpActivityPulseSender.Instance : inputInjector,
                AppActivitySimulationSettings.Load(),
                appLog: appLog);
            cursorWatchdogService = new CursorWatchdogService();
            customPointerService = new CustomPointerService();
            cursorOverrides = new CursorOverrideCoordinator(
                cursorWatchdogService,
                customPointerService,
                appLog);
            await cursorOverrides.StartAsync();
            textDestinationDraftCleanup = TextDestinationDraftStore.CreateCleanupService(appLog);
            presentationEmailDraftCleanup =
                new Features.Presentations.PresentationEmailDraftCleanup(appLog);
            IPhoneWebcamFeature phoneWebcam = PhoneWebcamFeature.CreateUnavailable();
            if (!isolatedTestMode)
            {
                phoneWebcamFeature = await PhoneWebcamFeature.CreateAsync();
                phoneWebcam = phoneWebcamFeature;
            }
            var inputDispatcher = new InputDispatcher(inputInjector);
            var workstationLockPolicy = new WorkstationLockPolicy(appLog);
            powerController = isolatedTestMode
                ? new NoOpSystemPowerController()
                : new SystemPowerController(new WindowsDisplayActionController(
                    System.Windows.Application.Current.Dispatcher,
                    appLog,
                    () => trayContext?.ShowPresentationBreakReminder()));
            awakeService = isolatedTestMode
                ? new NoOpAwakeService()
                : await AwakeService.CreateWindowsAsync(appLog);
            webHost = new WebHostService(
                pairingManager,
                inputDispatcher,
                audioController: null,
                remoteActionExecutor: null,
                powerController: powerController,
                awakeService: awakeService,
                workstationLockPolicy: workstationLockPolicy,
                appLog: appLog,
                appLaunchService: null,
                customScreenService: null,
                urlOpenService: null,
                textDestinationService: new TextDestinationService(inputDispatcher, inputInjector),
                clipboardTextReader: null,
                applyCustomPointer: cursorOverrides.ApplyCustomPointer,
                applyPresentationLaserPointer: cursorOverrides.SetPresentationLaserPointer,
                powerPointAutomation: null,
                isolatedTestMode: isolatedTestMode,
                configureWebHost: null,
                screenViewCapture: null,
                phoneWebcamFeature: phoneWebcam,
                phoneWebcamPeerFactory: null);
            EventHandler cursorOverridesRevoked = (_, _) => webHost.RevokeCursorOverrides();
            cursorOverrides.OverridesRevoked += cursorOverridesRevoked;

            pointerHighlightForegroundMonitor = new PointerHighlightForegroundMonitor(appLog);
            pointerHighlightForegroundMonitor.RemoteInputBlockedChanged += (_, eventArgs) =>
                webHost.SetInputBlockedByElevation(eventArgs.IsBlocked);
            inputDispatcher.TaskbarActivated += (_, _) => pointerHighlightForegroundMonitor.NotifyTaskbarActivation();
            webHost.SetInputBlockedByElevation(pointerHighlightForegroundMonitor.IsRemoteInputBlocked);
            await webHost.StartAsync();
            if (!isolatedTestMode)
            {
                screenViewSessionSwitch = (_, eventArgs) =>
                {
                    if (StopsScreenViewForSessionSwitch(eventArgs.Reason)) webHost.StopScreenViewing();
                };
                SystemEvents.SessionSwitch += screenViewSessionSwitch;
            }
#if DEBUG
            if (isolatedTestMode &&
                HasOption(args, "--presentation-demo-data") &&
                webHost.PresentationReportStore is InMemoryPresentationReportStore demoReportStore)
            {
                Features.Presentations.PresentationReportDemoData.AddTo(demoReportStore);
            }
            if (isolatedTestMode && HasOption(args, "--site-screenshot-custom-screens"))
            {
                Features.CustomScreens.CustomScreenDemoData.AddTo(
                    webHost.CustomScreenService);
            }
#endif
#if DEBUG
            if (HasOption(args, "--print-host-client-url"))
            {
                Console.WriteLine($"Voltura Air phone client: Windows host URL ({webHost.ServerUrl})");
            }
#endif

            mainWindow = new MainWindow(
                pairingManager,
                webHost,
                clientUrl,
                usePublicScreenshotPairingUrl,
                workstationLockPolicy,
                awakeService,
                activitySimulationService: activitySimulationService,
                cursorOverrides: cursorOverrides,
                appLog: appLog,
                phoneWebcam: phoneWebcam,
                requestRestart: requestRestart);
#if DEBUG
            WritePairingUrlIfRequested(args, mainWindow.PairingUrl);
#endif
            trayContext = new WpfTrayApplicationContext(
                mainWindow,
                webHost,
                pairingManager,
                awakeService,
                requestShutdown,
                activitySimulationService: activitySimulationService);
            return new WpfHostRuntime(
                inputInjector,
                activitySimulationService,
                cursorOverrides,
                cursorOverridesRevoked,
                pointerHighlightForegroundMonitor,
                textDestinationDraftCleanup,
                presentationEmailDraftCleanup,
                phoneWebcamFeature,
                webHost,
                pairingManager,
                mainWindow,
                trayContext,
                appLog,
                screenViewSessionSwitch);
        }
        catch
        {
            if (screenViewSessionSwitch is not null) SystemEvents.SessionSwitch -= screenViewSessionSwitch;
            TryDispose(trayContext, appLog, "tray_context");
            TryCloseWindow(mainWindow, appLog);
            await TryDisposeAsync(activitySimulationService, appLog, "activity_simulation_service");
            if (webHost is not null)
            {
                await TryStopWebHostAsync(webHost, appLog);
                await TryDisposeAsync(webHost, appLog, "web_host");
            }
            else
            {
                TryDispose(powerController as IDisposable, appLog, "power_controller");
                await TryDisposeAsync(awakeService, appLog, "awake_service");
            }

            TryDispose(pointerHighlightForegroundMonitor, appLog, "pointer_foreground_monitor");
            await TryDisposeAsync(textDestinationDraftCleanup, appLog, "text_destination_draft_cleanup");
            await TryDisposeAsync(
                presentationEmailDraftCleanup,
                appLog,
                "presentation_email_draft_cleanup");
            await TryDisposeAsync(phoneWebcamFeature, appLog, "phone_webcam_feature");

            if (cursorOverrides is not null)
            {
                TryDispose(cursorOverrides, appLog, "cursor_overrides");
            }
            else
            {
                TryDispose(customPointerService, appLog, "custom_pointer_service");
                TryDispose(cursorWatchdogService, appLog, "cursor_recovery_service");
            }
            TryDispose(inputInjector, appLog, "input_injector");
            TryDisposePairingManager(pairingManager, appLog);
            await TryDisposeAsync(appLog as IAsyncDisposable, appLog, "application_log");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        var appLog = _appLog;
        if (_screenViewSessionSwitch is not null) SystemEvents.SessionSwitch -= _screenViewSessionSwitch;
        TryDispose(_trayContext, appLog, "tray_context");
        TryCloseWindow(MainWindow, appLog);
        await TryDisposeAsync(_activitySimulationService, appLog, "activity_simulation_service");
        await TryStopWebHostAsync(_webHost, appLog);
        await TryDisposeAsync(_webHost, appLog, "web_host");
        TryDispose(_pointerHighlightForegroundMonitor, appLog, "pointer_foreground_monitor");
        await TryDisposeAsync(_textDestinationDraftCleanup, appLog, "text_destination_draft_cleanup");
        await TryDisposeAsync(
            _presentationEmailDraftCleanup,
            appLog,
            "presentation_email_draft_cleanup");
        await TryDisposeAsync(_phoneWebcamFeature, appLog, "phone_webcam_feature");
        _cursorOverrides.OverridesRevoked -= _cursorOverridesRevoked;
        TryDispose(_cursorOverrides, appLog, "cursor_overrides");
        TryDispose(_inputInjector, appLog, "input_injector");
        TryDisposePairingManager(PairingManager, appLog);
        await TryDisposeAsync(appLog as IAsyncDisposable, appLog, "application_log");
    }

    internal static bool StopsScreenViewForSessionSwitch(SessionSwitchReason reason) => reason is
        SessionSwitchReason.SessionLock or
        SessionSwitchReason.SessionLogoff or
        SessionSwitchReason.ConsoleDisconnect or
        SessionSwitchReason.RemoteDisconnect;

    private static void TryCloseWindow(MainWindow? mainWindow, IAppLog appLog)
    {
        if (mainWindow is null)
        {
            return;
        }

        try
        {
            mainWindow.AllowClose();
            mainWindow.Close();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogCleanupFailure(appLog, "main_window", ex);
        }
    }

    private static void TryDispose(IDisposable? resource, IAppLog appLog, string resourceName)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            resource.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogCleanupFailure(appLog, resourceName, ex);
        }
    }

    private static void TryDisposePairingManager(PairingManager? pairingManager, IAppLog appLog)
    {
        try
        {
            pairingManager?.DisposeHostIdentity();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogCleanupFailure(appLog, "pairing_manager", ex);
        }
    }

    private static async ValueTask TryDisposeAsync(IAsyncDisposable? resource, IAppLog appLog, string resourceName)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            await resource.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogCleanupFailure(appLog, resourceName, ex);
        }
    }

    private static async Task TryStopWebHostAsync(WebHostService webHost, IAppLog appLog)
    {
        try
        {
            await webHost.StopAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogCleanupFailure(appLog, "web_host_stop", ex);
        }
    }

    private static void LogCleanupFailure(IAppLog appLog, string resourceName, Exception exception)
    {
        try
        {
            appLog.Write(new AppLogEntry(
                Event: "host_lifecycle",
                Source: "windows_host",
                Action: $"dispose_{resourceName}",
                Outcome: "failed",
                Detail: exception.Message));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Cleanup must continue even when an injected logger also fails.
        }
    }

#if DEBUG
    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index += 1)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index + 1 < args.Length ? args[index + 1] : null;
        }

        return null;
    }
#endif

    private static bool HasOption(string[] args, string name)
    {
        return args.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

#if DEBUG
    private static string ResolveIsolatedAutomationPath(
        string[] args,
        string requestedPath,
        string leafName)
    {
        if (!HasOption(args, "--isolated-test-mode"))
        {
            throw new InvalidOperationException("Isolated automation paths require --isolated-test-mode.");
        }

        var automationDirectoryName = HasOption(args, "--site-screenshot-mode")
            ? "voltura-air-site-screenshots"
            : "voltura-air-dev-ui";
        var expectedPath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            automationDirectoryName,
            leafName));
        if (!string.Equals(
            Path.GetFullPath(requestedPath),
            expectedPath,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Isolated automation files must use the Voltura Air temporary workspace.");
        }

        return expectedPath;
    }

    private static void WritePairingUrlIfRequested(string[] args, string pairingUrl)
    {
        var requestedPairingUrlFile = GetOption(args, "--pairing-url-file");
        if (string.IsNullOrWhiteSpace(requestedPairingUrlFile))
        {
            return;
        }

        var fullPath = ResolveIsolatedAutomationPath(
            args,
            requestedPairingUrlFile,
            "pairing-url.txt");
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, pairingUrl);
    }
#endif
}
