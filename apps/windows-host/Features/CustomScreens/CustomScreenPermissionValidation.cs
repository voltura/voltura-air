namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenPermissionValidation
{
    public static void AddWarnings(
        CustomScreenDefinition draft,
        HostPermissionSet permissions,
        List<CustomScreenValidationFinding> findings)
    {
        var buttons = draft.Sections
            .SelectMany(section => section.Buttons.Select(button => (section, button)))
            .ToArray();
        var remoteInput = buttons.FirstOrDefault(item =>
            item.button.Action.Kind is "text" or "shortcut" or "builtIn");
        var remoteInputSection = draft.Sections.FirstOrDefault(section =>
            CustomScreenSectionKinds.IsTrackpad(section.Kind) ||
            CustomScreenSectionKinds.IsNavigationRing(section.Kind));
        if (!permissions.AllowRemoteInput &&
            (remoteInput.button is not null || remoteInputSection is not null))
        {
            findings.Add(Warning(
                "Remote input permission is disabled",
                "Keyboard, navigation, and trackpad controls are disabled by the current global PC permission.",
                "Enable Remote input in Preferences, or allow it for the intended paired device.",
                remoteInput.section?.Id ?? remoteInputSection?.Id,
                remoteInput.button?.Id));
        }

        var volumeSection = draft.Sections.FirstOrDefault(section =>
            CustomScreenSectionKinds.IsVolume(section.Kind));
        if (!permissions.AllowVolumeControl && volumeSection is not null)
        {
            findings.Add(Warning(
                "Volume permission is disabled",
                "Volume controls are disabled by the current global PC permission.",
                "Enable Volume control in Preferences, or allow it for the intended paired device.",
                volumeSection.Id));
        }

        var appLaunch = buttons.FirstOrDefault(item =>
            item.button.Action.Kind is "knownApp" or "appLaunch");
        if (!permissions.AllowRemoteAppLaunch && appLaunch.button is not null)
        {
            findings.Add(Warning(
                "Application launch permission is disabled",
                "Application buttons are disabled by the current global PC permission.",
                "Enable Application launch in Preferences, or allow it for the intended paired device.",
                appLaunch.section.Id,
                appLaunch.button.Id));
        }

        var url = buttons.FirstOrDefault(item => item.button.Action.Kind == "urlOpen");
        if (!permissions.AllowUrlOpen && url.button is not null)
        {
            findings.Add(Warning(
                "Website permission is disabled",
                "Website buttons are disabled by the current global PC permission.",
                "Enable opening web addresses in Preferences, or allow it for the intended paired device.",
                url.section.Id,
                url.button.Id));
        }

        foreach (var item in buttons.Where(item =>
                     item.button.Action.Kind == "hostAction" &&
                     !CustomScreenHostActions.IsPermitted(
                         item.button.Action.ActionId,
                         permissions)))
        {
            findings.Add(Warning(
                "Host action permission is disabled",
                $"{item.button.Name} is disabled by the current global PC permission.",
                "Enable the matching host permission in Preferences, or allow it for the intended paired device.",
                item.section.Id,
                item.button.Id));
        }
    }

    private static CustomScreenValidationFinding Warning(
        string title,
        string message,
        string resolution,
        string? sectionId = null,
        string? buttonId = null) =>
        new(CustomScreenValidationSeverity.Warning, title, message,
            resolution, sectionId, buttonId);
}
