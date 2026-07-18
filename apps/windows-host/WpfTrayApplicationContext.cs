using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace VolturaAir.Host;

internal sealed partial class WpfTrayApplicationContext : IDisposable
{
    private const int MaxTrayTooltipLength = 63;
    private const int DisconnectNotificationDelayMs = 1800;
    // Covers the mobile client's 3-second connection deadline, 1.2-second retry delay, and LAN handshake time.
    private const int StartupConnectionGracePeriodMs = 5000;
    private const string ProductSiteUrl = "https://voltura.se/air/";
    private const string DefaultTrayIconFileName = "VolturaAirTray.ico";
    private const string ConnectedTrayIconFileName = "VolturaAirTrayConnected.ico";
    private const string DisconnectedTrayIconFileName = "VolturaAirTrayDisconnected.ico";

    private readonly MainWindow _mainWindow;
    private readonly Container _components = new();
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly IAwakeService _awakeService;
    private readonly Action _requestShutdown;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly Dictionary<TrayConnectionState, Icon> _trayIcons;
    private readonly TrayConnectionIndicator _trayConnectionIndicator;
    // These items are owned by _trayMenu.Items and are disposed with the context menu.
#pragma warning disable CA2213
    private Forms.ToolStripMenuItem _awakeOffItem = null!;
    private Forms.ToolStripMenuItem _awakeTimedItem = null!;
    private Forms.ToolStripMenuItem _awakeExpirationItem = null!;
    private Forms.ToolStripMenuItem _awakeIndefiniteItem = null!;
    private Forms.ToolStripMenuItem _awakeKeepScreenOnItem = null!;
#pragma warning restore CA2213
    private OwnedDispatcherTimer? _pendingDisconnectNotification;
    private OwnedDispatcherTimer? _pendingStartupConnectionGrace;
    private bool _hadActiveController;
    private bool _disposed;

    public WpfTrayApplicationContext(
        MainWindow mainWindow,
        WebHostService webHost,
        PairingManager pairingManager,
        IAwakeService awakeService,
        Action requestShutdown)
    {
        _mainWindow = mainWindow;
        _dispatcher = mainWindow.Dispatcher;
        _webHost = webHost;
        _pairingManager = pairingManager;
        _awakeService = awakeService;
        _requestShutdown = requestShutdown;
        _hadActiveController = pairingManager.HasActiveController;
        _trayConnectionIndicator = new TrayConnectionIndicator(
            pairingManager.IsPaired,
            _hadActiveController,
            holdInitialDisconnectedState: pairingManager.IsPaired && !_hadActiveController);

        BuildMenu();
        _trayIcons = LoadTrayIcons();
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = GetTrayIcon(_trayConnectionIndicator.DisplayedState),
            Text = BuildTrayTooltip(_trayConnectionIndicator.DisplayedState),
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.ShowPage(HostPage.Connect);
        TrayIconVisibilityPromoter.PromoteWhenReady(_components, _trayIcon);
        ApplyMenuTheme();
        if (_webHost.IsInputBlockedByElevation)
        {
            OnRemoteInputBlockedChanged(this, new RemoteInputBlockedChangedEventArgs(true));
        }

        _pairingManager.ConnectionChanged += OnConnectionChanged;
        _webHost.ControllerSocketClosed += OnControllerSocketClosed;
        _webHost.RemoteInputBlockedChanged += OnRemoteInputBlockedChanged;
        AppThemeSettings.Changed += OnAppThemeChanged;
        _awakeService.StateChanged += OnAwakeStateChanged;
        ScheduleStartupConnectionGrace();
    }

    private void BuildMenu()
    {
        var showQrCodeItem = _trayMenu.Items.Add("Show Voltura Air", null, (_, _) => RunTrayCommand(() => _mainWindow.ShowPage(HostPage.Connect)));
        showQrCodeItem.Font = new Font(showQrCodeItem.Font, DrawingFontStyle.Bold);
        _trayMenu.Items.Add("Devices", null, (_, _) => RunTrayCommand(() => _mainWindow.ShowPage(HostPage.Devices)));
        _trayMenu.Items.Add("Preferences", null, (_, _) => RunTrayCommand(() => _mainWindow.ShowPage(HostPage.Preferences)));
        BuildAwakeMenu();
        _trayMenu.Items.Add("Open product page", null, (_, _) => RunTrayCommand(OpenProductSite));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => RunTrayCommand(RequestExit));
    }

    private void ApplyMenuTheme()
    {
        var theme = WindowsTheme.Current();
        _trayMenu.RenderMode = Forms.ToolStripRenderMode.Professional;
        _trayMenu.Renderer = new ThemedToolStripRenderer(theme);
        _trayMenu.BackColor = theme.Surface;
        _trayMenu.ForeColor = theme.Text;
        _trayMenu.ShowImageMargin = false;

        foreach (var item in EnumerateMenuItems(_trayMenu.Items))
        {
            item.BackColor = theme.Surface;
            item.ForeColor = theme.Text;
        }
    }

    private static void OpenProductSite()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ProductSiteUrl,
            UseShellExecute = true
        });
    }

    internal void RequestExit()
    {
        _mainWindow.AllowClose();
        _trayIcon.Visible = false;
        _requestShutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Dispose);
            return;
        }

        _disposed = true;
        CancelStartupConnectionGrace();
        CancelPendingDisconnectNotification();
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        _webHost.ControllerSocketClosed -= OnControllerSocketClosed;
        _webHost.RemoteInputBlockedChanged -= OnRemoteInputBlockedChanged;
        AppThemeSettings.Changed -= OnAppThemeChanged;
        _awakeService.StateChanged -= OnAwakeStateChanged;
        _trayIcon.Visible = false;
        _components.Dispose();
        _trayIcon.Dispose();
        _trayMenu.Dispose();

        foreach (var icon in _trayIcons.Values.Distinct())
        {
            icon.Dispose();
        }
    }
}
