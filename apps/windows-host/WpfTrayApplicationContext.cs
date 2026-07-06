using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using DrawingFontStyle = System.Drawing.FontStyle;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal sealed class WpfTrayApplicationContext : IDisposable
{
    private const int MaxTrayTooltipLength = 63;
    private const int DisconnectNotificationDelayMs = 1800;
    private const string ProductSiteUrl = "https://voltura.se/air/";
    private const string DefaultTrayIconFileName = "VolturaAirTray.ico";
    private const string ConnectedTrayIconFileName = "VolturaAirTrayConnected.ico";
    private const string DisconnectedTrayIconFileName = "VolturaAirTrayDisconnected.ico";

    private readonly MainWindow _mainWindow;
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly IReadOnlyDictionary<TrayConnectionState, Icon> _trayIcons;
    private CancellationTokenSource? _pendingDisconnectNotification;
    private bool _hadActiveController;
    private bool _disposed;

    public WpfTrayApplicationContext(MainWindow mainWindow, WebHostService webHost, PairingManager pairingManager)
    {
        _mainWindow = mainWindow;
        _webHost = webHost;
        _pairingManager = pairingManager;
        _hadActiveController = pairingManager.HasActiveController;
        _pairingManager.ConnectionChanged += OnConnectionChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;

        BuildMenu();
        _trayIcons = LoadTrayIcons();
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = GetTrayIcon(GetTrayConnectionState()),
            Text = BuildTrayTooltip(),
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.ShowPage(HostPage.Connect);
        ApplyMenuTheme();
    }

    private enum TrayConnectionState
    {
        NoDevicesRegistered,
        Disconnected,
        Connected
    }

    private void BuildMenu()
    {
        var showQrCodeItem = _trayMenu.Items.Add("Show Voltura Air", null, (_, _) => RunTrayCommand(() => _mainWindow.ShowPage(HostPage.Connect)));
        showQrCodeItem.Font = new Font(showQrCodeItem.Font, DrawingFontStyle.Bold);
        _trayMenu.Items.Add("Devices", null, (_, _) => RunTrayCommand(() => _mainWindow.ShowPage(HostPage.Devices)));
        _trayMenu.Items.Add("Preferences", null, (_, _) => RunTrayCommand(() => _mainWindow.ShowPage(HostPage.Preferences)));
        _trayMenu.Items.Add("Open product page", null, (_, _) => RunTrayCommand(OpenProductSite));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => RunTrayCommand(ExitApplication));
    }

    private static void RunTrayCommand(Action action)
    {
        if (HostUiInputGuard.IsRecentProtectedClientInput())
        {
            return;
        }

        action();
    }

    private void ApplyMenuTheme()
    {
        var theme = WindowsTheme.Current();
        _trayMenu.RenderMode = Forms.ToolStripRenderMode.Professional;
        _trayMenu.Renderer = new ThemedToolStripRenderer(theme);
        _trayMenu.BackColor = theme.Surface;
        _trayMenu.ForeColor = theme.Text;
        _trayMenu.ShowImageMargin = false;

        foreach (Forms.ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = theme.Surface;
            item.ForeColor = theme.Text;
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        var hasActiveController = _pairingManager.HasActiveController;
        WpfApplication.Current.Dispatcher.BeginInvoke(ApplyTrayConnectionState);

        if (!_hadActiveController && hasActiveController)
        {
            var cancelledTransientDisconnect = CancelPendingDisconnectNotification();
            if (!cancelledTransientDisconnect)
            {
                WpfApplication.Current.Dispatcher.BeginInvoke(() =>
                {
                    ShowConnectionStatusNotification(
                        "Voltura Air paired",
                        $"{_pairingManager.ActiveDeviceSummary} connected.",
                        Forms.ToolTipIcon.Info);
                });
            }
        }
        else if (_hadActiveController && !hasActiveController)
        {
            ScheduleDisconnectNotification();
        }

        _hadActiveController = hasActiveController;
    }

    private void ScheduleDisconnectNotification()
    {
        CancelPendingDisconnectNotification();

        var pending = new CancellationTokenSource();
        _pendingDisconnectNotification = pending;
        var token = pending.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DisconnectNotificationDelayMs, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            _ = WpfApplication.Current.Dispatcher.BeginInvoke(() => ShowPendingDisconnectNotification(pending));
        });
    }

    private bool CancelPendingDisconnectNotification()
    {
        var pending = _pendingDisconnectNotification;
        if (pending is null)
        {
            return false;
        }

        _pendingDisconnectNotification = null;
        pending.Cancel();
        return true;
    }

    private void ShowPendingDisconnectNotification(CancellationTokenSource pending)
    {
        if (_disposed || !ReferenceEquals(_pendingDisconnectNotification, pending))
        {
            return;
        }

        _pendingDisconnectNotification = null;
        if (_pairingManager.HasActiveController)
        {
            return;
        }

        if (AppNotificationSettings.ShowPairingWindowOnDisconnect())
        {
            _mainWindow.ShowPage(HostPage.Connect);
        }

        ShowConnectionStatusNotification(
            "Voltura Air disconnected",
            "No connected devices.",
            Forms.ToolTipIcon.Info);
    }

    private void ShowConnectionStatusNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (AppNotificationSettings.ShowConnectionStatusNotifications())
        {
            _trayIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        WpfApplication.Current.Dispatcher.BeginInvoke(ApplyMenuTheme);
    }

    private void ApplyTrayConnectionState()
    {
        if (_disposed)
        {
            return;
        }

        var state = GetTrayConnectionState();
        _trayIcon.Text = BuildTrayTooltip(state);
        _trayIcon.Icon = GetTrayIcon(state);
    }

    private TrayConnectionState GetTrayConnectionState()
    {
        if (_pairingManager.HasActiveController)
        {
            return TrayConnectionState.Connected;
        }

        return _pairingManager.IsPaired
            ? TrayConnectionState.Disconnected
            : TrayConnectionState.NoDevicesRegistered;
    }

    private Icon GetTrayIcon(TrayConnectionState state)
    {
        return _trayIcons.TryGetValue(state, out var icon)
            ? icon
            : _trayIcons[TrayConnectionState.NoDevicesRegistered];
    }

    private string BuildTrayTooltip()
    {
        return BuildTrayTooltip(GetTrayConnectionState());
    }

    private string BuildTrayTooltip(TrayConnectionState state)
    {
        var status = state switch
        {
            TrayConnectionState.Connected => BuildConnectedTooltipStatus(),
            TrayConnectionState.Disconnected => "no devices connected",
            _ => "no devices paired yet"
        };

        return TruncateTrayTooltip($"Voltura Air - {status}");
    }

    private string BuildConnectedTooltipStatus()
    {
        var activeDeviceCount = _pairingManager.ActiveDeviceNames.Count;
        if (activeDeviceCount <= 0)
        {
            return "connected";
        }

        var deviceLabel = activeDeviceCount == 1 ? "device" : "devices";
        return $"{activeDeviceCount} {deviceLabel} connected: {_pairingManager.ActiveDeviceSummary}";
    }

    private static string TruncateTrayTooltip(string value)
    {
        return value.Length <= MaxTrayTooltipLength ? value : $"{value[..(MaxTrayTooltipLength - 3)]}...";
    }

    private static void OpenProductSite()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ProductSiteUrl,
            UseShellExecute = true
        });
    }

    private void ExitApplication()
    {
        try
        {
            _mainWindow.AllowClose();
            _trayIcon.Visible = false;

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                Environment.Exit(0);
            });

            var application = WpfApplication.Current;
            if (application.Dispatcher.CheckAccess())
            {
                application.Shutdown();
            }
            else
            {
                application.Dispatcher.BeginInvoke(() => application.Shutdown());
            }
        }
        catch (InvalidOperationException)
        {
            Environment.Exit(0);
        }
    }

    private static IReadOnlyDictionary<TrayConnectionState, Icon> LoadTrayIcons()
    {
        var normal = LoadTrayIcon(DefaultTrayIconFileName);
        return new Dictionary<TrayConnectionState, Icon>
        {
            [TrayConnectionState.NoDevicesRegistered] = normal,
            [TrayConnectionState.Disconnected] = LoadTrayIconOrDefault(DisconnectedTrayIconFileName, normal),
            [TrayConnectionState.Connected] = LoadTrayIconOrDefault(ConnectedTrayIconFileName, normal)
        };
    }

    private static Icon LoadTrayIconOrDefault(string fileName, Icon fallback)
    {
        var iconPath = GetAssetPath(fileName);
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)fallback.Clone();
    }

    private static Icon LoadTrayIcon(string fileName)
    {
        var iconPath = GetAssetPath(fileName);
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingDisconnectNotification();
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        AppThemeSettings.Changed -= OnAppThemeChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();

        foreach (var icon in _trayIcons.Values.Distinct())
        {
            icon.Dispose();
        }
    }
}
