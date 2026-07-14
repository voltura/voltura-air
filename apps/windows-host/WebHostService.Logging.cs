using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private void LogReceivedClientCommand(string clientId, string type, JsonElement root)
    {
        if (!ShouldLogClientCommand(type))
        {
            return;
        }

        var action = type switch
        {
            "system.power" or "remote.launch" => GetString(root, "action"),
            "app.launch" => GetString(root, "actionId"),
            "awake.set" => root.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean() ? "enable" : "disable",
            "pointer.highlight.set" => root.TryGetProperty("enabled", out var highlightEnabled) && highlightEnabled.GetBoolean() ? "enable" : "disable",
            "audio.mute.toggle" => "toggle_mute",
            "audio.volume.set" => "set_volume",
            "pointer.button" => "pointer_button",
            "keyboard.text" => "text_input",
            _ => GetLoggedCommandAction(type, root)
        };
        _appLog.Write(new AppLogEntry(
            Event: "command_received",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: type,
            Action: action));
    }

    private static bool ShouldLogClientCommand(string type)
    {
        return type is not ("health.ping" or "status.get" or "pointer.move" or "pointer.wheel" or "pointer.zoom");
    }

    private static string? GetLoggedCommandAction(string type, JsonElement root)
    {
        if (type != "keyboard.special")
        {
            return null;
        }

        return HostUiInputGuard.IsMinimizeWindowShortcut(root)
            ? "minimize_window"
            : "special_key";
    }

    private void LogCommandOutcome(string clientId, string type, string? action, string outcome)
    {
        _appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: type,
            Action: action,
            Outcome: outcome));
    }
}
