namespace VolturaAir.Host;

internal static class CustomScreenDraftFactory
{
    public static CustomScreenDefinition CreateDraft()
    {
        var sectionId = $"section.{Guid.NewGuid():N}";
        return new CustomScreenDefinition(
            $"screen.{Guid.NewGuid():N}",
            "New custom screen",
            Guid.NewGuid().ToString("N"),
            [],
            false,
            true,
            [
                new CustomScreenSection(
                    sectionId,
                    "Controls",
                    true,
                    12,
                    "fill",
                    1,
                    2,
                    null,
                    null,
                    [
                        CreateBuiltInButton("Play / pause", "media.playPause", "play"),
                        CreateBuiltInButton("Previous", "media.previous", "skip-back"),
                        CreateBuiltInButton("Next", "media.next", "skip-forward")
                    ])
            ]);
    }

    public static CustomScreenDefinition CreateSection(CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "New panel",
            true,
            12,
            "content",
            1,
            0,
            null,
            null,
            [])).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateCollapsibleSection(
        CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "Collapsible panel",
            true,
            12,
            "content",
            1,
            0,
            null,
            null,
            [],
            Kind: "collapsible",
            InitiallyExpanded: true)).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateButton(
        CustomScreenDefinition screen,
        string sectionId,
        int row)
    {
        var button = CreateBuiltInButton(
            "New button",
            "media.playPause",
            "play") with
        {
            Row = row
        };
        var sections = screen.Sections.Select(section =>
            string.Equals(section.Id, sectionId, StringComparison.Ordinal)
                ? section with { Buttons = [.. section.Buttons, button] }
                : section).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateTrackpad(CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "Trackpad",
            true,
            12,
            "fill",
            1,
            0,
            null,
            null,
            [],
            Kind: "trackpad",
            TrackpadLeftClick: true,
            TrackpadRightClick: true,
            TrackpadButtonSide: "right",
            TrackpadFullscreenControl: true)).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateCollapsibleTrackpad(
        CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "Collapsible trackpad",
            true,
            12,
            "fill",
            1,
            0,
            null,
            null,
            [],
            Kind: "collapsibleTrackpad",
            TrackpadLeftClick: true,
            TrackpadRightClick: true,
            TrackpadButtonSide: "right",
            InitiallyExpanded: true,
            TrackpadFullscreenControl: true)).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateVolumeSlider(
        CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "Volume slider",
            false,
            12,
            "content",
            1,
            0,
            null,
            null,
            [],
            Kind: "volume")).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateNavigationRing(
        CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "Navigation ring",
            false,
            12,
            "content",
            1,
            0,
            null,
            null,
            [],
            Kind: "navigationRing")).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CreateDPad(CustomScreenDefinition screen)
    {
        var sections = screen.Sections.Append(new CustomScreenSection(
            $"section.{Guid.NewGuid():N}",
            "D-pad",
            false,
            12,
            "content",
            1,
            0,
            null,
            null,
            [],
            Kind: "dpad")).ToArray();
        return screen with { Sections = sections };
    }

    public static CustomScreenDefinition CloneWithNewIds(CustomScreenDefinition source) =>
        source with
        {
            Id = $"screen.{Guid.NewGuid():N}",
            Revision = Guid.NewGuid().ToString("N"),
            Sections = [.. source.Sections.Select(section => section with
            {
                Id = $"section.{Guid.NewGuid():N}",
                Buttons = [.. section.Buttons.Select(button => button with
                {
                    Id = $"button.{Guid.NewGuid():N}"
                })]
            })]
        };

    private static CustomScreenButton CreateBuiltInButton(
        string label,
        string builtIn,
        string icon) =>
        new(
            $"button.{Guid.NewGuid():N}",
            label,
            label,
            icon,
            "iconLabel",
            "standard",
            false,
            null,
            null,
            new CustomScreenAction("builtIn", BuiltIn: builtIn));
}
