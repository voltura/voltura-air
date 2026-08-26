using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using VolturaAir.Host.Features.PhoneWebcam;
using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host;

internal static class Program
{
    private const int DevelopmentRestartExitCode = 23;
    private static readonly TimeSpan StartupWindowMinimumDisplayDuration = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PhoneMicrophoneProbeTimeout = TimeSpan.FromSeconds(5);
    private static WpfHostRuntime? s_runtime;
    private static IDisposable? s_isolatedSettingsScope;
    private static int s_activationRequested;
    private static int s_activationDispatchPending;
    private static int s_restartRequested;
    private static Action? s_postShutdownLaunch;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--phone-microphone-status", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = GetPhoneMicrophoneStatusExitCodeAsync(
                static () => Task.Run(() => new PhoneWebcamAudioTarget().Refresh().State),
                PhoneMicrophoneProbeTimeout).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--installer-health-check", StringComparer.OrdinalIgnoreCase))
        {
            RunInstallerHealthCheck(args);
            return;
        }

        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);

        CatalogImportRequestStore.EnqueueIfPresent(args);
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
            if (!isolatedTestMode)
            {
                CatalogProtocolRegistration.TryRegisterCurrentApplication();
            }

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
#if DEBUG
            var offscreenSiteScreenshot = !string.IsNullOrWhiteSpace(
                GetOption(args, "--site-screenshot-output"));
#else
            const bool offscreenSiteScreenshot = false;
#endif
            if (!offscreenSiteScreenshot)
            {
                startupWindow.Show();
            }

            var minimumSplashDisplayTask = CreateStartupMinimumDisplayTask(!offscreenSiteScreenshot);
            _ = app.Dispatcher.InvokeAsync(
                () => InitializeAsync(
                    startupWindow,
                    minimumSplashDisplayTask,
                    args,
                    shutdownCoordinator.RequestShutdown,
                    () => RequestRestart(shutdownCoordinator.RequestShutdown),
                    installer => RequestUpdateInstall(installer, shutdownCoordinator.RequestShutdown)),
                DispatcherPriority.ContextIdle);
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
        Interlocked.Exchange(ref s_postShutdownLaunch, null)?.Invoke();
    }

    internal static async Task<int> GetPhoneMicrophoneStatusExitCodeAsync(
        Func<Task<PhoneWebcamAudioTargetState>> probe,
        TimeSpan timeout)
    {
        try
        {
            return await probe().WaitAsync(timeout).ConfigureAwait(false) switch
            {
                PhoneWebcamAudioTargetState.Ready => 0,
                PhoneWebcamAudioTargetState.InstalledButUnavailable => 10,
                PhoneWebcamAudioTargetState.NotInstalled => 20,
                _ => 30
            };
        }
        catch (Exception)
        {
            return 30;
        }
    }

    internal static Task CreateStartupMinimumDisplayTask(bool splashVisible)
    {
        return splashVisible
            ? Task.Delay(StartupWindowMinimumDisplayDuration)
            : Task.CompletedTask;
    }

    internal static async Task<T> AwaitStartupReadinessAsync<T>(
        Func<Task<T>> initialize,
        Task minimumSplashDisplayTask)
    {
        var result = await initialize();
        await minimumSplashDisplayTask;
        return result;
    }

    private static void RunInstallerHealthCheck(string[] args)
    {
        if (!args.Contains("--isolated-test-mode", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installer health checks require isolated test mode.");
        foreach (var relativePath in new[]
        {
            "installer-payload.sha256",
            "datachannel.dll",
            Path.Combine("PhoneWebcam", "VolturaAir.WebcamSetup.exe"),
            Path.Combine("wwwroot", "index.html")
        })
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, relativePath)))
                throw new FileNotFoundException("The installed payload is incomplete.", relativePath);
        }
        var nativePath = Path.Combine(AppContext.BaseDirectory, "datachannel.dll");
        if (!NativeLibrary.TryLoad(nativePath, out var handle))
            throw new DllNotFoundException("The installed Screen View native dependency could not be loaded.");
        NativeLibrary.Free(handle);
    }

    private static async Task InitializeAsync(
        StartupWindow startupWindow,
        Task minimumSplashDisplayTask,
        string[] args,
        Action requestShutdown,
        Action requestRestart,
        Action<string> requestUpdate)
    {
        try
        {
            var isolatedTestMode = args.Contains("--isolated-test-mode", StringComparer.OrdinalIgnoreCase);
#if DEBUG
            if ((args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase) ||
                 args.Contains("--preview-update-button", StringComparer.OrdinalIgnoreCase)) &&
                !isolatedTestMode)
            {
                throw new InvalidOperationException("Debug preview modes require --isolated-test-mode.");
            }
#endif

#if DEBUG
            ConfigureSiteScreenshotSettings(args);
#endif
            s_runtime = await AwaitStartupReadinessAsync(
                () => WpfHostRuntime.StartAsync(args, requestShutdown, requestRestart, requestUpdate),
                minimumSplashDisplayTask);
#if DEBUG
            var requestedSiteScreenshotOutput = GetOption(args, "--site-screenshot-output");
            if (!string.IsNullOrWhiteSpace(requestedSiteScreenshotOutput))
            {
                var siteScreenshotOutput = ResolveSiteScreenshotOutputPath(requestedSiteScreenshotOutput);
                await s_runtime.MainWindow.RenderSiteScreenshotAsync(args, siteScreenshotOutput);
                return;
            }
#endif
            startupWindow.Close();
            var catalogImportRequest = CatalogImportRequestStore.TryTake();
            if (catalogImportRequest is not null)
            {
                _ = s_runtime.MainWindow.OpenCatalogImportAsync(catalogImportRequest);
                return;
            }
#if DEBUG
            var screenshotPreferencesSection = args.Contains("--site-screenshot-mode", StringComparer.OrdinalIgnoreCase)
                ? GetOption(args, "--site-screenshot-preferences-section")
                : null;
            if (args.Contains("--site-screenshot-custom-screens", StringComparer.OrdinalIgnoreCase))
            {
                s_runtime.MainWindow.ShowCustomScreenEditorForScreenshot();
            }
            else if (args.Contains("--site-screenshot-relay-connection", StringComparer.OrdinalIgnoreCase))
            {
                s_runtime.MainWindow.ShowRelayConnectionForScreenshot();
            }
            else if (args.Contains("--presentation-demo-data", StringComparer.OrdinalIgnoreCase))
            {
                s_runtime.MainWindow.ShowPage(HostPage.Presentations);
            }
            else if (args.Contains("--preview-update-button", StringComparer.OrdinalIgnoreCase))
            {
                s_runtime.MainWindow.ShowUpdateButtonForPreview();
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
#if DEBUG
            if (!string.IsNullOrWhiteSpace(GetOption(args, "--site-screenshot-output")))
            {
                await Console.Error.WriteLineAsync(ex.ToString());
                requestShutdown();
                return;
            }
#endif
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

            var catalogImportRequest = CatalogImportRequestStore.TryTake();
            if (catalogImportRequest is not null)
            {
                _ = s_runtime.MainWindow.OpenCatalogImportAsync(catalogImportRequest);
            }
            else
            {
                s_runtime.MainWindow.ShowPage(HostPage.Connect);
            }
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
    private static string ResolveSiteScreenshotOutputPath(string requestedOutputPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var assetsDirectory = Path.Combine(repositoryRoot, "apps", "public-site", "assets");
        var resolvedOutputPath = Path.GetFileName(requestedOutputPath) switch
        {
            "voltura-air-host.png" => Path.Combine(assetsDirectory, "voltura-air-host.png"),
            "voltura-air-host-dark.png" => Path.Combine(assetsDirectory, "voltura-air-host-dark.png"),
            "voltura-air-host-custom-screens.png" => Path.Combine(assetsDirectory, "voltura-air-host-custom-screens.png"),
            "voltura-air-host-custom-screens-dark.png" => Path.Combine(assetsDirectory, "voltura-air-host-custom-screens-dark.png"),
            _ => throw new InvalidOperationException("Site screenshots may only write the curated host image files.")
        };

        if (!string.Equals(
            Path.GetFullPath(requestedOutputPath),
            Path.GetFullPath(resolvedOutputPath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Site screenshots must be written to apps/public-site/assets.");
        }

        return resolvedOutputPath;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VolturaAir.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Voltura Air repository root for site screenshot output.");
    }
#endif

#if DEBUG
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
        AppWindowSettings.TryMarkCloseToTrayNotificationShown();
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

    private static void RequestUpdateInstall(string installer, Action requestShutdown)
    {
        if (Interlocked.CompareExchange(
            ref s_postShutdownLaunch,
            () =>
            {
                UpdateProcessLauncher.TryLaunchInstaller(
                    () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = installer,
                        Arguments = "/S /AUTOUPDATE",
                        UseShellExecute = false
                    }),
                    () => RestartCurrentProcess("--update-failed"));
            },
            null) is null) requestShutdown();
    }

    private static bool IsDevelopmentHostSupervisor() =>
        string.Equals(Environment.GetEnvironmentVariable("VOLTURA_AIR_DEV_HOST"), "1", StringComparison.Ordinal);

    private static void RestartCurrentProcess(string? updateOutcomeArgument = null)
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
            foreach (var argument in UpdateProcessLauncher.BuildRestartArguments(
                         Environment.GetCommandLineArgs().Skip(1),
                         updateOutcomeArgument))
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
