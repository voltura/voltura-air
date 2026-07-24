using System.Windows;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal static class Program
{
    private const int DevelopmentRestartExitCode = 23;
    private static WpfHostRuntime? s_runtime;
    private static IDisposable? s_isolatedSettingsScope;
    private static int s_activationRequested;
    private static int s_activationDispatchPending;
    private static int s_restartRequested;

    [STAThread]
    private static void Main(string[] args)
    {
        SingleInstanceCoordinator? singleInstance = null;
        try
        {
            singleInstance = SingleInstanceCoordinator.TryAcquire(RequestMainWindow);
            if (singleInstance is null)
            {
                return;
            }

            var isolatedTestMode = args.Contains("--isolated-test-mode", StringComparer.OrdinalIgnoreCase);
            s_isolatedSettingsScope = isolatedTestMode ? HostSettingsRegistry.BeginIsolatedScope() : null;

            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);

            var app = new WpfApplication
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            var shutdownCoordinator = new WpfShutdownCoordinator(
                app.Dispatcher,
                DisposeRuntimeAsync,
                app.Shutdown,
                static exception => Console.Error.WriteLine("Voltura Air shutdown cleanup failed: {0}", exception.Message));
            app.Exit += OnApplicationExit;

            var startupWindow = new StartupWindow();
            startupWindow.Show();

            _ = app.Dispatcher.InvokeAsync(() => InitializeAsync(
                startupWindow,
                args,
                shutdownCoordinator.RequestShutdown,
                () => RequestRestart(shutdownCoordinator.RequestShutdown)));
            app.Run();
        }
        finally
        {
            DisposeIsolatedSettingsScope();
            singleInstance?.Dispose();
        }

        if (Interlocked.Exchange(ref s_restartRequested, 0) != 0 && !IsDevelopmentHostSupervisor())
        {
            RestartCurrentProcess();
        }
    }

    private static async Task InitializeAsync(
        StartupWindow startupWindow,
        string[] args,
        Action requestShutdown,
        Action requestRestart)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var isolatedTestMode = args.Contains("--isolated-test-mode", StringComparer.OrdinalIgnoreCase);
#if DEBUG
            if (args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase) && !isolatedTestMode)
            {
                throw new InvalidOperationException("Site screenshot mode requires --isolated-test-mode.");
            }
#endif

#if DEBUG
            ConfigureIsolatedDevelopmentSettings(args, isolatedTestMode);
            ConfigureSiteScreenshotSettings(args);
#endif
            s_runtime = await WpfHostRuntime.StartAsync(args, requestShutdown, requestRestart);
            var remaining = TimeSpan.FromMilliseconds(1500) - (DateTimeOffset.UtcNow - startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }

            startupWindow.Close();
#if DEBUG
            var screenshotPreferencesSection = args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase)
                ? GetOption(args, "--site-screenshot-preferences-section")
                : null;
            if (args.Contains("--presentation-demo-data", StringComparer.OrdinalIgnoreCase))
            {
                s_runtime.MainWindow.ShowPage(HostPage.Presentations);
            }
            else if (!string.IsNullOrWhiteSpace(screenshotPreferencesSection))
            {
                s_runtime.MainWindow.ShowPreferencesSectionForScreenshot(screenshotPreferencesSection);
            }
            else if (ConsumeActivationRequest() || ShouldShowMainWindowOnStartup(args, AppWindowSettings.StartHiddenInTray(), s_runtime.PairingManager.HasActiveController))
#else
            if (ConsumeActivationRequest() || ShouldShowMainWindowOnStartup(args, AppWindowSettings.StartHiddenInTray(), s_runtime.PairingManager.HasActiveController))
#endif
            {
                s_runtime.MainWindow.ShowPage(HostPage.Connect);
            }
        }
        catch (Exception ex)
        {
            startupWindow.ShowError(
                ex is HostPortUnavailableException
                    ? ex.Message
                    : "An unexpected startup error occurred.",
                ex.ToString());
        }
    }

    private static void RequestMainWindow()
    {
        Interlocked.Exchange(ref s_activationRequested, 1);
        QueueActivationDispatch();
    }

    private static void QueueActivationDispatch()
    {
        var runtime = s_runtime;
        if (runtime is not null && Interlocked.CompareExchange(ref s_activationDispatchPending, 1, 0) == 0)
        {
            _ = runtime.MainWindow.Dispatcher.BeginInvoke(ShowRequestedMainWindow);
        }
    }

    private static void ShowRequestedMainWindow()
    {
        try
        {
            if (s_runtime is null || !ConsumeActivationRequest())
            {
                return;
            }

            s_runtime.MainWindow.ShowPage(HostPage.Connect);
        }
        finally
        {
            Interlocked.Exchange(ref s_activationDispatchPending, 0);
            if (Volatile.Read(ref s_activationRequested) != 0)
            {
                QueueActivationDispatch();
            }
        }
    }

    private static bool ConsumeActivationRequest()
    {
        return Interlocked.Exchange(ref s_activationRequested, 0) != 0;
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

#if DEBUG
    private static void ConfigureIsolatedDevelopmentSettings(string[] args, bool isolatedTestMode)
    {
        if (!args.Contains("--enable-alpha-features", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (!isolatedTestMode)
        {
            throw new InvalidOperationException("The alpha-feature development switch requires --isolated-test-mode.");
        }

        AppDeveloperSettings.SetEnableAlphaFeatures(true);
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
        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        AppDeveloperSettings.SetEnableGestureDebug(false);
        AppNotificationSettings.SetShowConnectionStatusNotifications(false);
        AppNotificationSettings.SetShowPairingWindowOnDisconnect(false);
        AppPermissionSettings.Save(HostPermissions.DefaultGlobal with
        {
            AllowRemoteAppLaunch = false,
            AllowUrlOpen = true
        });
    }
#endif

    internal static bool ShouldShowMainWindowOnStartup(string[] args, bool startHiddenInTraySetting, bool hasActiveController)
    {
        return !startHiddenInTraySetting &&
            !args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) &&
            !hasActiveController;
    }

    private static void OnApplicationExit(object sender, ExitEventArgs e)
    {
        if (Volatile.Read(ref s_restartRequested) != 0 && IsDevelopmentHostSupervisor())
        {
            e.ApplicationExitCode = DevelopmentRestartExitCode;
        }

        s_runtime = null;
        DisposeIsolatedSettingsScope();
    }

    private static void RequestRestart(Action requestShutdown)
    {
        if (Interlocked.Exchange(ref s_restartRequested, 1) == 0)
        {
            requestShutdown();
        }
    }

    private static bool IsDevelopmentHostSupervisor() =>
        string.Equals(Environment.GetEnvironmentVariable("VOLTURA_AIR_DEV_HOST"), "1", StringComparison.Ordinal);

    private static void RestartCurrentProcess()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false
            };
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            _ = System.Diagnostics.Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.Error.WriteLine("Voltura Air restart failed: {0}", ex.Message);
        }
    }

    private static async ValueTask DisposeRuntimeAsync()
    {
        var runtime = s_runtime;
        s_runtime = null;
        try
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }
        }
        finally
        {
            DisposeIsolatedSettingsScope();
        }
    }

    private static void DisposeIsolatedSettingsScope()
    {
        Interlocked.Exchange(ref s_isolatedSettingsScope, null)?.Dispose();
    }
}
