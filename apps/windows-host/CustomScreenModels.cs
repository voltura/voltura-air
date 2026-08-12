using System.Text.Json.Serialization;
using System.Text.Json;

namespace VolturaAir.Host;

public static class CustomScreenJson
{
    public static readonly JsonSerializerOptions Exact = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
}

public static class CustomScreenLimits
{
    public const int MaxStoreBytes = 8 * 1024 * 1024;
    public const int MaxScreens = 128;
    public const int MaxSectionsPerScreen = 64;
    public const int MaxButtonsPerScreen = 256;
    public const int MaxButtonRows = 6;
    public const int MaxScreenNameLength = 24;
    public const int MaxSectionNameLength = 20;
    public const int MaxButtonNameLength = 24;
    public const int MaxButtonLabelLength = 16;
    public const int MaxTextLength = 256;
    public const int MaxIdLength = 64;
    public const int MinViewportWidth = 240;
    public const int MaxViewportWidth = 4096;
    public const int MinViewportHeight = 240;
    public const int MaxViewportHeight = 4096;
}

public static class CustomScreenSectionKinds
{
    public static bool IsCollapsible(string kind) =>
        kind is "collapsible" or "collapsibleTrackpad";

    public static bool IsTrackpad(string kind) =>
        kind is "trackpad" or "collapsibleTrackpad";

    public static bool IsVolume(string kind) => kind == "volume";

    public static bool IsNavigationRing(string kind) => kind is "navigationRing" or "dpad";

    public static bool AllowsButtons(string kind) =>
        kind is "buttons" or "collapsible";
}

public sealed record CustomScreenDocument(
    int Version,
    IReadOnlyList<CustomScreenDefinition> Screens);

public sealed record CustomScreenDefinition(
    string Id,
    string Name,
    string Revision,
    IReadOnlyList<string> AssignedClientIds,
    bool OrientationLayoutsEnabled,
    bool ShowNavigationHeader,
    IReadOnlyList<CustomScreenSection> Sections);

public sealed record CustomScreenSection(
    string Id,
    string Name,
    bool ShowHeader,
    int WidthColumns,
    string HeightMode,
    int FillWeight,
    int RowLimit,
    CustomScreenLayoutOverride? Portrait,
    CustomScreenLayoutOverride? Landscape,
    IReadOnlyList<CustomScreenButton> Buttons,
    string Kind = "buttons",
    bool TrackpadLeftClick = true,
    bool TrackpadRightClick = true,
    string TrackpadButtonSide = "right",
    bool InitiallyExpanded = true,
    bool TrackpadFullscreenControl = false,
    bool TrackpadGyroControl = false,
    string ButtonAlignment = "start");

public sealed record CustomScreenButton(
    string Id,
    string Name,
    string Label,
    string Icon,
    string Presentation,
    string Size,
    bool Repeat,
    CustomScreenLayoutOverride? Portrait,
    CustomScreenLayoutOverride? Landscape,
    CustomScreenAction Action,
    int Row = 0);

public sealed record CustomScreenLayoutOverride(
    int Order,
    bool Visible,
    int? WidthColumns = null,
    string? Size = null,
    int? Row = null);

public sealed record CustomScreenAction(
    string Kind,
    string? Text = null,
    string? Key = null,
    IReadOnlyList<string>? Modifiers = null,
    string? ActionId = null,
    string? BuiltIn = null,
    string? Url = null,
    string? Color = null);

public sealed record CustomScreenSummary(string Id, string Name, string Revision);

public sealed record CustomScreenViewport(
    int Width,
    int Height,
    string Orientation);

public sealed record CustomScreenMobileDefinition(
    string Id,
    string Name,
    string Revision,
    bool OrientationLayoutsEnabled,
    bool ShowNavigationHeader,
    IReadOnlyList<CustomScreenMobileSection> Sections);

public sealed record CustomScreenMobileSection(
    string Id,
    string Name,
    bool ShowHeader,
    int WidthColumns,
    string HeightMode,
    int FillWeight,
    int RowLimit,
    string ButtonAlignment,
    CustomScreenLayoutOverride? Portrait,
    CustomScreenLayoutOverride? Landscape,
    IReadOnlyList<CustomScreenMobileButton> Buttons,
    string Kind,
    bool Collapsible,
    bool InitiallyExpanded,
    bool TrackpadLeftClick,
    bool TrackpadRightClick,
    string TrackpadButtonSide,
    bool TrackpadEnabled,
    string? TrackpadUnavailableReason,
    bool TrackpadFullscreenControl,
    bool TrackpadGyroControl,
    bool VolumeEnabled,
    string? VolumeUnavailableReason);

public sealed record CustomScreenMobileButton(
    string Id,
    string Name,
    string Label,
    string Icon,
    string Presentation,
    string Size,
    bool Repeat,
    CustomScreenLayoutOverride? Portrait,
    CustomScreenLayoutOverride? Landscape,
    bool Enabled,
    string? UnavailableReason,
    int Row,
    string? Confirmation = null,
    string? ConfirmationMessage = null,
    string? LaserPointerColor = null);

public sealed record CustomScreenStoreLoadResult(
    IReadOnlyList<CustomScreenDefinition> Screens,
    string? Error)
{
    [JsonIgnore]
    public bool Succeeded => Error is null;
}
