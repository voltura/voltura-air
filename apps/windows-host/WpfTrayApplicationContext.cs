using System.Windows;
using Forms = System.Windows.Forms;
using DrawingFontStyle = System.Drawing.FontStyle;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal sealed class WpfTrayApplicationContext : IDisposable
{
    private const int MaxTrayTooltipLength = 63;
    private const string ProductSiteUrl = "https://voltura.se/air/";

    private readonly MainWindow _mainWindow;
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly Icon _trayIconImage;
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
        _trayIconImage = LoadTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = _trayIconImage,
            Text = BuildTrayTooltip(),
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.ShowPage(HostPage.Connect);
        ApplyMenuTheme();
    }

    private void BuildMenu()
    {
        var showQrCodeItem = _trayMenu.Items.Add("Show Voltura Air", null, (_, _) => _mainWindow.ShowPage(HostPage.Connect));
        showQrCodeItem.Font = new Font(showQrCodeItem.Font, DrawingFontStyle.Bold);
        _trayMenu.Items.Add("Devices", null, (_, _) => _mainWindow.ShowPage(HostPage.Devices));
        _trayMenu.Items.Add("Preferences", null, (_, _) => _mainWindow.ShowPage(HostPage.Preferences));
        _trayMenu.Items.Add("Open product page", null, (_, _) => OpenProductSite());
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
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
        WpfApplication.Current.Dispatcher.BeginInvoke(() => _trayIcon.Text = BuildTrayTooltip());

        if (!_hadActiveController && hasActiveController)
        {
            WpfApplication.Current.Dispatcher.BeginInvoke(() =>
            {
                ShowConnectionStatusNotification(
                    "Voltura Air paired",
                    $"{_pairingManager.ActiveDeviceSummary} connected.",
                    Forms.ToolTipIcon.Info);
            });
        }
        else if (_hadActiveController && !hasActiveController)
        {
            WpfApplication.Current.Dispatcher.BeginInvoke(() =>
            {
                if (AppNotificationSettings.ShowPairingWindowOnDisconnect())
                {
                    _mainWindow.ShowPage(HostPage.Connect);
                }

                ShowConnectionStatusNotification(
                    "Voltura Air disconnected",
                    "No connected devices.",
                    Forms.ToolTipIcon.Info);
            });
        }

        _hadActiveController = hasActiveController;
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

    private string BuildTrayTooltip()
    {
        var status = _pairingManager.HasActiveController
            ? $"Connected: {_pairingManager.ActiveDeviceSummary}"
            : "No connected devices";

        return TruncateTrayTooltip($"Voltura Air - {status}");
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

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAirTray.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        AppThemeSettings.Changed -= OnAppThemeChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _trayIconImage.Dispose();
    }
}
