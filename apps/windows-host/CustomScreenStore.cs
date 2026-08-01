using System.Security;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

public interface ICustomScreenStore
{
    CustomScreenStoreLoadResult Load();

    bool TrySave(IReadOnlyList<CustomScreenDefinition> screens, out string failureReason);
}

public sealed class CustomScreenStore : ICustomScreenStore
{
    private const int CurrentVersion = 3;
    private readonly string _filePath;

    public CustomScreenStore(string? rootFolder = null)
    {
        var folder = Path.Combine(
            rootFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Voltura Air");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "custom-screens.json");
    }

    public CustomScreenStoreLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new([], null);
        }

        try
        {
            if (new FileInfo(_filePath).Length > CustomScreenLimits.MaxStoreBytes)
            {
                return new([], "The Custom screens file is too large. It was left unchanged.");
            }

            var document = JsonSerializer.Deserialize<CustomScreenDocument>(
                File.ReadAllText(_filePath, Encoding.UTF8),
                JsonOptions.Default);
            if (document is null || document.Version != CurrentVersion)
            {
                // Alpha-only development policy. Custom screens must gain an
                // explicit migration or informed recovery flow before the
                // feature can graduate or ship as a stable user-data format.
                File.Delete(_filePath);
                return new([], null);
            }

            if (!CustomScreenValidator.TryValidateCollection(document.Screens, out var error))
            {
                return new([], $"The Custom screens file is invalid: {error} It was left unchanged.");
            }

            return new(document.Screens, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return new([], $"Custom screens could not be loaded: {ex.Message} The file was left unchanged.");
        }
    }

    public bool TrySave(IReadOnlyList<CustomScreenDefinition> screens, out string failureReason)
    {
        if (!CustomScreenValidator.TryValidateCollection(screens, out failureReason))
        {
            return false;
        }

        var json = JsonSerializer.Serialize(new CustomScreenDocument(CurrentVersion, screens), JsonOptions.Default);
        if (Encoding.UTF8.GetByteCount(json) > CustomScreenLimits.MaxStoreBytes)
        {
            failureReason = "The Custom screens library is too large to save.";
            return false;
        }

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _filePath, overwrite: true);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            failureReason = $"Custom screens could not be saved: {ex.Message}";
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }
}

public sealed class InMemoryCustomScreenStore : ICustomScreenStore
{
    private IReadOnlyList<CustomScreenDefinition> _screens = [];

    public CustomScreenStoreLoadResult Load() => new(_screens, null);

    public bool TrySave(IReadOnlyList<CustomScreenDefinition> screens, out string failureReason)
    {
        if (!CustomScreenValidator.TryValidateCollection(screens, out failureReason))
        {
            return false;
        }

        _screens = [.. screens];
        failureReason = string.Empty;
        return true;
    }
}

internal static class CustomScreenValidator
{
    private static readonly HashSet<int> Widths = [3, 4, 6, 8, 9, 12];
    private static readonly HashSet<int> VolumeWidths = [3, 6, 9, 12];
    private static readonly HashSet<int> NavigationRingWidths = [6, 8, 9, 12];
    private static readonly HashSet<string> Heights = ["content", "fill"];
    private static readonly HashSet<string> SectionKinds =
        ["buttons", "collapsible", "trackpad", "collapsibleTrackpad", "volume", "navigationRing"];
    private static readonly HashSet<string> TrackpadButtonSides = ["left", "right"];
    private static readonly HashSet<string> ButtonAlignments =
        ["start", "center", "end", "space-between", "space-around", "space-evenly"];
    private static readonly HashSet<string> Presentations = ["iconLabel", "icon", "label"];
    private static readonly HashSet<string> Sizes = ["compact", "standard", "wide", "fill"];
    private static readonly HashSet<string> Icons =
    [
        "play", "pause", "skip-back", "skip-forward", "volume-1", "volume-2",
        "volume-x", "arrow-up", "arrow-down", "arrow-left", "arrow-right",
        "corner-down-left", "escape", "keyboard", "clipboard", "copy", "app-window",
        "monitor", "minimize", "square-x", "search", "refresh", "maximize", "command"
    ];
    private static readonly HashSet<string> ActionKinds = ["text", "shortcut", "appLaunch", "builtIn"];
    private static readonly HashSet<string> Modifiers = ["Control", "Shift", "Alt", "AltGr", "Win"];

    public static bool TryValidateCollection(
        IReadOnlyList<CustomScreenDefinition>? screens,
        out string error)
    {
        if (screens is null || screens.Count > CustomScreenLimits.MaxScreens)
        {
            error = $"At most {CustomScreenLimits.MaxScreens} screens can be stored.";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var screen in screens)
        {
            if (!ids.Add(screen.Id))
            {
                error = "Screen IDs must be unique.";
                return false;
            }

            if (!TryValidate(screen, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidate(
        CustomScreenDefinition? screen,
        out string error)
    {
        if (screen is null ||
            !ValidId(screen.Id) ||
            !ValidText(screen.Name, CustomScreenLimits.MaxScreenNameLength) ||
            !ValidId(screen.Revision) ||
            screen.Sections is null ||
            screen.Sections.Count > CustomScreenLimits.MaxSectionsPerScreen ||
            screen.AssignedClientIds is null ||
            screen.AssignedClientIds.Any(id => !ValidId(id)))
        {
            error = "A screen contains invalid identity, name, assignment, or section data.";
            return false;
        }

        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        var buttonIds = new HashSet<string>(StringComparer.Ordinal);
        var buttonCount = 0;
        foreach (var section in screen.Sections)
        {
            if (!sectionIds.Add(section.Id) ||
                !ValidId(section.Id) ||
                !ValidText(section.Name, CustomScreenLimits.MaxSectionNameLength) ||
                !Widths.Contains(section.WidthColumns) ||
                (CustomScreenSectionKinds.IsVolume(section.Kind) &&
                    (!VolumeWidths.Contains(section.WidthColumns) ||
                        !ValidVolumeOverride(section.Portrait) ||
                        !ValidVolumeOverride(section.Landscape))) ||
                (CustomScreenSectionKinds.IsNavigationRing(section.Kind) &&
                    (!NavigationRingWidths.Contains(section.WidthColumns) ||
                        !ValidNavigationRingOverride(section.Portrait) ||
                        !ValidNavigationRingOverride(section.Landscape))) ||
                !Heights.Contains(section.HeightMode) ||
                section.FillWeight is < 1 or > 4 ||
                section.RowLimit is < 0 or > 3 ||
                !SectionKinds.Contains(section.Kind) ||
                !TrackpadButtonSides.Contains(section.TrackpadButtonSide) ||
                !ButtonAlignments.Contains(section.ButtonAlignment) ||
                !ValidOverride(section.Portrait, section: true) ||
                !ValidOverride(section.Landscape, section: true) ||
                section.Buttons is null ||
                (!CustomScreenSectionKinds.AllowsButtons(section.Kind) &&
                    section.Buttons.Count != 0) ||
                (CustomScreenSectionKinds.IsCollapsible(section.Kind) && !section.ShowHeader))
            {
                error = "A section contains invalid layout or identity data.";
                return false;
            }

            buttonCount += section.Buttons.Count;
            if (buttonCount > CustomScreenLimits.MaxButtonsPerScreen)
            {
                error = $"A screen can contain at most {CustomScreenLimits.MaxButtonsPerScreen} buttons.";
                return false;
            }

            foreach (var button in section.Buttons)
            {
                if (button.Row is < 0 or > 3 || button.Row > section.RowLimit)
                {
                    error = $"Button \"{button.Name}\" row must be Auto or within the section's row limit.";
                    return false;
                }

                if (!buttonIds.Add(button.Id) ||
                    !TryValidateButton(
                        button,
                        CustomScreenLimits.MaxButtonNameLength,
                        CustomScreenLimits.MaxButtonLabelLength))
                {
                    error = "A button contains invalid visual, layout, or action data.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidVolumeOverride(
        CustomScreenLayoutOverride? layout) =>
        layout?.WidthColumns is null ||
        VolumeWidths.Contains(layout.WidthColumns.Value);

    private static bool ValidNavigationRingOverride(
        CustomScreenLayoutOverride? layout) =>
        layout?.WidthColumns is null ||
        NavigationRingWidths.Contains(layout.WidthColumns.Value);

    private static bool TryValidateButton(
        CustomScreenButton button,
        int nameLimit,
        int labelLimit) =>
        ValidId(button.Id) &&
        ValidText(button.Name, nameLimit) &&
        button.Label is not null &&
        button.Label.Length <= labelLimit &&
        Icons.Contains(button.Icon) &&
        Presentations.Contains(button.Presentation) &&
        Sizes.Contains(button.Size) &&
        button.Row is >= 0 and <= 3 &&
        ValidOverride(button.Portrait, section: false) &&
        ValidOverride(button.Landscape, section: false) &&
        (!CustomScreenService.RequiresLabelOnlyPresentation(button.Action) ||
         button.Presentation == "label") &&
        TryValidateAction(button.Action);

    private static bool TryValidateAction(CustomScreenAction? action)
    {
        if (action is null || !ActionKinds.Contains(action.Kind))
        {
            return false;
        }

        return action.Kind switch
        {
            "text" => ValidText(action.Text, CustomScreenLimits.MaxTextLength),
            "shortcut" => CustomScreenShortcutKeys.TryNormalize(action.Key, out _) &&
                (action.Modifiers?.Count ?? 0) <= 5 &&
                (action.Modifiers ?? []).Distinct(StringComparer.Ordinal).Count() ==
                    (action.Modifiers?.Count ?? 0) &&
                (action.Modifiers ?? []).All(Modifiers.Contains),
            "appLaunch" => ValidId(action.ActionId),
            "builtIn" => CustomScreenBuiltIns.IsSupported(action.BuiltIn),
            _ => false
        };
    }

    private static bool ValidOverride(CustomScreenLayoutOverride? value, bool section) =>
        value is null ||
        (value.Order >= 0 &&
         (value.WidthColumns is null || section && Widths.Contains(value.WidthColumns.Value)) &&
         (value.Size is null || !section && Sizes.Contains(value.Size)) &&
         (value.Row is null || !section && value.Row is >= 0 and <= 3));

    private static bool ValidId(string? value) =>
        value is { Length: > 0 and <= CustomScreenLimits.MaxIdLength } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool ValidText(string? value, int maxLength) =>
        value is { Length: > 0 } &&
        value.Length <= maxLength &&
        value.Any(character => !char.IsWhiteSpace(character)) &&
        !value.Any(char.IsControl);
}
