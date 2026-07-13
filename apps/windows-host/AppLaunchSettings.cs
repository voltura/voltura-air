using System.Text.Json;
using Microsoft.Win32;

namespace VolturaAir.Host;

public enum AppLaunchKind
{
    Browser,
    Spotify,
    Vlc,
    PowerPoint,
    Custom
}

public sealed record AppLaunchAction(
    string Id,
    string Label,
    AppLaunchKind Kind,
    string? ExecutablePath = null,
    string? Arguments = null);

public sealed record AppLaunchActionSummary(string Id, string Label, string Kind);

public static class AppLaunchSettings
{
    public const int MaxActions = 16;
    public const int MaxIdLength = 64;
    public const int MaxLabelLength = 10;
    public const int MaxPathLength = 1024;
    public const int MaxArgumentsLength = 2048;

    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string ActionsValueName = "AppLaunchActions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AppLaunchAction[] Presets =
    [
        new("preset.browser", "WWW", AppLaunchKind.Browser),
        new("preset.spotify", "Spotify", AppLaunchKind.Spotify),
        new("preset.vlc", "VLC", AppLaunchKind.Vlc),
        new("preset.powerpoint", "PPT", AppLaunchKind.PowerPoint)
    ];

    public static event EventHandler? Changed;

    public static IReadOnlyList<AppLaunchAction> GetActions()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return Parse(key?.GetValue(ActionsValueName) as string);
    }

    public static IReadOnlyList<AppLaunchAction> GetPresets() => Presets;

    public static string GetPresetName(AppLaunchKind kind) => kind switch
    {
        AppLaunchKind.Browser => "Browser",
        AppLaunchKind.Spotify => "Spotify",
        AppLaunchKind.Vlc => "VLC",
        AppLaunchKind.PowerPoint => "PowerPoint",
        _ => "Application"
    };

    public static AppLaunchAction? Find(string id)
    {
        return GetActions().FirstOrDefault(action => string.Equals(action.Id, id, StringComparison.Ordinal));
    }

    public static bool SetPresetEnabled(AppLaunchKind kind, bool enabled, out string error)
    {
        error = string.Empty;
        var preset = Presets.FirstOrDefault(action => action.Kind == kind);
        if (preset is null)
        {
            error = "Unsupported application preset.";
            return false;
        }

        var actions = GetActions().ToList();
        var existingIndex = actions.FindIndex(action => action.Id == preset.Id);
        if (enabled && existingIndex < 0)
        {
            if (actions.Count >= MaxActions)
            {
                error = $"At most {MaxActions} launch buttons can be configured.";
                return false;
            }

            actions.Add(preset);
        }
        else if (!enabled && existingIndex >= 0)
        {
            actions.RemoveAt(existingIndex);
        }
        else
        {
            return true;
        }

        Save(actions);
        return true;
    }

    public static bool TrySetPresetLabel(AppLaunchKind kind, string? label, out string error)
    {
        if (!TryNormalizeLabel(label, out var normalizedLabel, out error))
        {
            return false;
        }

        var preset = Presets.FirstOrDefault(action => action.Kind == kind);
        var actions = GetActions().ToList();
        var existingIndex = preset is null ? -1 : actions.FindIndex(action => action.Id == preset.Id && action.Kind == kind);
        if (preset is null || existingIndex < 0)
        {
            error = "Enable this application preset before changing its label.";
            return false;
        }

        if (string.Equals(actions[existingIndex].Label, normalizedLabel, StringComparison.Ordinal))
        {
            return true;
        }

        actions[existingIndex] = actions[existingIndex] with { Label = normalizedLabel };
        Save(actions);
        return true;
    }

    public static bool TrySaveCustom(
        string label,
        string executablePath,
        string? arguments,
        string? existingId,
        out AppLaunchAction action,
        out string error)
    {
        action = new AppLaunchAction(string.Empty, string.Empty, AppLaunchKind.Custom);
        if (!TryNormalizeCustom(label, executablePath, arguments, out var normalizedLabel, out var normalizedPath, out var normalizedArguments, out error))
        {
            return false;
        }

        var actions = GetActions().ToList();
        var id = IsCustomId(existingId) ? existingId! : $"custom.{Guid.NewGuid():N}";
        var existingIndex = actions.FindIndex(candidate => candidate.Id == id && candidate.Kind == AppLaunchKind.Custom);
        if (existingIndex < 0 && actions.Count >= MaxActions)
        {
            error = $"At most {MaxActions} launch buttons can be configured.";
            return false;
        }

        action = new AppLaunchAction(id, normalizedLabel, AppLaunchKind.Custom, normalizedPath, normalizedArguments);
        if (existingIndex >= 0)
        {
            actions[existingIndex] = action;
        }
        else
        {
            actions.Add(action);
        }

        Save(actions);
        return true;
    }

    public static bool RemoveCustom(string id)
    {
        if (!IsCustomId(id))
        {
            return false;
        }

        var actions = GetActions().ToList();
        var removed = actions.RemoveAll(action => action.Id == id && action.Kind == AppLaunchKind.Custom) > 0;
        if (removed)
        {
            Save(actions);
        }

        return removed;
    }

    public static bool TryValidateCustom(AppLaunchAction action, out string error)
    {
        if (action.Kind != AppLaunchKind.Custom || !IsCustomId(action.Id))
        {
            error = "The custom launch action has an invalid ID.";
            return false;
        }

        return TryNormalizeCustom(action.Label, action.ExecutablePath, action.Arguments, out _, out _, out _, out error);
    }

    public static bool TryNormalizeCustom(
        string? label,
        string? executablePath,
        string? arguments,
        out string normalizedLabel,
        out string normalizedPath,
        out string normalizedArguments,
        out string error)
    {
        normalizedLabel = label?.Trim() ?? string.Empty;
        normalizedPath = executablePath?.Trim().Trim('"') ?? string.Empty;
        normalizedArguments = arguments?.Trim() ?? string.Empty;
        error = string.Empty;

        if (!TryNormalizeLabel(normalizedLabel, out var validatedLabel, out error))
        {
            return false;
        }
        normalizedLabel = validatedLabel;

        if (normalizedPath.Length is < 1 or > MaxPathLength || ContainsControlCharacter(normalizedPath))
        {
            error = "Enter a valid executable path.";
            return false;
        }

        if (!Path.IsPathFullyQualified(normalizedPath))
        {
            error = "The executable path must be absolute.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = "Custom launch targets must be .exe files.";
            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            error = "The executable file does not exist.";
            return false;
        }

        if (normalizedArguments.Length > MaxArgumentsLength || ContainsControlCharacter(normalizedArguments))
        {
            error = $"Arguments must be one line and no longer than {MaxArgumentsLength} characters.";
            return false;
        }

        return true;
    }

    internal static IReadOnlyList<AppLaunchAction> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<AppLaunchAction>>(json, JsonOptions) ?? [];
            var normalized = new List<AppLaunchAction>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var action in stored)
            {
                if (normalized.Count >= MaxActions || !seenIds.Add(action.Id))
                {
                    continue;
                }

                var preset = Presets.FirstOrDefault(candidate => candidate.Id == action.Id && candidate.Kind == action.Kind);
                if (preset is not null && TryNormalizeLabel(action.Label, out var presetLabel, out _))
                {
                    normalized.Add(preset with { Label = presetLabel });
                    continue;
                }

                if (action.Kind == AppLaunchKind.Custom &&
                    TryValidateCustom(action, out _) &&
                    action.Label.Length <= MaxLabelLength)
                {
                    normalized.Add(action with
                    {
                        Label = action.Label.Trim(),
                        ExecutablePath = action.ExecutablePath!.Trim().Trim('"'),
                        Arguments = action.Arguments?.Trim() ?? string.Empty
                    });
                }
            }

            return normalized;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string Serialize(IReadOnlyList<AppLaunchAction> actions)
    {
        return JsonSerializer.Serialize(actions, JsonOptions);
    }

    internal static bool TryNormalizeLabel(string? label, out string normalizedLabel, out string error)
    {
        normalizedLabel = label?.Trim() ?? string.Empty;
        error = string.Empty;
        if (normalizedLabel.Length is < 1 or > MaxLabelLength || ContainsControlCharacter(normalizedLabel))
        {
            error = $"Enter a label between 1 and {MaxLabelLength} characters.";
            return false;
        }

        return true;
    }

    private static void Save(IReadOnlyList<AppLaunchAction> actions)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ActionsValueName, Serialize(actions), RegistryValueKind.String);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsCustomId(string? id)
    {
        return id is { Length: > 7 and <= MaxIdLength } &&
            id.StartsWith("custom.", StringComparison.Ordinal) &&
            id[7..].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static bool ContainsControlCharacter(string value) => value.Any(char.IsControl);
}
