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
    private readonly WebHostService _webHost;
    private readonly Action _requestShutdown;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly Dictionary<TrayConnectionState, Icon> _trayIcons;
    private readonly TrayAwakeMenuController _awakeMenuController;
    private readonly TrayConnectionFeedbackController _connectionFeedbackController;
    private readonly OwnedDispatcherAction _connectionChangedAction;
    private readonly Action<string, string, Forms.ToolTipIcon>? _notificationSink;
    private bool _hadActiveController;
    private Forms.ToolStripMenuItem? _screenViewingItem;
    private Forms.ToolStripMenuItem? _blockScreenViewingItem;
    private string? _screenViewingClientId;
    private string? _screenViewingDeviceName;
    private bool _disposed;

    public WpfTrayApplicationContext(
        MainWindow mainWindow,
        WebHostService webHost,
        PairingManager pairingManager,
        IAwakeService awakeService,
        Action requestShutdown,
        Action<string, string, Forms.ToolTipIcon>? notificationSink = null,
        IActivitySimulationService? activitySimulationService = null)
    {
        _mainWindow = mainWindow;
        _dispatcher = mainWindow.Dispatcher;
        _pairingManager = pairingManager;
        _webHost = webHost;
        _requestShutdown = requestShutdown;
        _notificationSink = notificationSink;
        _hadActiveController = pairingManager.HasActiveController;
        _connectionChangedAction = new OwnedDispatcherAction(_dispatcher, HandleConnectionChanged);
#pragma warning disable CA2000 // The inert fallback owns no resources; production composition always supplies the runtime-owned service.
        var effectiveActivitySimulationService = activitySimulationService ?? new InertActivitySimulationService();
#pragma warning restore CA2000
        _awakeMenuController = new TrayAwakeMenuController(
            _dispatcher,
            awakeService,
            effectiveActivitySimulationService,
            _mainWindow.ShowAwakePreferences,
            ReportAwakeFailure,
            ReportActivitySimulationFailure);
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
        _mainWindow.HiddenToTray += OnMainWindowHiddenToTray;
        _pairingManager.ConnectionChanged += OnConnectionChanged;
        _webHost.ScreenViewActivityChanged += OnScreenViewActivityChanged;
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
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        _webHost.ScreenViewActivityChanged -= OnScreenViewActivityChanged;
        _mainWindow.HiddenToTray -= OnMainWindowHiddenToTray;
        _connectionChangedAction.Dispose();
        _connectionFeedbackController.Dispose();
        _awakeMenuController.Dispose();
        _trayIcon.Visible = false;
        _components.Dispose();
        _trayIcon.Dispose();
        _blockScreenViewingItem?.Dispose();
        _screenViewingItem?.Dispose();
        _trayMenu.Dispose();

        foreach (var icon in _trayIcons.Values.Distinct())
        {
            icon.Dispose();
        }
    }

    private void BuildMenu()
    {
        _screenViewingItem = new Forms.ToolStripMenuItem("Stop screen viewing")
        {
            Visible = false
        };
        _screenViewingItem.Click += async (_, _) => await RunProtectedAsync(() => StopScreenViewingFromTrayAsync(disallow: false));
        _blockScreenViewingItem = new Forms.ToolStripMenuItem("Disallow device");
        _blockScreenViewingItem.Click += async (_, _) => await RunProtectedAsync(() => StopScreenViewingFromTrayAsync(disallow: true));
        _screenViewingItem.DropDownItems.Add(_blockScreenViewingItem);
        _trayMenu.Items.Add(_screenViewingItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator { Visible = false, Tag = "screen-view-separator" });
        var showItem = _trayMenu.Items.Add(
            "Show Voltura Air",
            null,
            (_, _) => RunProtected(() => _mainWindow.ShowPage(HostPage.Connect)));
        showItem.Font = new Font(showItem.Font, DrawingFontStyle.Bold);
        _trayMenu.Items.Add("Devices", null, (_, _) => RunProtected(() => _mainWindow.ShowPage(HostPage.Devices)));
        _trayMenu.Items.Add("Preferences", null, (_, _) => RunProtected(() => _mainWindow.ShowPage(HostPage.Preferences)));
        _trayMenu.Items.Add(_awakeMenuController.MenuItem);
        _trayMenu.Items.Add("Open product page", null, (_, _) => RunProtected(ProductWebsite.Open));
        _trayMenu.Items.Add("Browse custom screens", null, (_, _) => RunProtected(ProductWebsite.OpenCustomScreenLibrary));
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

    private void ReportActivitySimulationFailure(string error)
    {
        ShowNotification("Simulated activity", error, Forms.ToolTipIcon.Warning);
    }

    private void OnMainWindowHiddenToTray(object? sender, EventArgs e)
    {
        if (AppWindowSettings.TryMarkCloseToTrayNotificationShown())
        {
            ShowNotification(CloseToTrayNotification.Title, CloseToTrayNotification.Message, Forms.ToolTipIcon.Info);
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _connectionChangedAction.Queue();

    private void OnScreenViewActivityChanged(object? sender, ScreenViewActivityChangedEventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() => ApplyScreenViewActivity(e));
    }

    private void ApplyScreenViewActivity(ScreenViewActivityChangedEventArgs activity)
    {
        if (_disposed || _screenViewingItem is null)
        {
            return;
        }

        _screenViewingClientId = activity.Active ? activity.ClientId : null;
        _screenViewingDeviceName = activity.Active
            ? _pairingManager.GetDeviceName(activity.ClientId) ?? "paired device"
            : null;
        _screenViewingItem.Text = activity.Active
            ? $"Stop screen viewing - {_screenViewingDeviceName}"
            : "Stop screen viewing";
        if (_blockScreenViewingItem is { } blockScreenViewingItem)
        {
            blockScreenViewingItem.Text = activity.Active
                ? $"Disallow {_screenViewingDeviceName}"
                : "Disallow device";
        }
        _screenViewingItem.Visible = activity.Active;
        if (!activity.Active)
        {
            CloseScreenViewingMenus();
        }
        if (_screenViewingItem.Owner?.Items.Count > 1 && _screenViewingItem.Owner.Items[1].Tag as string == "screen-view-separator")
        {
            _screenViewingItem.Owner.Items[1].Visible = activity.Active;
        }
        _trayIcon.Text = BuildTrayTooltip(_connectionFeedbackController.DisplayedState);
        if (activity.Active)
        {
            ShowNotification(
                "Screen viewing active",
                $"{_screenViewingDeviceName} can see this display. Use the tray menu to stop immediately.",
                Forms.ToolTipIcon.Info);
        }
    }

    private async Task StopScreenViewingFromTrayAsync(bool disallow)
    {
        var clientId = _screenViewingClientId;
        CloseScreenViewingMenus();
        if (clientId is null)
        {
            return;
        }

        Task notification = _webHost.StopScreenViewingFromHostAsync(clientId, disallow);
        if (disallow)
        {
            BlockScreenViewingPermission(_pairingManager, clientId);
        }
        await notification;
    }

    private void CloseScreenViewingMenus()
    {
        _screenViewingItem?.DropDown.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
        _trayMenu.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
    }

    internal static bool BlockScreenViewingPermission(PairingManager pairingManager, string clientId)
    {
        var current = pairingManager.GetDevicePermissionOverrides(clientId);
        return pairingManager.SetDevicePermissionOverrides(
            clientId,
            current with { AllowScreenViewing = false });
    }

    private void HandleConnectionChanged()
    {
        if (_disposed)
        {
            return;
        }

        var hasActiveController = _pairingManager.HasActiveController;
        if (!_hadActiveController && hasActiveController && _mainWindow.ShouldCloseAfterDeviceConnected())
        {
            _mainWindow.Close();
        }

        _hadActiveController = hasActiveController;
    }

    private void ShowNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (_notificationSink is not null)
        {
            _notificationSink(title, message, icon);
            return;
        }

        _trayIcon.ShowBalloonTip(3000, title, message, icon);
    }

    internal void ShowPresentationBreakReminder()
    {
        ShowNotification(
            "Presentation break",
            "Break still active. Press Resume presentation to end it.",
            Forms.ToolTipIcon.Info);
    }

    private Icon GetTrayIcon(TrayConnectionState state) => _trayIcons.TryGetValue(state, out var icon)
        ? icon
        : _trayIcons[TrayConnectionState.NoDevicesRegistered];

    private string BuildTrayTooltip(TrayConnectionState state)
    {
        if (_screenViewingDeviceName is not null)
        {
            return TruncateTrayTooltip($"Voltura Air - screen viewed by {_screenViewingDeviceName}");
        }

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

    private static Task RunProtectedAsync(Func<Task> action) =>
        HostUiInputGuard.IsRecentProtectedClientInput() ? Task.CompletedTask : action();
}
