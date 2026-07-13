namespace VolturaAir.Host;

internal sealed class WpfHostRuntime : IAsyncDisposable
{
    private readonly SendInputInjector _inputInjector;
    private readonly WebHostService _webHost;
    private readonly WpfTrayApplicationContext _trayContext;

    private WpfHostRuntime(
        SendInputInjector inputInjector,
        WebHostService webHost,
        PairingManager pairingManager,
        MainWindow mainWindow,
        WpfTrayApplicationContext trayContext)
    {
        _inputInjector = inputInjector;
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
        var inputDispatcher = new InputDispatcher(inputInjector);
        var clientUrl = GetOption(args, "--client-url") ?? Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        var usePublicScreenshotPairingUrl = HasOption(args, "--site-screenshot-mode");
        var isolatedTestMode = HasOption(args, "--isolated-test-mode");
        var webHost = new WebHostService(pairingManager, inputDispatcher, isolatedTestMode: isolatedTestMode);

        try
        {
            await webHost.StartAsync();
            var mainWindow = new MainWindow(pairingManager, webHost, clientUrl, usePublicScreenshotPairingUrl);
            WritePairingUrlIfRequested(args, mainWindow.PairingUrl);
            var trayContext = new WpfTrayApplicationContext(mainWindow, webHost, pairingManager);
            return new WpfHostRuntime(inputInjector, webHost, pairingManager, mainWindow, trayContext);
        }
        catch
        {
            await webHost.DisposeAsync();
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
