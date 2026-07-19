using System.Globalization;
using System.Net.WebSockets;

namespace VolturaAir.Host;

internal sealed class PowerCommandHandler(
    ISystemPowerController powerController,
    IWorkstationLockPolicy workstationLockPolicy,
    HostStatusPayloadFactory statusFactory,
    WebSocketTransport transport,
    IAppLogWriter appLog)
{
    public async Task HandleAsync(WebSocket socket, string clientId, string operationId, string action, CancellationToken cancellationToken)
    {
        if (!SystemPowerActions.IsSupported(action))
        {
            LogAction(clientId, action, "unsupported");
            await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-UNSUPPORTED", "This power action is not supported.", cancellationToken);
            return;
        }

        if (!powerController.IsActionAvailable(action))
        {
            LogAction(clientId, action, "unavailable");
            await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-UNAVAILABLE", "This Windows feature is not available on the PC.", cancellationToken);
            return;
        }

        if (!CanExecute(clientId, action))
        {
            LogAction(clientId, action, "permission_denied");
            await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-DENIED", "This action is disabled by the PC host.", cancellationToken);
            return;
        }

        if (action == SystemPowerActions.Lock)
        {
            var lockStatus = workstationLockPolicy.GetStatus();
            if (lockStatus.State == WorkstationLockPolicyState.Disabled)
            {
                LogAction(clientId, action, "lock_disabled");
                await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-LOCK-DISABLED", "Windows locking is disabled. Enable it in the Voltura Air host settings.", cancellationToken);
                return;
            }

            if (lockStatus.State == WorkstationLockPolicyState.Unavailable)
            {
                WriteDiagnostic(action, "Lock policy status is unavailable.", null, lockStatus.Diagnostic);
                LogAction(clientId, action, "lock_policy_unavailable");
                await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-LOCK-UNAVAILABLE", "Windows rejected the lock request. Open the Voltura Air host settings, enable Windows locking, and try again.", cancellationToken);
                return;
            }
        }

        SystemPowerExecutionResult result;
        try
        {
            result = powerController.TryExecute(action);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            WriteDiagnostic(action, "The platform controller threw an exception.", null, ex.Message);
            LogAction(clientId, action, "execution_failed", detail: ex.Message);
            await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-EXECUTION-FAILED", GetFailureMessage(action), cancellationToken);
            return;
        }

        if (!result.Succeeded)
        {
            WriteDiagnostic(action, "Windows rejected the action.", result.Win32Error, null);
            LogAction(clientId, action, "execution_failed", result.Win32Error);
            await SendResultAsync(socket, clientId, operationId, action, false, "VAIR-POWER-EXECUTION-FAILED", GetFailureMessage(action), cancellationToken);
            return;
        }

        LogAction(clientId, action, action == SystemPowerActions.Lock ? "lock_request_accepted" : "request_accepted");
        await SendResultAsync(socket, clientId, operationId, action, true, null, GetSuccessMessage(action), cancellationToken);
    }

    private async Task SendResultAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string action,
        bool succeeded,
        string? code,
        string message,
        CancellationToken cancellationToken)
    {
        await transport.SendAsync(socket, new { type = "system.power.result", operationId, action, succeeded, code, message }, cancellationToken);
        appLog.Write(new AppLogEntry(
            Event: "response_sent",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: "system.power.result",
            Action: action,
            Outcome: succeeded ? "succeeded" : "failed",
            Code: code));
    }

    private bool CanExecute(string clientId, string action)
    {
        var permissions = statusFactory.GetEffectivePermissions(clientId);
        return action switch
        {
            SystemPowerActions.Lock => permissions.AllowPcLock,
            SystemPowerActions.BlackoutDisplay => permissions.AllowBlackoutDisplay,
            SystemPowerActions.DisplayOff => permissions.AllowDisplayOff,
            SystemPowerActions.ScreenSaver => permissions.AllowScreenSaver,
            SystemPowerActions.SignOut => permissions.AllowSignOut,
            SystemPowerActions.Restart => permissions.AllowRestart,
            SystemPowerActions.Shutdown => permissions.AllowShutdown,
            _ => false
        };
    }

    private static string GetFailureMessage(string action) => action == SystemPowerActions.Lock
        ? "Windows rejected the lock request. Open the Voltura Air host settings, enable Windows locking, and try again."
        : "Windows could not complete this action. Try again on the PC.";

    private static string GetSuccessMessage(string action) => action switch
    {
        SystemPowerActions.Lock => "Windows accepted the lock request.",
        SystemPowerActions.BlackoutDisplay => "Displays are blacked out. Move the pointer or press any key to restore them.",
        SystemPowerActions.DisplayOff => "Windows accepted the display-off request. Some PCs enter sleep or Modern Standby and then require physical input to wake.",
        SystemPowerActions.ScreenSaver => "Windows accepted the screen-saver request.",
        SystemPowerActions.SignOut => "Sign-out request accepted by Windows.",
        SystemPowerActions.Restart => "Restart request accepted by Windows.",
        SystemPowerActions.Shutdown => "Shut-down request accepted by Windows.",
        _ => "Power request accepted by Windows."
    };

    private static void WriteDiagnostic(string action, string reason, int? win32Error, string? detail)
    {
        Console.Error.WriteLine(
            "Voltura Air power action failed: action={0}, reason={1}, win32={2}, detail={3}",
            action,
            reason,
            win32Error?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
            detail ?? "n/a");
    }

    private void LogAction(
        string clientId,
        string action,
        string outcome,
        int? win32Error = null,
        string? detail = null)
    {
        appLog.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: "system.power",
            Action: action,
            Outcome: outcome,
            Win32Error: win32Error,
            Detail: detail));
    }
}
