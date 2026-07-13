using System.Net.WebSockets;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private object CreateAwakeCapability(string clientId)
    {
        var state = _awakeService.State;
        return new
        {
            canControl = CanControlAwake(clientId),
            active = state.IsActive,
            mode = ToProtocolAwakeMode(state.Mode),
            expiresAt = state.ExpiresAt?.ToUniversalTime().ToString("O")
        };
    }

    private async Task HandleAwakeSetAsync(WebSocket socket, string clientId, bool enabled, CancellationToken cancellationToken)
    {
        if (!CanControlAwake(clientId))
        {
            await SendAwakeResultAsync(socket, enabled, false, "VAIR-AWAKE-DENIED", "Keep awake control is disabled by the PC host.", cancellationToken);
            LogAwakeAction(clientId, enabled, "permission_denied");
            return;
        }

        var result = enabled ? _awakeService.SetIndefinite() : _awakeService.SetOff();
        LogAwakeAction(clientId, enabled, result.Succeeded ? "succeeded" : "execution_failed", result.Error);
        await SendAwakeResultAsync(
            socket,
            enabled,
            result.Succeeded,
            result.Succeeded ? null : "VAIR-AWAKE-EXECUTION-FAILED",
            result.Succeeded
                ? enabled ? "The PC will stay awake using the host screen setting." : "The selected Windows power plan is active."
                : result.Error ?? "Windows rejected the keep-awake request.",
            cancellationToken);
    }

    private Task SendAwakeResultAsync(
        WebSocket socket,
        bool enabled,
        bool succeeded,
        string? code,
        string message,
        CancellationToken cancellationToken)
    {
        _appLog.Write(new AppLogEntry(
            Event: "response_sent",
            Source: "remote_client",
            MessageType: "awake.result",
            Action: enabled ? "enable" : "disable",
            Outcome: succeeded ? "succeeded" : "failed",
            Code: code));
        return SendSocketAsync(socket, new { type = "awake.result", enabled, succeeded, code, message }, cancellationToken);
    }

    private bool CanControlAwake(string clientId) =>
        _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).AllowAwakeControl;

    private void OnAwakeStateChanged(object? sender, EventArgs e)
    {
        _ = Task.Run(BroadcastStatusAsync);
    }

    private void LogAwakeAction(string clientId, bool enabled, string outcome, string? detail = null)
    {
        _appLog.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: "awake.set",
            Action: enabled ? "enable" : "disable",
            Outcome: outcome,
            Detail: detail));
    }

    private static string ToProtocolAwakeMode(AwakeMode mode) => mode switch
    {
        AwakeMode.Indefinite => "indefinite",
        AwakeMode.Timed => "timed",
        AwakeMode.Expiration => "expiration",
        _ => "off"
    };
}
