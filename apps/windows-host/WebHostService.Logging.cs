using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private void LogReceivedClientCommand(
        string clientId,
        string type,
        JsonElement root,
        ValidatedInputCommand? inputCommand)
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
            "audio.mute.toggle" => "toggle_mute",
            "audio.volume.set" => "set_volume",
            "url.open" => "open_url",
            "pointer.button" => "pointer_button",
            "keyboard.text" => "text_input",
            _ => GetLoggedCommandAction(type, inputCommand)
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

    private static string? GetLoggedCommandAction(string type, ValidatedInputCommand? inputCommand)
    {
        if (type != "keyboard.special")
        {
            return null;
        }

        return inputCommand is { } command && HostUiInputGuard.IsMinimizeWindowShortcut(command)
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
