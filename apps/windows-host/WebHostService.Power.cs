using System.Net.WebSockets;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private object CreatePowerCapabilities(string clientId)
    {
        var permissions = _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load());
        var lockStatus = _workstationLockPolicy.GetStatus();
        return new
        {
            @lock = permissions.AllowPcLock,
            lockAvailability = ToProtocolLockAvailability(lockStatus.State),
            blackoutDisplay = permissions.AllowBlackoutDisplay,
            displayOff = permissions.AllowDisplayOff,
            screenSaver = permissions.AllowScreenSaver,
            screenSaverAvailable = _powerController.IsActionAvailable(SystemPowerActions.ScreenSaver),
            signOut = permissions.AllowSignOut,
            restart = permissions.AllowRestart,
            shutdown = permissions.AllowShutdown
        };
    }

    private async Task HandlePowerActionAsync(WebSocket socket, string clientId, string action, CancellationToken cancellationToken)
    {
        if (!SystemPowerActions.IsSupported(action))
        {
            LogPowerAction(clientId, action, "unsupported");
            await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-UNSUPPORTED", "This power action is not supported.", cancellationToken);
            return;
        }

        if (!_powerController.IsActionAvailable(action))
        {
            LogPowerAction(clientId, action, "unavailable");
            await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-UNAVAILABLE", "This Windows feature is not available on the PC.", cancellationToken);
            return;
        }

        if (!CanExecutePowerAction(clientId, action))
        {
            LogPowerAction(clientId, action, "permission_denied");
            await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-DENIED", "This action is disabled by the PC host.", cancellationToken);
            return;
        }

        if (action == SystemPowerActions.Lock)
        {
            var lockStatus = _workstationLockPolicy.GetStatus();
            if (lockStatus.State == WorkstationLockPolicyState.Disabled)
            {
                LogPowerAction(clientId, action, "lock_disabled");
                await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-LOCK-DISABLED", "Windows locking is disabled. Enable it in the Voltura Air host settings.", cancellationToken);
                return;
            }

            if (lockStatus.State == WorkstationLockPolicyState.Unavailable)
            {
                WritePowerDiagnostic(action, "Lock policy status is unavailable.", null, lockStatus.Diagnostic);
                LogPowerAction(clientId, action, "lock_policy_unavailable");
                await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-LOCK-UNAVAILABLE", "Windows rejected the lock request. Open the Voltura Air host settings, enable Windows locking, and try again.", cancellationToken);
                return;
            }
        }

        SystemPowerExecutionResult result;
        try
        {
            result = _powerController.TryExecute(action);
        }
        catch (Exception ex) when (ex is not WebSocketException and not OperationCanceledException and not ObjectDisposedException)
        {
            WritePowerDiagnostic(action, "The platform controller threw an exception.", null, ex.Message);
            LogPowerAction(clientId, action, "execution_failed", detail: ex.Message);
            await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-EXECUTION-FAILED", action == SystemPowerActions.Lock
                ? "Windows rejected the lock request. Open the Voltura Air host settings, enable Windows locking, and try again."
                : "Windows could not complete this action. Try again on the PC.", cancellationToken);
            return;
        }

        if (!result.Succeeded)
        {
            WritePowerDiagnostic(action, "Windows rejected the action.", result.Win32Error, null);
            LogPowerAction(clientId, action, "execution_failed", result.Win32Error);
            await SendPowerResultAsync(socket, clientId, action, false, "VAIR-POWER-EXECUTION-FAILED", action == SystemPowerActions.Lock
                ? "Windows rejected the lock request. Open the Voltura Air host settings, enable Windows locking, and try again."
                : "Windows could not complete this action. Try again on the PC.", cancellationToken);
            return;
        }

        LogPowerAction(clientId, action, action == SystemPowerActions.Lock ? "lock_request_accepted" : "request_accepted");
        await SendPowerResultAsync(socket, clientId, action, true, null, GetPowerSuccessMessage(action), cancellationToken);
    }

    private async Task SendPowerResultAsync(
        WebSocket socket,
        string clientId,
        string action,
        bool succeeded,
        string? code,
        string message,
        CancellationToken cancellationToken)
    {
        await SendSocketAsync(socket, new { type = "system.power.result", action, succeeded, code, message }, cancellationToken);
        _appLog.Write(new AppLogEntry(
            Event: "response_sent",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: "system.power.result",
            Action: action,
            Outcome: succeeded ? "succeeded" : "failed",
            Code: code));
    }

    private static string ToProtocolLockAvailability(WorkstationLockPolicyState state)
    {
        return state switch
        {
            WorkstationLockPolicyState.NotExplicitlyDisabled => "notExplicitlyDisabled",
            WorkstationLockPolicyState.Disabled => "disabledByPolicy",
            _ => "unavailable"
        };
    }

    private static string GetPowerSuccessMessage(string action)
    {
        return action switch
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
    }

    private static void WritePowerDiagnostic(string action, string reason, int? win32Error, string? detail)
    {
        Console.Error.WriteLine(
            "Voltura Air power action failed: action={0}, reason={1}, win32={2}, detail={3}",
            action,
            reason,
            win32Error?.ToString() ?? "n/a",
            detail ?? "n/a");
    }

    private void LogPowerAction(
        string clientId,
        string action,
        string outcome,
        int? win32Error = null,
        string? detail = null)
    {
        _appLog.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: "system.power",
            Action: action,
            Outcome: outcome,
            Win32Error: win32Error,
            Detail: detail));
    }

    private bool CanExecutePowerAction(string clientId, string action)
    {
        var permissions = _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load());
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
}
