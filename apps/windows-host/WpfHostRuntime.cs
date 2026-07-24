namespace VolturaAir.Host;

internal sealed class WpfHostRuntime : IAsyncDisposable
{
    private readonly SendInputInjector _inputInjector;
    private readonly CursorWatchdogService _cursorWatchdogService;
    private readonly EventHandler _cursorWatchdogMonitoringLost;
    private readonly CustomPointerService _customPointerService;
    private readonly PointerHighlightForegroundMonitor _pointerHighlightForegroundMonitor;
    private readonly IAsyncDisposable _textDestinationDraftCleanup;
    private readonly IAsyncDisposable _presentationEmailDraftCleanup;
    private readonly WebHostService _webHost;
    private readonly WpfTrayApplicationContext _trayContext;
    private readonly IAppLog _appLog;
    private int _disposeState;

    private WpfHostRuntime(
        SendInputInjector inputInjector,
        CursorWatchdogService cursorWatchdogService,
        EventHandler cursorWatchdogMonitoringLost,
        CustomPointerService customPointerService,
        PointerHighlightForegroundMonitor pointerHighlightForegroundMonitor,
        IAsyncDisposable textDestinationDraftCleanup,
        IAsyncDisposable presentationEmailDraftCleanup,
        WebHostService webHost,
        PairingManager pairingManager,
        MainWindow mainWindow,
        WpfTrayApplicationContext trayContext,
        IAppLog appLog)
    {
        _inputInjector = inputInjector;
        _cursorWatchdogService = cursorWatchdogService;
        _cursorWatchdogMonitoringLost = cursorWatchdogMonitoringLost;
        _customPointerService = customPointerService;
        _pointerHighlightForegroundMonitor = pointerHighlightForegroundMonitor;
        _textDestinationDraftCleanup = textDestinationDraftCleanup;
        _presentationEmailDraftCleanup = presentationEmailDraftCleanup;
        _webHost = webHost;
        PairingManager = pairingManager;
        MainWindow = mainWindow;
        _trayContext = trayContext;
        _appLog = appLog;
    }

    public PairingManager PairingManager { get; }

    public MainWindow MainWindow { get; }

    public static async Task<WpfHostRuntime> StartAsync(string[] args, Action requestShutdown, Action requestRestart)
    {
#if DEBUG
        var pairingStoreRoot = GetOption(args, "--pairing-store-root");
        var clientUrl = GetOption(args, "--client-url") ?? Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        var usePublicScreenshotPairingUrl = HasOption(args, "--site-screenshot-mode");
#else
        string? pairingStoreRoot = null;
        string? clientUrl = null;
        const bool usePublicScreenshotPairingUrl = false;
#endif
        var isolatedTestMode = HasOption(args, "--isolated-test-mode");
        IAppLog appLog = isolatedTestMode ? NullAppLog.Instance : new AppLog();
        SendInputInjector? inputInjector = null;
        CursorWatchdogService? cursorWatchdogService = null;
        CustomPointerService? customPointerService = null;
        IAsyncDisposable? textDestinationDraftCleanup = null;
        IAsyncDisposable? presentationEmailDraftCleanup = null;
        ISystemPowerController? powerController = null;
        IAwakeService? awakeService = null;
        WebHostService? webHost = null;
        PointerHighlightForegroundMonitor? pointerHighlightForegroundMonitor = null;
        MainWindow? mainWindow = null;
        WpfTrayApplicationContext? trayContext = null;
        try
        {
            var pairingManager = new PairingManager(new PairingStore(string.IsNullOrWhiteSpace(pairingStoreRoot) ? null : pairingStoreRoot));
            inputInjector = new SendInputInjector();
            cursorWatchdogService = new CursorWatchdogService();
            customPointerService = new CustomPointerService(
                AppPointerSettings.UseCursorRecoveryWatchdog,
                cursorWatchdogService.EnsureMonitoring,
                cursorWatchdogService.StopMonitoring);
            // A prior forced exit may have left the Windows cursor scheme behind.
            // Normalize it before this host decides whether to apply Custom pointer again.
            CustomPointerService.RestoreWindowsCursorScheme();
            var customPointerSettings = AppPointerSettings.GetCustomPointer();
            if (customPointerSettings.Enabled && !AppPointerSettings.UseCursorRecoveryWatchdog())
            {
                customPointerSettings = customPointerSettings with { Enabled = false };
                AppPointerSettings.SetCustomPointer(customPointerSettings);
            }
            customPointerService.ApplyPresentationLaserPointerSettings(
                AppPointerSettings.GetPresentationLaserPointer());
            customPointerService.Apply(customPointerSettings);
            EventHandler cursorWatchdogMonitoringLost = (_, _) =>
            {
                var disabledPointer = AppPointerSettings.GetCustomPointer() with { Enabled = false };
                customPointerService.Apply(disabledPointer);
                AppPointerSettings.SetCustomPointer(disabledPointer);
                appLog.Write(new AppLogEntry(
                    Event: "host_action",
                    Source: "windows_host",
                    Action: "cursor_recovery_watchdog",
                    Outcome: "lost"));
            };
            cursorWatchdogService.MonitoringLost += cursorWatchdogMonitoringLost;
            textDestinationDraftCleanup = TextDestinationDraftStore.CreateCleanupService(appLog);
            presentationEmailDraftCleanup =
                new Features.Presentations.PresentationEmailDraftCleanup(appLog);
            var inputDispatcher = new InputDispatcher(inputInjector);
            var workstationLockPolicy = new WorkstationLockPolicy(appLog);
            powerController = isolatedTestMode
                ? new NoOpSystemPowerController()
                : new SystemPowerController(new WindowsDisplayActionController(System.Windows.Application.Current.Dispatcher, appLog));
            awakeService = isolatedTestMode
                ? new NoOpAwakeService()
                : await AwakeService.CreateWindowsAsync(appLog);
            webHost = new WebHostService(
                pairingManager,
                inputDispatcher,
                powerController: powerController,
                awakeService: awakeService,
                workstationLockPolicy: workstationLockPolicy,
                appLog: appLog,
                textDestinationService: new TextDestinationService(inputDispatcher, inputInjector),
                applyCustomPointer: customPointerService.Apply,
                applyPresentationLaserPointer: customPointerService.SetPresentationLaserPointer,
                isolatedTestMode: isolatedTestMode);

            pointerHighlightForegroundMonitor = new PointerHighlightForegroundMonitor(appLog);
            pointerHighlightForegroundMonitor.RemoteInputBlockedChanged += (_, eventArgs) =>
                webHost.SetInputBlockedByElevation(eventArgs.IsBlocked);
            inputDispatcher.TaskbarActivated += (_, _) => pointerHighlightForegroundMonitor.NotifyTaskbarActivation();
            webHost.SetInputBlockedByElevation(pointerHighlightForegroundMonitor.IsRemoteInputBlocked);
            await webHost.StartAsync();
#if DEBUG
            if (isolatedTestMode &&
                HasOption(args, "--presentation-demo-data") &&
                webHost.PresentationReportStore is InMemoryPresentationReportStore demoReportStore)
            {
                Features.Presentations.PresentationReportDemoData.AddTo(demoReportStore);
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
                customPointerService: customPointerService,
                appLog: appLog,
                requestRestart: requestRestart);
#if DEBUG
            WritePairingUrlIfRequested(args, mainWindow.PairingUrl);
#endif
            trayContext = new WpfTrayApplicationContext(mainWindow, webHost, pairingManager, awakeService, requestShutdown);
            return new WpfHostRuntime(
                inputInjector,
                cursorWatchdogService,
                cursorWatchdogMonitoringLost,
                customPointerService,
                pointerHighlightForegroundMonitor,
                textDestinationDraftCleanup,
                presentationEmailDraftCleanup,
                webHost,
                pairingManager,
                mainWindow,
                trayContext,
                appLog);
        }
        catch
        {
            TryDispose(trayContext, appLog, "tray_context");
            TryCloseWindow(mainWindow, appLog);
            TryDispose(pointerHighlightForegroundMonitor, appLog, "pointer_foreground_monitor");
            await TryDisposeAsync(textDestinationDraftCleanup, appLog, "text_destination_draft_cleanup");
            await TryDisposeAsync(
                presentationEmailDraftCleanup,
                appLog,
                "presentation_email_draft_cleanup");
            if (webHost is not null)
            {
                await TryDisposeAsync(webHost, appLog, "web_host");
            }
            else
            {
                TryDispose(powerController as IDisposable, appLog, "power_controller");
                await TryDisposeAsync(awakeService, appLog, "awake_service");
            }

            TryDispose(customPointerService, appLog, "custom_pointer_service");
            TryDispose(cursorWatchdogService, appLog, "cursor_watchdog_service");
            TryDispose(inputInjector, appLog, "input_injector");
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
        TryDispose(_trayContext, appLog, "tray_context");
        TryCloseWindow(MainWindow, appLog);
        await TryStopWebHostAsync(_webHost, appLog);
        await TryDisposeAsync(_webHost, appLog, "web_host");
        TryDispose(_pointerHighlightForegroundMonitor, appLog, "pointer_foreground_monitor");
        await TryDisposeAsync(_textDestinationDraftCleanup, appLog, "text_destination_draft_cleanup");
        await TryDisposeAsync(
            _presentationEmailDraftCleanup,
            appLog,
            "presentation_email_draft_cleanup");
        TryDispose(_customPointerService, appLog, "custom_pointer_service");
        _cursorWatchdogService.MonitoringLost -= _cursorWatchdogMonitoringLost;
        TryDispose(_cursorWatchdogService, appLog, "cursor_watchdog_service");
        TryDispose(_inputInjector, appLog, "input_injector");
        await TryDisposeAsync(appLog as IAsyncDisposable, appLog, "application_log");
    }

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
    private static void WritePairingUrlIfRequested(string[] args, string pairingUrl)
    {
        var pairingUrlFile = GetOption(args, "--pairing-url-file");
        if (string.IsNullOrWhiteSpace(pairingUrlFile))
        {
            return;
        }

        var fullPath = Path.GetFullPath(pairingUrlFile);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, pairingUrl);
    }
#endif
}
