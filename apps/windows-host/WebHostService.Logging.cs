using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private void LogReceivedClientCommand(string clientId, string type, JsonElement root)
    {
        if (type is "health.ping" or "status.get" or "pointer.move" or "pointer.wheel" or "pointer.zoom")
        {
            return;
        }

        var action = type is "system.power" or "remote.launch"
            ? GetString(root, "action")
            : null;
        _appLog.Write(new AppLogEntry(
            Event: "command_received",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: type,
            Action: action));
    }
}
