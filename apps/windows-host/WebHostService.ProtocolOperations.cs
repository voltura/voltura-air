using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private static bool IsInputMessage(string? type)
    {
        return type is "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" or "keyboard.text" or "keyboard.special";
    }

    private static bool IsAudioMessage(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        return typeProperty.GetString() is "audio.mute.toggle" or "audio.volume.set";
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private Task HandleAppLaunchAsync(WebSocket socket, string clientId, string actionId, CancellationToken cancellationToken)
    {
        var result = CanLaunchRemoteApps(clientId)
            ? _appLaunchService.Execute(actionId)
            : new AppLaunchExecutionResult(false, "permission-denied", "Application launch is disabled for this device on the PC.");

        _appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "app.launch",
            Action: actionId,
            Outcome: result.Succeeded ? "succeeded" : result.Code));

        return SendSocketAsync(socket, new
        {
            type = "app.launch.result",
            actionId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message
        }, cancellationToken);
    }

    private Task HandleUrlOpenAsync(WebSocket socket, string clientId, string operationId, string url, CancellationToken cancellationToken)
    {
        var result = CanOpenUrls(clientId)
            ? _urlOpenService.Execute(url)
            : new UrlOpenExecutionResult(false, "permission-denied", "Opening web addresses is disabled for this device on the PC.");

        _appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: "url.open",
            Action: "open_url",
            Outcome: result.Succeeded ? "accepted" : result.Code));

        return SendSocketAsync(socket, new
        {
            type = "url.open.result",
            operationId,
            succeeded = result.Succeeded,
            code = result.Code,
            message = result.Message,
            normalizedUrl = result.NormalizedUrl
        }, cancellationToken);
    }

    private async Task CloseAuthenticatedSocketAsync(WebSocket socket, string clientId, string reason, WebSocketCloseStatus status, CancellationToken cancellationToken)
    {
        ControllerSocketClosed?.Invoke(this, new ControllerSocketClosedEventArgs(clientId, reason, status));
        await CloseSocketAsync(socket, reason, status, cancellationToken);
    }

    private static int GetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static void TrySleepPc()
    {
        try
        {
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, force: false, disableWakeEvent: false);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
