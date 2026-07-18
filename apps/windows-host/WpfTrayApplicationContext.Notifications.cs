using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed partial class WpfTrayApplicationContext
{
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

}
