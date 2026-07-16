using System.ComponentModel;
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
    private CancellationTokenSource? _pendingDisconnectNotification;
    private CancellationTokenSource? _pendingStartupConnectionGrace;
    private bool _hadActiveController;
    private bool _disposed;

    public WpfTrayApplicationContext(MainWindow mainWindow, WebHostService webHost, PairingManager pairingManager, IAwakeService awakeService)
    {
        _mainWindow = mainWindow;
        _dispatcher = mainWindow.Dispatcher;
        _webHost = webHost;
        _pairingManager = pairingManager;
        _awakeService = awakeService;
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
        _trayMenu.Items.Add("Exit", null, (_, _) => RunTrayCommand(ExitApplication));
    }

    private void BuildAwakeMenu()
    {
        var awakeMenu = new Forms.ToolStripMenuItem("Keep awake");
        _awakeOffItem = new Forms.ToolStripMenuItem("Use selected power plan", null, (_, _) => RunTrayCommand(() => ApplyAwake(_awakeService.SetOff())));
        _awakeTimedItem = new Forms.ToolStripMenuItem("For an interval");
        AddAwakeInterval(_awakeTimedItem, "30 minutes", 30);
        AddAwakeInterval(_awakeTimedItem, "1 hour", 60);
        AddAwakeInterval(_awakeTimedItem, "2 hours", 120);
        _awakeExpirationItem = new Forms.ToolStripMenuItem("Until...", null, (_, _) => RunTrayCommand(_mainWindow.ShowAwakePreferences));
        _awakeIndefiniteItem = new Forms.ToolStripMenuItem("Indefinitely", null, (_, _) => RunTrayCommand(() => ApplyAwake(_awakeService.SetIndefinite())));
        _awakeKeepScreenOnItem = new Forms.ToolStripMenuItem("Keep screen on", null, (_, _) => RunTrayCommand(() =>
            ApplyAwake(_awakeService.SetKeepScreenOn(!_awakeService.State.KeepScreenOn))));

        awakeMenu.DropDownItems.Add(_awakeOffItem);
        awakeMenu.DropDownItems.Add(_awakeTimedItem);
        awakeMenu.DropDownItems.Add(_awakeExpirationItem);
        awakeMenu.DropDownItems.Add(_awakeIndefiniteItem);
        awakeMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        awakeMenu.DropDownItems.Add(_awakeKeepScreenOnItem);
        _trayMenu.Items.Add(awakeMenu);
        ApplyAwakeMenuState();
    }

    private void AddAwakeInterval(Forms.ToolStripMenuItem parent, string label, int minutes)
    {
        parent.DropDownItems.Add(label, null, (_, _) => RunTrayCommand(() => ApplyAwake(_awakeService.SetTimed(TimeSpan.FromMinutes(minutes)))));
    }

    private void ApplyAwake(AwakeOperationResult result)
    {
        if (!result.Succeeded)
        {
            _trayIcon.ShowBalloonTip(3000, "Keep awake", result.Error ?? "Windows rejected the request.", Forms.ToolTipIcon.Warning);
        }
    }

    private void ApplyAwakeMenuState()
    {
        var state = _awakeService.State;
        _awakeOffItem.Checked = state.Mode == AwakeMode.Off;
        _awakeTimedItem.Checked = state.Mode == AwakeMode.Timed;
        _awakeExpirationItem.Checked = state.Mode == AwakeMode.Expiration;
        _awakeIndefiniteItem.Checked = state.Mode == AwakeMode.Indefinite;
        _awakeKeepScreenOnItem.Checked = state.KeepScreenOn;
        _awakeKeepScreenOnItem.Enabled = state.IsActive;
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

        foreach (var item in EnumerateMenuItems(_trayMenu.Items))
        {
            item.BackColor = theme.Surface;
            item.ForeColor = theme.Text;
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.BeginInvoke(HandleConnectionChanged);
    }

    private void HandleConnectionChanged()
    {
        if (_disposed)
        {
            return;
        }

        var hasActiveController = _pairingManager.HasActiveController;
        if (hasActiveController)
        {
            CancelStartupConnectionGrace();
        }

        if (!_hadActiveController && hasActiveController)
        {
            var cancelledTransientDisconnect = CancelPendingDisconnectNotification();
            ApplyTrayConnectionState();
            if (!cancelledTransientDisconnect)
            {
                ShowConnectionStatusNotification(
                    "Voltura Air paired",
                    $"{_pairingManager.ActiveDeviceSummary} connected.",
                    Forms.ToolTipIcon.Info);
            }
        }
        else if (_hadActiveController && !hasActiveController)
        {
            ScheduleDisconnectNotification();
            ApplyTrayConnectionState(holdConnectedDuringReconnect: true);
        }
        else
        {
            ApplyTrayConnectionState(holdConnectedDuringReconnect: _pendingDisconnectNotification is not null);
        }

        _hadActiveController = hasActiveController;
    }

    private void ScheduleStartupConnectionGrace()
    {
        if (_pairingManager.HasActiveController || !_pairingManager.IsPaired)
        {
            return;
        }

        var pending = new CancellationTokenSource();
        _pendingStartupConnectionGrace = pending;
        var token = pending.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StartupConnectionGracePeriodMs, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!token.IsCancellationRequested)
            {
                _ = _dispatcher.BeginInvoke(() => CompleteStartupConnectionGrace(pending));
            }
        });
    }

    private void CompleteStartupConnectionGrace(CancellationTokenSource pending)
    {
        if (_disposed || !ReferenceEquals(_pendingStartupConnectionGrace, pending))
        {
            return;
        }

        _pendingStartupConnectionGrace = null;
        try
        {
            ApplyTrayConnectionState();
        }
        finally
        {
            pending.Dispose();
        }
    }

    private void CancelStartupConnectionGrace()
    {
        var pending = _pendingStartupConnectionGrace;
        if (pending is null)
        {
            return;
        }

        _pendingStartupConnectionGrace = null;
        try
        {
            pending.Cancel();
        }
        finally
        {
            pending.Dispose();
        }
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

            if (token.IsCancellationRequested)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(() => ShowPendingDisconnectNotification(pending));
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
        try
        {
            pending.Cancel();
        }
        finally
        {
            pending.Dispose();
        }

        return true;
    }

    private void ShowPendingDisconnectNotification(CancellationTokenSource pending)
    {
        if (_disposed || !ReferenceEquals(_pendingDisconnectNotification, pending))
        {
            return;
        }

        _pendingDisconnectNotification = null;
        try
        {
            if (_pairingManager.HasActiveController)
            {
                return;
            }

            ApplyTrayConnectionState();

            if (AppNotificationSettings.ShowPairingWindowOnDisconnect())
            {
                _mainWindow.ShowPage(HostPage.Connect);
            }

            ShowConnectionStatusNotification(
                "Voltura Air disconnected",
                "No connected devices.",
                Forms.ToolTipIcon.Info);
        }
        finally
        {
            pending.Dispose();
        }
    }

    private void ShowConnectionStatusNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (AppNotificationSettings.ShowConnectionStatusNotifications())
        {
            _trayIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }

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

    private void OnControllerSocketClosed(object? sender, ControllerSocketClosedEventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            ShowConnectionStatusNotification(
                "Voltura Air connection closed",
                $"A controller connection was closed: {e.Reason}. The phone will reconnect automatically.",
                Forms.ToolTipIcon.Warning);
        });
    }

    private void OnRemoteInputBlockedChanged(object? sender, RemoteInputBlockedChangedEventArgs e)
    {
        if (!e.IsBlocked)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && RemoteInputBlockedTrayNotification.ShouldShow(e.IsBlocked, _pairingManager.HasActiveController))
            {
                _trayIcon.ShowBalloonTip(
                    4000,
                    RemoteInputBlockedTrayNotification.Title,
                    RemoteInputBlockedTrayNotification.Message,
                    Forms.ToolTipIcon.Warning);
            }
        });
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

    private void OnAwakeStateChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ApplyAwakeMenuState();
            }
        });
    }

    private void ApplyTrayConnectionState(bool holdConnectedDuringReconnect = false)
    {
        if (_disposed)
        {
            return;
        }

        var state = _trayConnectionIndicator.Update(
            _pairingManager.IsPaired,
            _pairingManager.HasActiveController,
            holdConnectedDuringReconnect,
            holdInitialDisconnectedState: _pendingStartupConnectionGrace is not null);
        _trayIcon.Text = BuildTrayTooltip(state);
        _trayIcon.Icon = GetTrayIcon(state);
    }

    private Icon GetTrayIcon(TrayConnectionState state)
    {
        return _trayIcons.TryGetValue(state, out var icon)
            ? icon
            : _trayIcons[TrayConnectionState.NoDevicesRegistered];
    }

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

    private static string TruncateTrayTooltip(string value)
    {
        return value.Length <= MaxTrayTooltipLength ? value : $"{value[..(MaxTrayTooltipLength - 3)]}...";
    }

    private static void OpenProductSite()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
