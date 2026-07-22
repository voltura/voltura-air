using System.Windows;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Connection;

internal enum ConnectionConfirmation
{
    RestartWithPairedDevices,
    DiscardPendingChanges
}

internal sealed class ConnectionPageBoundaryFeedback(
    Window owner,
    IAppLogWriter appLog,
    Func<ConnectionConfirmation, bool>? confirm = null)
{
    public bool Confirm(ConnectionConfirmation confirmation) => confirm?.Invoke(confirmation) ?? confirmation switch
    {
        ConnectionConfirmation.RestartWithPairedDevices => ThemedConfirmationDialog.Show(
            owner,
            "Restart Voltura Air?",
            "Connected devices will disconnect. After restart, scan the pairing code displayed by Voltura Air on each device to update its connection.",
            "Save and restart",
            "Cancel",
            ConfirmationTone.Question),
        ConnectionConfirmation.DiscardPendingChanges => ThemedConfirmationDialog.Show(
            owner,
            "Discard connection changes?",
            "Your pending adapter and port changes will be discarded.",
            "Discard changes",
            "Keep editing",
            ConfirmationTone.Question),
        _ => false
    };

    public void LogFailure(string action, Exception exception)
    {
        appLog.Write(new AppLogEntry(
            "host_action",
            "windows_host",
            Action: action,
            Outcome: "failed",
            Detail: exception.GetType().Name));
    }
}
