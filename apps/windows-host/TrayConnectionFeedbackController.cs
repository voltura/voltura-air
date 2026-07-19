using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class TrayConnectionFeedbackController : IDisposable
{
    private static readonly TimeSpan DisconnectNotificationDelay = TimeSpan.FromMilliseconds(1800);
    // Covers the mobile client's 3-second connection deadline, 1.2-second retry delay, and LAN handshake time.
    private static readonly TimeSpan StartupConnectionGracePeriod = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private readonly PairingManager _pairingManager;
    private readonly WebHostService _webHost;
    private readonly Action<TrayConnectionState> _applyState;
    private readonly Action<string, string, Forms.ToolTipIcon> _showNotification;
    private readonly Action _showConnectPage;
    private readonly TrayConnectionIndicator _indicator;
    private OwnedDispatcherTimer? _pendingDisconnectNotification;
    private OwnedDispatcherTimer? _pendingStartupConnectionGrace;
    private bool _hadActiveController;
    private bool _started;
    private bool _disposed;

    public TrayConnectionFeedbackController(
        Dispatcher dispatcher,
        PairingManager pairingManager,
        WebHostService webHost,
        Action<TrayConnectionState> applyState,
        Action<string, string, Forms.ToolTipIcon> showNotification,
        Action showConnectPage)
    {
        _dispatcher = dispatcher;
        _pairingManager = pairingManager;
        _webHost = webHost;
        _applyState = applyState;
        _showNotification = showNotification;
        _showConnectPage = showConnectPage;
        _hadActiveController = pairingManager.HasActiveController;
        _indicator = new TrayConnectionIndicator(
            pairingManager.IsPaired,
            _hadActiveController,
            holdInitialDisconnectedState: pairingManager.IsPaired && !_hadActiveController);
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
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelStartupConnectionGrace();
        CancelPendingDisconnectNotification();
        if (_started)
        {
            _pairingManager.ConnectionChanged -= OnConnectionChanged;
            _webHost.ControllerSocketClosed -= OnControllerSocketClosed;
            _webHost.RemoteInputBlockedChanged -= OnRemoteInputBlockedChanged;
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _ = _dispatcher.BeginInvoke(HandleConnectionChanged);

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
            ApplyCurrentState();
            if (!cancelledTransientDisconnect)
            {
                ShowConnectionNotification(
                    "Voltura Air paired",
                    $"{_pairingManager.ActiveDeviceSummary} connected.",
                    Forms.ToolTipIcon.Info);
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
            Forms.ToolTipIcon.Info);
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
                    Forms.ToolTipIcon.Warning);
            }
        });
    }

    private void OnRemoteInputBlockedChanged(object? sender, RemoteInputBlockedChangedEventArgs e)
    {
        if (e.IsBlocked)
        {
            _ = _dispatcher.BeginInvoke(ReportRemoteInputBlocked);
        }
    }

    private void ReportRemoteInputBlocked()
    {
        if (!_disposed && RemoteInputBlockedTrayNotification.ShouldShow(true, _pairingManager.HasActiveController))
        {
            _showNotification(
                RemoteInputBlockedTrayNotification.Title,
                RemoteInputBlockedTrayNotification.Message,
                Forms.ToolTipIcon.Warning);
        }
    }

    private void ShowConnectionNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (AppNotificationSettings.ShowConnectionStatusNotifications())
        {
            _showNotification(title, message, icon);
        }
    }
}
