using VolturaAir.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var pairingManager = new PairingManager(new PairingStore());
        using var inputInjector = new SendInputInjector();
        var inputDispatcher = new InputDispatcher(inputInjector);
        var clientUrl = GetOption(args, "--client-url") ?? Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");
        WebHostService? webHost = null;

        try
        {
            webHost = new WebHostService(pairingManager, inputDispatcher);
            webHost.StartAsync().GetAwaiter().GetResult();
            var form = new PairingForm(webHost.ServerUrl, pairingManager, clientUrl);
            WritePairingUrlIfRequested(args, form.PairingUrl);
            using var windowChrome = ThemedWindowChrome.Install(form, form.Icon!);
            using var appContext = new TrayApplicationContext(form, webHost, pairingManager, showMainWindow: !args.Contains("--minimized", StringComparer.OrdinalIgnoreCase));
            Application.Run(appContext);
        }
        catch (HostPortUnavailableException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Voltura Air connection settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (webHost is not null)
            {
                webHost.StopAsync().GetAwaiter().GetResult();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
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
