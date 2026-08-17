using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal static class HostSettingsJsonValue
{
    internal const int MaximumCharacters = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static Action<string, string>? BeforeWriteForTests { get; set; }

    public static T Load<T>(string valueName, T missingValue, T malformedValue, Func<T, bool>? validate = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
            object? raw = key?.GetValue(valueName);
            if (raw is null) return missingValue;
            if (raw is not string json || json.Length is 0 or > MaximumCharacters || !HasExactShape(json, missingValue))
                return malformedValue;
            T? value = JsonSerializer.Deserialize<T>(json, Options);
            return value is not null && (validate?.Invoke(value) ?? true) ? value : malformedValue;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return malformedValue;
        }
    }

    public static void Save<T>(string valueName, T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        if (json.Length > MaximumCharacters) throw new InvalidOperationException($"The {valueName} settings value is too large.");
        BeforeWriteForTests?.Invoke(valueName, json);
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue(valueName, json, RegistryValueKind.String);
    }

    private static bool HasExactShape<T>(string json, T example)
    {
        using JsonDocument actual = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        using JsonDocument expected = JsonDocument.Parse(JsonSerializer.Serialize(example, Options));
        if (actual.RootElement.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(expected.RootElement.EnumerateObject().Select(property => property.Name), StringComparer.Ordinal);
        int count = 0;
        foreach (JsonProperty property in actual.RootElement.EnumerateObject())
        {
            count += 1;
            if (!names.Remove(property.Name)) return false;
        }
        return count > 0 && names.Count == 0;
    }
}
