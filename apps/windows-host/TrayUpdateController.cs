using System.Windows.Threading;
using Forms = System.Windows.Forms;
using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host;

internal sealed class TrayUpdateController(
    Dispatcher dispatcher,
    UpdateService updates,
    Forms.ToolStripMenuItem updateItem,
    Action<string, string, Forms.ToolTipIcon, Action?> showNotification,
    Action showReadyUpdate) : IDisposable
{
    private bool _disposed;

    public void Start(UpdateStartupOutcome startupOutcome)
    {
        updates.StateChanged += OnUpdateStateChanged;
        updates.NotificationRequested += OnUpdateNotificationRequested;
        ApplyState();
        ShowStartupOutcome(startupOutcome);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        updates.StateChanged -= OnUpdateStateChanged;
        updates.NotificationRequested -= OnUpdateNotificationRequested;
    }

    private void OnUpdateStateChanged(object? sender, EventArgs eventArgs) =>
        _ = dispatcher.BeginInvoke(ApplyState);

    private void OnUpdateNotificationRequested(object? sender, UpdateNotificationEventArgs eventArgs) =>
        _ = dispatcher.BeginInvoke(() => ShowUpdateNotification(eventArgs));

    private void ShowUpdateNotification(UpdateNotificationEventArgs notification)
    {
        if (_disposed)
        {
            return;
        }

        var (title, message, icon) = notification.Kind switch
        {
            UpdateNotificationKind.UpToDate => ("Updates", $"Voltura Air {notification.Version} is up to date.", Forms.ToolTipIcon.Info),
            UpdateNotificationKind.WaitingForDevices => ("Updates", $"Version {notification.Version} found. Download starts when devices disconnect.", Forms.ToolTipIcon.Info),
            UpdateNotificationKind.Ready => ("Update ready", $"Version {notification.Version} is ready to install.", Forms.ToolTipIcon.Info),
            UpdateNotificationKind.InvalidStagedUpdate => ("Update unavailable", "The downloaded update couldn't be verified. Check for updates again.", Forms.ToolTipIcon.Warning),
            UpdateNotificationKind.InstallFailed => ("Update failed", "Couldn't start the update installer. Voltura Air is still running. Try again.", Forms.ToolTipIcon.Warning),
            _ => ("Updates", "Couldn't check for updates. Try again later.", Forms.ToolTipIcon.Warning)
        };
        showNotification(
            title,
            message,
            icon,
            NotificationAction(notification.Kind, showReadyUpdate));
    }

    internal static Action? NotificationAction(UpdateNotificationKind kind, Action showReadyUpdate) =>
        kind == UpdateNotificationKind.Ready ? showReadyUpdate : null;

    private void ShowStartupOutcome(UpdateStartupOutcome outcome)
    {
        switch (outcome)
        {
            case UpdateStartupOutcome.Updated:
                var version = typeof(WpfTrayApplicationContext).Assembly.GetName().Version;
                showNotification(
                    "Voltura Air updated",
                    $"Updated to version {version?.Major}.{version?.Minor}.{version?.Build}.",
                    Forms.ToolTipIcon.Info,
                    null);
                break;
            case UpdateStartupOutcome.Failed:
                showNotification(
                    "Update failed",
                    "Voltura Air is still available. Try installing the update again from the tray menu.",
                    Forms.ToolTipIcon.Warning,
                    null);
                break;
        }
    }

    private void ApplyState()
    {
        if (_disposed)
        {
            return;
        }

        var version = updates.TargetVersion;
        (updateItem.Text, updateItem.Enabled) = updates.State switch
        {
            UpdateState.Checking => ("Checking for updates…", false),
            UpdateState.WaitingForDevices => ($"Waiting to download version {version}…", false),
            UpdateState.Downloading => ($"Downloading version {version}…", false),
            UpdateState.Ready => ($"Install version {version} and restart", true),
            _ => ("Check for updates", true)
        };
    }
}
