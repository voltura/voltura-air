namespace VolturaAir.Host;

internal sealed class CustomScreenMobileProjection(
    IAppLaunchService appLaunchService)
{
    public CustomScreenMobileDefinition ToMobile(
        CustomScreenDefinition screen,
        bool canUseRemoteInput,
        bool canLaunchApps,
        bool canControlVolume) =>
        new(
            screen.Id,
            screen.Name,
            screen.Revision,
            screen.OrientationLayoutsEnabled,
            screen.ShowNavigationHeader,
            [.. screen.Sections.Select(section =>
                ToMobileSection(
                    section,
                    canUseRemoteInput,
                    canLaunchApps,
                    canControlVolume))]);

    private CustomScreenMobileSection ToMobileSection(
        CustomScreenSection section,
        bool canUseRemoteInput,
        bool canLaunchApps,
        bool canControlVolume) =>
        new(
            section.Id,
            section.Name,
            section.ShowHeader,
            section.WidthColumns,
            section.HeightMode,
            section.FillWeight,
            section.RowLimit,
            section.ButtonAlignment,
            section.Portrait,
            section.Landscape,
            [.. section.Buttons.Select(button =>
            {
                var availability = ResolveAvailability(
                    button.Action,
                    canUseRemoteInput,
                    canLaunchApps);
                return new CustomScreenMobileButton(
                    button.Id,
                    button.Name,
                    button.Label,
                    button.Icon,
                    button.Presentation,
                    button.Size,
                    button.Repeat &&
                        CustomScreenService.IsRepeatable(button.Action),
                    button.Portrait,
                    button.Landscape,
                    availability.Enabled,
                    availability.Reason,
                    button.Row);
            })],
            CustomScreenSectionKinds.IsTrackpad(section.Kind)
                ? "trackpad"
                : CustomScreenSectionKinds.IsVolume(section.Kind)
                    ? "volume"
                    : CustomScreenSectionKinds.IsNavigationRing(section.Kind)
                        ? "navigationRing"
                    : "buttons",
            CustomScreenSectionKinds.IsCollapsible(section.Kind),
            section.InitiallyExpanded,
            section.TrackpadLeftClick,
            section.TrackpadRightClick,
            section.TrackpadButtonSide,
            canUseRemoteInput,
            canUseRemoteInput
                ? null
                : "Remote input is disabled for this device on the PC.",
            section.TrackpadFullscreenControl,
            canControlVolume,
            canControlVolume
                ? null
                : "Volume control is disabled for this device on the PC.");

    private (bool Enabled, string? Reason) ResolveAvailability(
        CustomScreenAction action,
        bool canUseRemoteInput,
        bool canLaunchApps)
    {
        if (action.Kind == "appLaunch")
        {
            if (!canLaunchApps)
            {
                return (
                    false,
                    "Application launch is disabled for this device on the PC.");
            }

            return appLaunchService.GetActions().Any(item =>
                string.Equals(
                    item.Id,
                    action.ActionId,
                    StringComparison.Ordinal))
                ? (true, null)
                : (
                    false,
                    "This approved application action is no longer available.");
        }

        return canUseRemoteInput
            ? (true, null)
            : (
                false,
                "Remote input is disabled for this device on the PC.");
    }
}
