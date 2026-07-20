using System.Text.Json;
using System.Collections.Frozen;

namespace VolturaAir.Host;

internal sealed record PairHelloRequest(
    string ClientId,
    string DeviceName,
    string? PairToken,
    string? ReconnectPublicKey,
    string? Platform,
    string? Browser,
    string? DisplayMode);

internal sealed record PairProofRequest(
    string ClientId,
    string Signature);

internal static class ClientMessageValidator
{
    private static readonly FrozenSet<string> PairHelloProperties = new[]
    {
        "type", "clientId", "deviceName", "pairToken", "reconnectPublicKey", "platform", "browser", "displayMode"
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> PairProofProperties = new[]
    {
        "type", "clientId", "signature"
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenDictionary<string, FrozenSet<string>> AuthenticatedMessageProperties =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            ["pair.disconnect"] = Fields("type"),
            ["device.rename"] = Fields("type", "deviceName"),
            ["health.ping"] = Fields("type"),
            ["status.get"] = Fields("type"),
            ["pointer.speed.set"] = Fields("type", "pointerSpeed"),
            ["appearance.mode-buttons.set"] = Fields("type", "showModeButtons"),
            ["custom.pointer.set"] = Fields("type", "enabled"),
            ["audio.get"] = Fields("type"),
            ["system.sleep"] = Fields("type"),
            ["system.power"] = Fields("type", "operationId", "action"),
            ["awake.set"] = Fields("type", "operationId", "enabled"),
            ["presentation.command"] = Fields("type", "operationId", "target", "action"),
            ["remote.launch"] = Fields("type", "action"),
            ["app.launch"] = Fields("type", "operationId", "actionId"),
            ["url.open"] = Fields("type", "operationId", "url"),
            ["text.send"] = Fields("type", "operationId", "text", "sendEnter"),
            ["clipboard.get"] = Fields("type", "operationId"),
            ["audio.mute.toggle"] = Fields("type"),
            ["audio.volume.set"] = Fields("type", "volume"),
            ["pointer.move"] = Fields("type", "seq", "dx", "dy"),
            ["pointer.button"] = Fields("type", "seq", "button", "action"),
            ["pointer.wheel"] = Fields("type", "seq", "dx", "dy"),
            ["pointer.zoom"] = Fields("type", "seq", "direction"),
            ["keyboard.text"] = Fields("type", "seq", "text"),
            ["keyboard.special"] = Fields("type", "seq", "key", "modifiers")
        }.ToFrozenDictionary(StringComparer.Ordinal);
    private const int MaxClientIdLength = 128;
    private const int MaxDeviceNameLength = 120;
    private const int MaxCredentialLength = 512;
    private const int MaxMetadataLength = 80;
    private const int MaxKeyLength = 80;
    private const int MaxModifierLength = 40;
    private const int MaxModifierCount = 8;
    private const int MaxRemoteActionLength = 80;
    private const int MaxOperationIdLength = 64;
    private const double MaxPointerDelta = 5000;

    public static bool TryReadType(JsonElement root, out string? type)
    {
        type = null;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeProperty) ||
            typeProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        type = typeProperty.GetString();
        return !string.IsNullOrEmpty(type);
    }

    public static bool TryValidatePairHello(JsonElement root, out PairHelloRequest request)
    {
        request = new PairHelloRequest(string.Empty, string.Empty, null, null, null, null, null);
        if (!TryReadType(root, out var type) ||
            type != "pair.hello" ||
            !HasOnlyUniqueProperties(root, PairHelloProperties))
        {
            return false;
        }

        if (!TryGetRequiredString(root, "clientId", MaxClientIdLength, allowEmpty: false, out var clientId) ||
            !TryGetRequiredString(root, "deviceName", MaxDeviceNameLength, allowEmpty: false, out var deviceName) ||
            string.IsNullOrWhiteSpace(deviceName) ||
            !TryGetOptionalString(root, "pairToken", MaxCredentialLength, out var pairToken) ||
            !TryGetOptionalString(root, "reconnectPublicKey", MaxCredentialLength, out var reconnectPublicKey) ||
            !TryGetOptionalString(root, "platform", MaxMetadataLength, out var platform) ||
            !TryGetOptionalString(root, "browser", MaxMetadataLength, out var browser) ||
            !TryGetOptionalString(root, "displayMode", MaxMetadataLength, out var displayMode))
        {
            return false;
        }

        request = new PairHelloRequest(clientId, deviceName, pairToken, reconnectPublicKey, platform, browser, displayMode);
        return true;
    }

    public static bool TryValidatePairProof(JsonElement root, out PairProofRequest request)
    {
        request = new PairProofRequest(string.Empty, string.Empty);
        if (!TryReadType(root, out var type) ||
            type != "pair.proof" ||
            !HasOnlyUniqueProperties(root, PairProofProperties))
        {
            return false;
        }

        if (!TryGetRequiredString(root, "clientId", MaxClientIdLength, allowEmpty: false, out var clientId) ||
            !TryGetRequiredString(root, "signature", MaxCredentialLength, allowEmpty: false, out var signature))
        {
            return false;
        }

        request = new PairProofRequest(clientId, signature);
        return true;
    }

    public static bool IsValidAuthenticatedMessage(JsonElement root, string type)
    {
        if (!AuthenticatedMessageProperties.TryGetValue(type, out var allowedProperties) ||
            !HasOnlyUniqueProperties(root, allowedProperties))
        {
            return false;
        }

        return type switch
        {
            "pair.disconnect" => true,
            "device.rename" => TryGetRequiredString(root, "deviceName", MaxDeviceNameLength, allowEmpty: false, out var deviceName) &&
                !string.IsNullOrWhiteSpace(deviceName),
            "health.ping" => true,
            "status.get" => true,
            "pointer.speed.set" => TryGetNumber(root, "pointerSpeed", DevicePointerProfile.MinPointerSpeed, DevicePointerProfile.MaxPointerSpeed, out _),
            "appearance.mode-buttons.set" => root.TryGetProperty("showModeButtons", out var showModeButtons) && showModeButtons.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "custom.pointer.set" => root.TryGetProperty("enabled", out var customPointerEnabled) && customPointerEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "audio.get" => true,
            "system.sleep" => true,
            "system.power" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var powerOperationId) &&
                IsValidOperationId(powerOperationId) &&
                TryGetRequiredString(root, "action", MaxRemoteActionLength, allowEmpty: false, out _),
            "awake.set" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var awakeOperationId) &&
                IsValidOperationId(awakeOperationId) &&
                root.TryGetProperty("enabled", out var enabled) &&
                enabled.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "presentation.command" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var presentationOperationId) &&
                IsValidOperationId(presentationOperationId) &&
                TryGetRequiredString(root, "target", MaxMetadataLength, allowEmpty: false, out var presentationTarget) &&
                PresentationCommands.IsTarget(presentationTarget) &&
                TryGetRequiredString(root, "action", MaxRemoteActionLength, allowEmpty: false, out var presentationAction) &&
                PresentationCommands.IsAction(presentationAction),
            "remote.launch" => TryGetRequiredString(root, "action", MaxRemoteActionLength, allowEmpty: false, out var action) &&
                RemoteLaunchActions.IsSupported(action),
            "app.launch" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var appLaunchOperationId) &&
                IsValidOperationId(appLaunchOperationId) &&
                TryGetRequiredString(root, "actionId", AppLaunchSettings.MaxIdLength, allowEmpty: false, out var actionId) &&
                IsValidAppLaunchActionId(actionId),
            "url.open" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var urlOperationId) &&
                IsValidOperationId(urlOperationId) &&
                TryGetRequiredString(root, "url", UrlOpenLimits.MaxUrlLength, allowEmpty: false, out _),
            "text.send" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var operationId) &&
                IsValidOperationId(operationId) &&
                TryGetRequiredString(root, "text", TextTransferLimits.MaxTextLength, allowEmpty: false, out _) &&
                root.TryGetProperty("sendEnter", out var sendEnter) && sendEnter.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "clipboard.get" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var clipboardOperationId) &&
                IsValidOperationId(clipboardOperationId),
            "audio.mute.toggle" => true,
            "audio.volume.set" => TryGetNumber(root, "volume", 0, 100, out _),
            "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" or "keyboard.text" or "keyboard.special" =>
                TryDecodeInputMessage(root, type, out _),
            _ => false
        };
    }

    public static bool TryDecodeInputMessage(JsonElement root, string type, out ValidatedInputCommand command)
    {
        command = default;
        if (!TryGetOptionalSequence(root, out var sequence))
        {
            return false;
        }

        switch (type)
        {
            case "pointer.move":
                if (!TryGetPointerDelta(root, "dx", out var moveDx) ||
                    !TryGetPointerDelta(root, "dy", out var moveDy))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerMove, sequence, Dx: moveDx, Dy: moveDy);
                return true;
            case "pointer.button":
                if (!TryGetRequiredString(root, "button", MaxMetadataLength, allowEmpty: false, out var button) ||
                    button is not ("left" or "right") ||
                    !TryGetRequiredString(root, "action", MaxMetadataLength, allowEmpty: false, out var buttonAction) ||
                    buttonAction is not ("down" or "up" or "click"))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerButton, sequence, Button: button, Action: buttonAction);
                return true;
            case "pointer.wheel":
                if (!TryGetPointerDelta(root, "dx", out var wheelDx) ||
                    !TryGetPointerDelta(root, "dy", out var wheelDy))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerWheel, sequence, Dx: wheelDx, Dy: wheelDy);
                return true;
            case "pointer.zoom":
                if (!TryGetRequiredString(root, "direction", MaxMetadataLength, allowEmpty: false, out var direction) ||
                    direction is not ("in" or "out"))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerZoom, sequence, Action: direction);
                return true;
            case "keyboard.text":
                if (!TryGetRequiredString(root, "text", TextTransferLimits.MaxTextLength, allowEmpty: false, out var text))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.KeyboardText, sequence, Text: text);
                return true;
            case "keyboard.special":
                if (!TryGetRequiredString(root, "key", MaxKeyLength, allowEmpty: false, out var key) ||
                    !TryGetOptionalStringArray(root, "modifiers", MaxModifierCount, MaxModifierLength, out var modifiers))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.KeyboardSpecial, sequence, Key: key, ModifierValues: modifiers);
                return true;
            default:
                return false;
        }
    }

    private static bool IsValidAppLaunchActionId(string actionId)
    {
        return actionId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsValidOperationId(string operationId)
    {
        return operationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');
    }

    private static FrozenSet<string> Fields(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

    private static bool HasOnlyUniqueProperties(JsonElement root, FrozenSet<string> allowedProperties)
    {
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name) || !seenProperties.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, int maxLength, bool allowEmpty, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length <= maxLength && (allowEmpty || value.Length > 0);
    }

    private static bool TryGetOptionalString(JsonElement root, string propertyName, int maxLength, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;
    }

    private static bool TryGetNumber(JsonElement root, string propertyName, double min, double max, out double value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value) &&
            value >= min &&
            value <= max;
    }

    private static bool TryGetOptionalSequence(JsonElement root, out long? sequence)
    {
        sequence = null;
        if (!root.TryGetProperty("seq", out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value) || value <= 0)
        {
            return false;
        }

        sequence = value;
        return true;
    }

    private static bool TryGetOptionalStringArray(
        JsonElement root,
        string propertyName,
        int maxItems,
        int maxItemLength,
        out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = new List<string>(Math.Min(maxItems, property.GetArrayLength()));
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = item.GetString();
            if (string.IsNullOrEmpty(value) || value.Length > maxItemLength)
            {
                return false;
            }

            items.Add(value);
            if (items.Count > maxItems)
            {
                return false;
            }
        }

        values = [.. items];
        return values.Length > 0;
    }

    private static bool TryGetPointerDelta(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetNumber(root, propertyName, -MaxPointerDelta, MaxPointerDelta, out var number))
        {
            return false;
        }

        value = (int)Math.Clamp(Math.Round(number), -MaxPointerDelta, MaxPointerDelta);
        return true;
    }
}
