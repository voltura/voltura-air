using System.Windows;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal static class Program
{
    private static WpfHostRuntime? s_runtime;

    [STAThread]
    private static void Main(string[] args)
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);

        var app = new WpfApplication
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        app.Exit += OnApplicationExit;

        var startupWindow = new StartupWindow();
        startupWindow.Show();

        _ = app.Dispatcher.InvokeAsync(() => InitializeAsync(startupWindow, args));
        app.Run();
    }

    private static async Task InitializeAsync(StartupWindow startupWindow, string[] args)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            s_runtime = await WpfHostRuntime.StartAsync(args);
            var remaining = TimeSpan.FromMilliseconds(1500) - (DateTimeOffset.UtcNow - startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }

            startupWindow.Close();
            if (ShouldShowMainWindowOnStartup(args, AppWindowSettings.StartHiddenInTray(), s_runtime.PairingManager.HasActiveController))
            {
                s_runtime.MainWindow.ShowPage(HostPage.Connect);
            }
        }
        catch (Exception ex)
        {
            startupWindow.ShowError(
                ex is HostPortUnavailableException ? ex.Message : "An unexpected startup error occurred.",
                ex.ToString());
        }
    }

    internal static bool ShouldShowMainWindowOnStartup(string[] args, bool startHiddenInTraySetting, bool hasActiveController)
    {
        return !startHiddenInTraySetting &&
            !args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) &&
            !hasActiveController;
    }

    private static void OnApplicationExit(object sender, ExitEventArgs e)
    {
        if (s_runtime is null)
        {
            return;
        }

        s_runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        s_runtime = null;
    }
}
