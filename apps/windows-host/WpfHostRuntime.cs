using System.Diagnostics;
using System.Globalization;

namespace VolturaAir.Host;

internal sealed class WpfHostRuntime : IAsyncDisposable
{
    private readonly SendInputInjector _inputInjector;
    private readonly Process? _cursorWatchdog;
    private readonly CustomPointerService _customPointerService;
    private readonly PointerHighlightForegroundMonitor _pointerHighlightForegroundMonitor;
    private readonly IDisposable _textDestinationDraftCleanup;
    private readonly WebHostService _webHost;
    private readonly WpfTrayApplicationContext _trayContext;

    private WpfHostRuntime(
        SendInputInjector inputInjector,
        Process? cursorWatchdog,
        CustomPointerService customPointerService,
        PointerHighlightForegroundMonitor pointerHighlightForegroundMonitor,
        IDisposable textDestinationDraftCleanup,
        WebHostService webHost,
        PairingManager pairingManager,
        MainWindow mainWindow,
        WpfTrayApplicationContext trayContext)
    {
        _inputInjector = inputInjector;
        _cursorWatchdog = cursorWatchdog;
        _customPointerService = customPointerService;
        _pointerHighlightForegroundMonitor = pointerHighlightForegroundMonitor;
        _textDestinationDraftCleanup = textDestinationDraftCleanup;
        _webHost = webHost;
        PairingManager = pairingManager;
        MainWindow = mainWindow;
        _trayContext = trayContext;
    }

    public PairingManager PairingManager { get; }

    public MainWindow MainWindow { get; }

    public static async Task<WpfHostRuntime> StartAsync(string[] args)
    {
        var pairingStoreRoot = GetOption(args, "--pairing-store-root");
        var pairingManager = new PairingManager(new PairingStore(string.IsNullOrWhiteSpace(pairingStoreRoot) ? null : pairingStoreRoot));
        var inputInjector = new SendInputInjector();
        var clientUrl = GetOption(args, "--client-url") ?? Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        var usePublicScreenshotPairingUrl = HasOption(args, "--site-screenshot-mode");
        var isolatedTestMode = HasOption(args, "--isolated-test-mode");
        IAppLog appLog = isolatedTestMode ? NullAppLog.Instance : new AppLog();
        var cursorWatchdog = TryStartCursorWatchdog();
        var customPointerService = new CustomPointerService();
        customPointerService.Apply(AppPointerSettings.GetCustomPointer());
        MonitorCursorWatchdog(cursorWatchdog, customPointerService);
        var textDestinationDraftCleanup = TextDestinationDraftStore.CreateCleanupService();
        var inputDispatcher = new InputDispatcher(inputInjector);
        var workstationLockPolicy = new WorkstationLockPolicy(appLog);
        ISystemPowerController powerController = isolatedTestMode
            ? new NoOpSystemPowerController()
            : new SystemPowerController(new WindowsDisplayActionController(System.Windows.Application.Current.Dispatcher, appLog));
        IAwakeService awakeService = isolatedTestMode
            ? new NoOpAwakeService()
            : AwakeService.CreateWindows(appLog);
        var webHost = new WebHostService(
            pairingManager,
            inputDispatcher,
            powerController: powerController,
            awakeService: awakeService,
            workstationLockPolicy: workstationLockPolicy,
            appLog: appLog,
            textDestinationService: new TextDestinationService(inputDispatcher, inputInjector),
            applyCustomPointer: customPointerService.Apply,
            isolatedTestMode: isolatedTestMode);

        PointerHighlightForegroundMonitor? pointerHighlightForegroundMonitor = null;
        try
        {
            pointerHighlightForegroundMonitor = new PointerHighlightForegroundMonitor(appLog);
            pointerHighlightForegroundMonitor.RemoteInputBlockedChanged += (_, eventArgs) =>
                webHost.SetInputBlockedByElevation(eventArgs.IsBlocked);
            inputDispatcher.TaskbarActivated += (_, _) => pointerHighlightForegroundMonitor.NotifyTaskbarActivation();
            webHost.SetInputBlockedByElevation(pointerHighlightForegroundMonitor.IsRemoteInputBlocked);
            await webHost.StartAsync();
            var mainWindow = new MainWindow(
                pairingManager,
                webHost,
                clientUrl,
                usePublicScreenshotPairingUrl,
                workstationLockPolicy,
                awakeService,
                customPointerService: customPointerService,
                appLog: appLog);
            WritePairingUrlIfRequested(args, mainWindow.PairingUrl);
            var trayContext = new WpfTrayApplicationContext(mainWindow, webHost, pairingManager, awakeService);
            return new WpfHostRuntime(
                inputInjector,
                cursorWatchdog,
                customPointerService,
                pointerHighlightForegroundMonitor,
                textDestinationDraftCleanup,
                webHost,
                pairingManager,
                mainWindow,
                trayContext);
        }
        catch
        {
            pointerHighlightForegroundMonitor?.Dispose();
            textDestinationDraftCleanup.Dispose();
            await webHost.DisposeAsync();
            customPointerService.Dispose();
            cursorWatchdog?.Dispose();
            inputInjector.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _trayContext.Dispose();
        MainWindow.AllowClose();
        MainWindow.Close();
        await _webHost.StopAsync();
        await _webHost.DisposeAsync();
        _inputInjector.Dispose();
        _pointerHighlightForegroundMonitor.Dispose();
        _textDestinationDraftCleanup.Dispose();
        _customPointerService.Dispose();
        _cursorWatchdog?.Dispose();
    }

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

    private static bool HasOption(string[] args, string name)
    {
        return args.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static Process? TryStartCursorWatchdog()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "VolturaAir.CursorWatchdog.exe"),
                CreateNoWindow = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static void MonitorCursorWatchdog(
        Process? cursorWatchdog,
        CustomPointerService customPointerService)
    {
        if (cursorWatchdog is null)
        {
            return;
        }

        try
        {
            cursorWatchdog.Exited += (_, _) => customPointerService.Restore();
            cursorWatchdog.EnableRaisingEvents = true;
            if (cursorWatchdog.HasExited)
            {
                customPointerService.Restore();
            }
        }
        catch
        {
            customPointerService.Restore();
        }
    }

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
}
