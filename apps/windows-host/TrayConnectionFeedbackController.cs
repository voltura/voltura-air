using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed record DeviceConnectionNotification(string Title, string Message, string? ClientId);

internal sealed class TrayConnectionFeedbackController : IDisposable
{
    private static readonly TimeSpan DisconnectNotificationDelay = TimeSpan.FromMilliseconds(1800);
    // Covers the mobile client's 3-second connection deadline, 1.2-second retry delay, and LAN handshake time.
    private static readonly TimeSpan StartupConnectionGracePeriod = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly Action<TrayConnectionState> _applyState;
    private readonly Action<string, string, Forms.ToolTipIcon, Action?> _showNotification;
    private readonly Func<bool> _canShowNotification;
    private readonly Func<string, string, Forms.ToolTipIcon, Action?, bool> _tryShowNotification;
    private readonly Action _showConnectPage;
    private readonly Action<string> _showDeviceAccess;
    private readonly TrayConnectionIndicator _indicator;
    private readonly OwnedDispatcherAction _connectionChangedAction;
    private readonly OwnedDispatcherAction _remoteInputBlockedAction;
    private OwnedDispatcherTimer? _pendingDisconnectNotification;
    private OwnedDispatcherTimer? _pendingStartupConnectionGrace;
    private bool _initialNoticeDisplayActive;
    private bool _hadActiveController;
    private bool _started;
    private bool _disposed;

    public TrayConnectionFeedbackController(
        Dispatcher dispatcher,
        PairingManager pairingManager,
        WebHostService webHost,
        Action<TrayConnectionState> applyState,
        Action<string, string, Forms.ToolTipIcon, Action?> showNotification,
        Func<bool> canShowNotification,
        Func<string, string, Forms.ToolTipIcon, Action?, bool> tryShowNotification,
        Action showConnectPage,
        Action<string> showDeviceAccess)
    {
        _dispatcher = dispatcher;
        _pairingManager = pairingManager;
        _webHost = webHost;
        _applyState = applyState;
        _showNotification = showNotification;
        _canShowNotification = canShowNotification;
        _tryShowNotification = tryShowNotification;
        _showConnectPage = showConnectPage;
        _showDeviceAccess = showDeviceAccess;
        _hadActiveController = pairingManager.HasActiveController;
        _indicator = new TrayConnectionIndicator(
            pairingManager.IsPaired,
            _hadActiveController,
            holdInitialDisconnectedState: pairingManager.IsPaired && !_hadActiveController);
        _connectionChangedAction = new OwnedDispatcherAction(_dispatcher, HandleConnectionChanged);
        _remoteInputBlockedAction = new OwnedDispatcherAction(_dispatcher, ReportRemoteInputBlockedIfCurrent);
    }

    public TrayConnectionState DisplayedState => _indicator.DisplayedState;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _pairingManager.ConnectionChanged += OnConnectionChanged;
        _webHost.ControllerSocketClosed += OnControllerSocketClosed;
        _webHost.RemoteInputBlockedChanged += OnRemoteInputBlockedChanged;
        ScheduleStartupConnectionGrace();

        if (_webHost.IsInputBlockedByElevation)
        {
            ReportRemoteInputBlocked();
        }

        _connectionChangedAction.Queue();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectionChangedAction.Dispose();
        _remoteInputBlockedAction.Dispose();
        CancelStartupConnectionGrace();
        CancelPendingDisconnectNotification();
        if (_started)
        {
            _pairingManager.ConnectionChanged -= OnConnectionChanged;
            _webHost.ControllerSocketClosed -= OnControllerSocketClosed;
            _webHost.RemoteInputBlockedChanged -= OnRemoteInputBlockedChanged;
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _connectionChangedAction.Queue();

    private void HandleConnectionChanged()
    {
        if (_disposed)
        {
            return;
        }

        var hasActiveController = _pairingManager.HasActiveController;
        var showedMandatoryNotice = ShowPendingInitialNotice() ||
            _pairingManager.HasActivePendingInitialDeviceConnectionNotice;
        if (hasActiveController)
        {
            CancelStartupConnectionGrace();
        }

        if (!_hadActiveController && hasActiveController)
        {
            var cancelledTransientDisconnect = CancelPendingDisconnectNotification();
            ApplyCurrentState();
            if (ShouldShowOptionalConnectedNotification(
                becameActive: true,
                cancelledTransientDisconnect,
                showedMandatoryNotice))
            {
                ShowOptionalConnectedNotification();
            }
        }
        else if (_hadActiveController && !hasActiveController)
        {
            ScheduleDisconnectNotification();
            ApplyCurrentState(holdConnectedDuringReconnect: true);
        }
        else
        {
            ApplyCurrentState(holdConnectedDuringReconnect: _pendingDisconnectNotification is not null);
        }

        _hadActiveController = hasActiveController;
    }

    private bool ShowPendingInitialNotice()
    {
        if (_initialNoticeDisplayActive)
        {
            return true;
        }

        if (!_canShowNotification())
        {
            return _pairingManager.HasActivePendingInitialDeviceConnectionNotice;
        }

        if (!_pairingManager.TryTakeInitialDeviceConnectionNotice(out var notice) || notice is null)
        {
            return false;
        }

        _initialNoticeDisplayActive = true;
        var notification = CreateDeviceNotification(
            notice.ClientId,
            notice.DeviceName,
            notice.AccessProfile);
        if (!_tryShowNotification(
            notification.Title,
            notification.Message,
            Forms.ToolTipIcon.Info,
            () => _showDeviceAccess(notice.ClientId)))
        {
            throw new InvalidOperationException("The available tray notification slot rejected a mandatory notice.");
        }
        return true;
    }

    internal void OnNotificationSlotAvailable()
    {
        if (_disposed)
        {
            return;
        }

        _initialNoticeDisplayActive = false;
        ShowPendingInitialNotice();
    }

    private void ShowOptionalConnectedNotification()
    {
        var activeDevices = _pairingManager.GetDevices().Where(device => device.IsActive).ToArray();
        var notification = CreateOptionalConnectedNotification(
            activeDevices,
            _pairingManager.ActiveDeviceSummary);
        ShowConnectionNotification(
            notification.Title,
            notification.Message,
            Forms.ToolTipIcon.Info,
            notification.ClientId is { } clientId
                ? () => _showDeviceAccess(clientId)
                : null);
    }

    internal static DeviceConnectionNotification CreateOptionalConnectedNotification(
        IReadOnlyList<PairedDeviceStatus> activeDevices,
        string activeDeviceSummary)
    {
        if (activeDevices.Count == 1)
        {
            var device = activeDevices[0];
            return CreateDeviceNotification(
                device.ClientId,
                device.DeviceName,
                device.AccessProfile);
        }

        return new DeviceConnectionNotification(
            "Voltura Air paired",
            $"{activeDeviceSummary} connected.",
            null);
    }

    internal static bool ShouldShowOptionalConnectedNotification(
        bool becameActive,
        bool cancelledTransientDisconnect,
        bool showedMandatoryNotice) =>
        becameActive && !cancelledTransientDisconnect && !showedMandatoryNotice;

    internal static DeviceConnectionNotification CreateDeviceNotification(
        string clientId,
        string deviceName,
        DeviceAccessProfile profile) => new(
            "Device connected",
            $"{deviceName} uses {DeviceAccessProfiles.GetDisplayName(profile)} access. Click to change.",
            clientId);

    private void ApplyCurrentState(bool holdConnectedDuringReconnect = false)
    {
        if (_disposed)
        {
            return;
        }

        var state = _indicator.Update(
            _pairingManager.IsPaired,
            _pairingManager.HasActiveController,
            holdConnectedDuringReconnect,
            holdInitialDisconnectedState: _pendingStartupConnectionGrace is not null);
        _applyState(state);
    }

    private void ScheduleStartupConnectionGrace()
    {
        if (_pairingManager.HasActiveController || !_pairingManager.IsPaired)
        {
            return;
        }

        OwnedDispatcherTimer? timer = null;
        timer = new OwnedDispatcherTimer(
            _dispatcher,
            StartupConnectionGracePeriod,
            () => OnStartupConnectionGraceElapsed(timer));
        _pendingStartupConnectionGrace = timer;
        timer.Start();
    }

    private void OnStartupConnectionGraceElapsed(OwnedDispatcherTimer? timer)
    {
        if (timer is null || !ReferenceEquals(_pendingStartupConnectionGrace, timer))
        {
            return;
        }

        _pendingStartupConnectionGrace = null;
        if (!_disposed)
        {
            ApplyCurrentState();
        }
    }

    private void CancelStartupConnectionGrace()
    {
        var timer = _pendingStartupConnectionGrace;
        if (timer is null)
        {
            return;
        }

        _pendingStartupConnectionGrace = null;
        timer.Dispose();
    }

    private void ScheduleDisconnectNotification()
    {
        CancelPendingDisconnectNotification();

        OwnedDispatcherTimer? timer = null;
        timer = new OwnedDispatcherTimer(
            _dispatcher,
            DisconnectNotificationDelay,
            () => OnDisconnectNotificationElapsed(timer));
        _pendingDisconnectNotification = timer;
        timer.Start();
    }

    private bool CancelPendingDisconnectNotification()
    {
        var timer = _pendingDisconnectNotification;
        if (timer is null)
        {
            return false;
        }

        _pendingDisconnectNotification = null;
        timer.Dispose();
        return true;
    }

    private void OnDisconnectNotificationElapsed(OwnedDispatcherTimer? timer)
    {
        if (timer is null || !ReferenceEquals(_pendingDisconnectNotification, timer))
        {
            return;
        }

        _pendingDisconnectNotification = null;
        if (_disposed || _pairingManager.HasActiveController)
        {
            return;
        }

        ApplyCurrentState();
        if (AppNotificationSettings.ShowPairingWindowOnDisconnect())
        {
            _showConnectPage();
        }

        ShowConnectionNotification(
            "Voltura Air disconnected",
            "No connected devices.",
            Forms.ToolTipIcon.Info,
            action: null);
    }

    private void OnControllerSocketClosed(object? sender, ControllerSocketClosedEventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ShowConnectionNotification(
                    "Voltura Air connection closed",
                    $"A controller connection was closed: {e.Reason}. The phone will reconnect automatically.",
                    Forms.ToolTipIcon.Warning,
                    action: null);
            }
        });
    }

    private void OnRemoteInputBlockedChanged(object? sender, RemoteInputBlockedChangedEventArgs e)
    {
        if (e.IsBlocked)
        {
            _remoteInputBlockedAction.Queue();
        }
    }

    private void ReportRemoteInputBlockedIfCurrent()
    {
        if (_webHost.IsInputBlockedByElevation)
        {
            ReportRemoteInputBlocked();
        }
    }

    private void ReportRemoteInputBlocked()
    {
        if (!_disposed && RemoteInputBlockedTrayNotification.ShouldShow(true, _pairingManager.HasActiveController))
        {
            _showNotification(
                RemoteInputBlockedTrayNotification.Title,
                RemoteInputBlockedTrayNotification.Message,
                Forms.ToolTipIcon.Warning,
                null);
        }
    }

    private void ShowConnectionNotification(
        string title,
        string message,
        Forms.ToolTipIcon icon,
        Action? action = null)
    {
        if (AppNotificationSettings.ShowConnectionStatusNotifications())
        {
            _showNotification(title, message, icon, action);
        }
    }
}
