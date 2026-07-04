using System.Text.Json;

namespace VolturaAir.Host;

internal sealed record PairHelloRequest(
    string ClientId,
    string DeviceName,
    string? PairToken,
    string? Secret,
    string? Platform,
    string? Browser,
    string? DisplayMode);

internal static class ClientMessageValidator
{
    private const int MaxClientIdLength = 128;
    private const int MaxDeviceNameLength = 120;
    private const int MaxCredentialLength = 512;
    private const int MaxMetadataLength = 80;
    private const int MaxTextLength = 4096;
    private const int MaxKeyLength = 80;
    private const int MaxModifierLength = 40;
    private const int MaxModifierCount = 8;
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
        if (!TryReadType(root, out var type) || type != "pair.hello")
        {
            return false;
        }

        if (!TryGetRequiredString(root, "clientId", MaxClientIdLength, allowEmpty: false, out var clientId) ||
            !TryGetRequiredString(root, "deviceName", MaxDeviceNameLength, allowEmpty: true, out var deviceName) ||
            !TryGetOptionalString(root, "pairToken", MaxCredentialLength, out var pairToken) ||
            !TryGetOptionalString(root, "secret", MaxCredentialLength, out var secret) ||
            !TryGetOptionalString(root, "platform", MaxMetadataLength, out var platform) ||
            !TryGetOptionalString(root, "browser", MaxMetadataLength, out var browser) ||
            !TryGetOptionalString(root, "displayMode", MaxMetadataLength, out var displayMode))
        {
            return false;
        }

        request = new PairHelloRequest(clientId, deviceName, pairToken, secret, platform, browser, displayMode);
        return true;
    }

    public static bool IsValidAuthenticatedMessage(JsonElement root)
    {
        if (!TryReadType(root, out var type))
        {
            return false;
        }

        return type switch
        {
            "pair.disconnect" => true,
            "device.rename" => TryGetRequiredString(root, "deviceName", MaxDeviceNameLength, allowEmpty: true, out _),
            "status.ping" => true,
            "system.sleep" => true,
            "audio.mute.toggle" => true,
            "audio.volume.set" => TryGetNumber(root, "volume", 0, 100, out _),
            "pointer.move" => TryGetNumber(root, "dx", -MaxPointerDelta, MaxPointerDelta, out _) &&
                TryGetNumber(root, "dy", -MaxPointerDelta, MaxPointerDelta, out _),
            "pointer.button" => TryGetOneOf(root, "button", "left", "right") &&
                TryGetOneOf(root, "action", "down", "up", "click"),
            "pointer.wheel" => TryGetNumber(root, "dx", -MaxPointerDelta, MaxPointerDelta, out _) &&
                TryGetNumber(root, "dy", -MaxPointerDelta, MaxPointerDelta, out _),
            "pointer.zoom" => TryGetOneOf(root, "direction", "in", "out"),
            "keyboard.text" => TryGetRequiredString(root, "text", MaxTextLength, allowEmpty: true, out _),
            "keyboard.special" => TryGetRequiredString(root, "key", MaxKeyLength, allowEmpty: false, out _) &&
                TryGetOptionalStringArray(root, "modifiers", MaxModifierCount, MaxModifierLength),
            _ => false
        };
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
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is null || value.Length <= maxLength;
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

    private static bool TryGetOneOf(JsonElement root, string propertyName, params string[] allowedValues)
    {
        if (!TryGetRequiredString(root, propertyName, MaxMetadataLength, allowEmpty: false, out var value))
        {
            return false;
        }

        return allowedValues.Contains(value, StringComparer.Ordinal);
    }

    private static bool TryGetOptionalStringArray(JsonElement root, string propertyName, int maxItems, int maxItemLength)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var count = 0;
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

            count++;
            if (count > maxItems)
            {
                return false;
            }
        }

        return true;
    }
}
