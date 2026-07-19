using System.ComponentModel;
using System.Drawing;
using System.Windows.Threading;
using DrawingFontStyle = System.Drawing.FontStyle;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class WpfTrayApplicationContext : IDisposable
{
    private const int MaxTrayTooltipLength = 63;
    private const string DefaultTrayIconFileName = "VolturaAirTray.ico";
    private const string ConnectedTrayIconFileName = "VolturaAirTrayConnected.ico";
    private const string DisconnectedTrayIconFileName = "VolturaAirTrayDisconnected.ico";

    private readonly MainWindow _mainWindow;
    private readonly Container _components = new();
    private readonly Dispatcher _dispatcher;
    private readonly PairingManager _pairingManager;
    private readonly Action _requestShutdown;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly Dictionary<TrayConnectionState, Icon> _trayIcons;
    private readonly TrayAwakeMenuController _awakeMenuController;
    private readonly TrayConnectionFeedbackController _connectionFeedbackController;
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
        _pairingManager = pairingManager;
        _requestShutdown = requestShutdown;
        _awakeMenuController = new TrayAwakeMenuController(
            _dispatcher,
            awakeService,
            _mainWindow.ShowAwakePreferences,
            ReportAwakeFailure);
        BuildMenu();

        _trayIcons = LoadTrayIcons();
        _connectionFeedbackController = new TrayConnectionFeedbackController(
            _dispatcher,
            pairingManager,
            webHost,
            ApplyTrayConnectionState,
            ShowNotification,
            () => _mainWindow.ShowPage(HostPage.Connect));
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = GetTrayIcon(_connectionFeedbackController.DisplayedState),
            Text = BuildTrayTooltip(_connectionFeedbackController.DisplayedState),
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.ShowPage(HostPage.Connect);
        TrayIconVisibilityPromoter.PromoteWhenReady(_components, _trayIcon);

        ApplyMenuTheme();
        AppThemeSettings.Changed += OnAppThemeChanged;
        _connectionFeedbackController.Start();
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
        AppThemeSettings.Changed -= OnAppThemeChanged;
        _connectionFeedbackController.Dispose();
        _awakeMenuController.Dispose();
        _trayIcon.Visible = false;
        _components.Dispose();
        _trayIcon.Dispose();
        _trayMenu.Dispose();

        foreach (var icon in _trayIcons.Values.Distinct())
        {
            icon.Dispose();
        }
    }

    private void BuildMenu()
    {
        var showItem = _trayMenu.Items.Add(
            "Show Voltura Air",
            null,
            (_, _) => RunProtected(() => _mainWindow.ShowPage(HostPage.Connect)));
        showItem.Font = new Font(showItem.Font, DrawingFontStyle.Bold);
        _trayMenu.Items.Add("Devices", null, (_, _) => RunProtected(() => _mainWindow.ShowPage(HostPage.Devices)));
        _trayMenu.Items.Add("Preferences", null, (_, _) => RunProtected(() => _mainWindow.ShowPage(HostPage.Preferences)));
        _trayMenu.Items.Add(_awakeMenuController.MenuItem);
        _trayMenu.Items.Add("Open product page", null, (_, _) => RunProtected(ProductWebsite.Open));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => RunProtected(RequestExit));
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ApplyMenuTheme();
            }
        });
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

    private void ApplyTrayConnectionState(TrayConnectionState state)
    {
        if (_disposed)
        {
            return;
        }

        _trayIcon.Text = BuildTrayTooltip(state);
        _trayIcon.Icon = GetTrayIcon(state);
    }

    private void ReportAwakeFailure(AwakeOperationResult result)
    {
        ShowNotification(
            "Keep awake",
            result.Error ?? "Windows rejected the request.",
            Forms.ToolTipIcon.Warning);
    }

    private void ShowNotification(string title, string message, Forms.ToolTipIcon icon) =>
        _trayIcon.ShowBalloonTip(3000, title, message, icon);

    private Icon GetTrayIcon(TrayConnectionState state) => _trayIcons.TryGetValue(state, out var icon)
        ? icon
        : _trayIcons[TrayConnectionState.NoDevicesRegistered];

    private string BuildTrayTooltip(TrayConnectionState state)
    {
        var status = state switch
        {
            TrayConnectionState.Starting => "waiting for paired devices to reconnect",
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

    private static string TruncateTrayTooltip(string value) => value.Length <= MaxTrayTooltipLength
        ? value
        : $"{value[..(MaxTrayTooltipLength - 3)]}...";

    private static Dictionary<TrayConnectionState, Icon> LoadTrayIcons()
    {
        var normal = LoadTrayIcon(DefaultTrayIconFileName);
        return new Dictionary<TrayConnectionState, Icon>
        {
            [TrayConnectionState.Starting] = (Icon)normal.Clone(),
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

    private static string GetAssetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

    private static IEnumerable<Forms.ToolStripItem> EnumerateMenuItems(Forms.ToolStripItemCollection items)
    {
        foreach (Forms.ToolStripItem item in items)
        {
            yield return item;
            if (item is Forms.ToolStripDropDownItem dropDown)
            {
                foreach (var child in EnumerateMenuItems(dropDown.DropDownItems))
                {
                    yield return child;
                }
            }
        }
    }

    private static void RunProtected(Action action)
    {
        if (!HostUiInputGuard.IsRecentProtectedClientInput())
        {
            action();
        }
    }
}
