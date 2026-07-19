using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class HostCommandLog(IAppLogWriter appLog)
{
    public void Received(string clientId, string type, JsonElement root, ValidatedInputCommand? inputCommand)
    {
        if (!ShouldRecord(type))
        {
            return;
        }

        var action = type switch
        {
            "system.power" or "remote.launch" => ProtocolMessageFields.GetString(root, "action"),
            "app.launch" => ProtocolMessageFields.GetString(root, "actionId"),
            "awake.set" => root.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean() ? "enable" : "disable",
            "audio.mute.toggle" => "toggle_mute",
            "audio.volume.set" => "set_volume",
            "url.open" => "open_url",
            "pointer.button" => "pointer_button",
            "keyboard.text" => "text_input",
            _ => GetAction(type, inputCommand)
        };
        appLog.Write(new AppLogEntry(
            Event: "command_received",
            Source: "remote_client",
            ClientId: clientId,
            MessageType: type,
            Action: action));
    }

    public void Outcome(string clientId, string type, string? action, string outcome)
    {
        appLog.Write(new AppLogEntry(
            Event: "command_outcome",
            Source: "windows_host",
            ClientId: clientId,
            MessageType: type,
            Action: action,
            Outcome: outcome));
    }

    public static bool ShouldRecord(string type) =>
        type is not ("health.ping" or "status.get" or "pointer.move" or "pointer.wheel" or "pointer.zoom");

    public static string? GetAction(string type, ValidatedInputCommand? inputCommand)
    {
        if (type != "keyboard.special")
        {
            return null;
        }

        return inputCommand is { } command && HostUiInputGuard.IsMinimizeWindowShortcut(command)
            ? "minimize_window"
            : "special_key";
    }
}

internal static class ProtocolMessageFields
{
    public static string GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    public static int GetInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : 0;
}
