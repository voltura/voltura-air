using Forms = System.Windows.Forms;
using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host;

internal sealed partial class WpfTrayApplicationContext
{
    private void OnUpdateStateChanged(object? sender, EventArgs e) => _ = _dispatcher.BeginInvoke(ApplyUpdateState);

    private void OnUpdateNotificationRequested(object? sender, UpdateNotificationEventArgs e) => _ = _dispatcher.BeginInvoke(() =>
    {
        if (_disposed) return;
        var (title, message, icon) = e.Kind switch
        {
            UpdateNotificationKind.UpToDate => ("Updates", $"Voltura Air {e.Version} is up to date.", Forms.ToolTipIcon.Info),
            UpdateNotificationKind.WaitingForDevices => ("Updates", $"Version {e.Version} found. Download starts when devices disconnect.", Forms.ToolTipIcon.Info),
            UpdateNotificationKind.Ready => ("Update ready", $"Version {e.Version} is ready to install.", Forms.ToolTipIcon.Info),
            UpdateNotificationKind.InvalidStagedUpdate => ("Update unavailable", "The downloaded update couldn't be verified. Check for updates again.", Forms.ToolTipIcon.Warning),
            UpdateNotificationKind.InstallFailed => ("Update failed", "Couldn't start the update installer. Voltura Air is still running. Try again.", Forms.ToolTipIcon.Warning),
            _ => ("Updates", "Couldn't check for updates. Try again later.", Forms.ToolTipIcon.Warning)
        };
        ShowNotification(title, message, icon);
    });

    private void ShowStartupOutcome(UpdateStartupOutcome outcome)
    {
        switch (outcome)
        {
            case UpdateStartupOutcome.Updated:
                var version = typeof(WpfTrayApplicationContext).Assembly.GetName().Version;
                ShowNotification(
                    "Voltura Air updated",
                    $"Updated to version {version?.Major}.{version?.Minor}.{version?.Build}.",
                    Forms.ToolTipIcon.Info);
                break;
            case UpdateStartupOutcome.Failed:
                ShowNotification(
                    "Update failed",
                    "Voltura Air is still available. Try installing the update again from the tray menu.",
                    Forms.ToolTipIcon.Warning);
                break;
        }
    }

    private void ApplyUpdateState()
    {
        if (_disposed || _updateItem is null || _updates is null) return;
        var version = _updates.TargetVersion;
        (_updateItem.Text, _updateItem.Enabled) = _updates.State switch
        {
            UpdateState.Checking => ("Checking for updates…", false),
            UpdateState.WaitingForDevices => ($"Waiting to download version {version}…", false),
            UpdateState.Downloading => ($"Downloading version {version}…", false),
            UpdateState.Ready => ($"Install version {version} and restart", true),
            _ => ("Check for updates", true)
        };
    }
}
