using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed partial class WpfTrayApplicationContext
{
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

        OwnedDispatcherTimer? timer = null;
        timer = new OwnedDispatcherTimer(
            _dispatcher,
            TimeSpan.FromMilliseconds(StartupConnectionGracePeriodMs),
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
            ApplyTrayConnectionState();
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
            TimeSpan.FromMilliseconds(DisconnectNotificationDelayMs),
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

}
