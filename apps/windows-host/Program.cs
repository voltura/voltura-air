using System.Windows;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal static class Program
{
    private static WpfHostRuntime? s_runtime;
    private static IDisposable? s_isolatedSettingsScope;
    private static int s_activationRequested;

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = SingleInstanceCoordinator.TryAcquire(RequestMainWindow);
        if (singleInstance is null)
        {
            return;
        }

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
            var isolatedTestMode = args.Contains("--isolated-test-mode", StringComparer.OrdinalIgnoreCase);
            if (args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase) && !isolatedTestMode)
            {
                throw new InvalidOperationException("Site screenshot mode requires --isolated-test-mode.");
            }

            s_isolatedSettingsScope = isolatedTestMode ? HostSettingsRegistry.BeginIsolatedScope() : null;
            ConfigureSiteScreenshotSettings(args);
            s_runtime = await WpfHostRuntime.StartAsync(args);
            var remaining = TimeSpan.FromMilliseconds(1500) - (DateTimeOffset.UtcNow - startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }

            startupWindow.Close();
            var screenshotPreferencesSection = args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase)
                ? GetOption(args, "--site-screenshot-preferences-section")
                : null;
            if (!string.IsNullOrWhiteSpace(screenshotPreferencesSection))
            {
                s_runtime.MainWindow.ShowPreferencesSectionForScreenshot(screenshotPreferencesSection);
            }
            else if (ConsumeActivationRequest() || ShouldShowMainWindowOnStartup(args, AppWindowSettings.StartHiddenInTray(), s_runtime.PairingManager.HasActiveController))
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

    private static void RequestMainWindow()
    {
        Interlocked.Exchange(ref s_activationRequested, 1);
        var runtime = s_runtime;
        if (runtime is not null)
        {
            _ = runtime.MainWindow.Dispatcher.BeginInvoke(ShowRequestedMainWindow);
        }
    }

    private static void ShowRequestedMainWindow()
    {
        if (s_runtime is null || !ConsumeActivationRequest())
        {
            return;
        }

        s_runtime.MainWindow.ShowPage(HostPage.Connect);
    }

    private static bool ConsumeActivationRequest()
    {
        return Interlocked.Exchange(ref s_activationRequested, 0) != 0;
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

    private static void ConfigureSiteScreenshotSettings(string[] args)
    {
        if (!args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var themeArgument = GetOption(args, "--site-screenshot-theme");
        if (!Enum.TryParse<AppThemeMode>(themeArgument, ignoreCase: true, out var theme) || !Enum.IsDefined(theme))
        {
            throw new InvalidOperationException("Site screenshot mode requires --site-screenshot-theme Light, Dark, or System.");
        }

        AppThemeSettings.SetMode(theme);
        AppDeveloperSettings.SetEnableGestureDebug(false);
        AppNotificationSettings.SetShowConnectionStatusNotifications(false);
        AppNotificationSettings.SetShowPairingWindowOnDisconnect(false);
        AppPermissionSettings.Save(HostPermissions.DefaultGlobal with
        {
            AllowRemoteAppLaunch = false,
            AllowUrlOpen = true
        });
    }

    internal static bool ShouldShowMainWindowOnStartup(string[] args, bool startHiddenInTraySetting, bool hasActiveController)
    {
        return !startHiddenInTraySetting &&
            !args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) &&
            !hasActiveController;
    }

    private static void OnApplicationExit(object sender, ExitEventArgs e)
    {
        try
        {
            s_runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            s_runtime = null;
            s_isolatedSettingsScope?.Dispose();
            s_isolatedSettingsScope = null;
        }
    }
}
