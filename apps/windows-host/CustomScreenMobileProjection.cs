namespace VolturaAir.Host;

internal sealed class CustomScreenMobileProjection(
    IAppLaunchService appLaunchService)
{
    public CustomScreenMobileDefinition ToMobile(
        CustomScreenDefinition screen,
        bool canUseRemoteInput,
        bool canLaunchApps,
        bool canControlVolume,
        bool canOpenUrls,
        HostPermissionSet permissions,
        IReadOnlySet<string>? unavailableHostActions = null)
    {
        var availableAppActions = appLaunchService.GetActions()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var availableKnownApps = appLaunchService.GetKnownApplications()
            .Where(item => item.Available)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var requiredKnownApp = CustomScreenKnownAppDependency.Find(screen);
        var requiredKnownAppAvailable = requiredKnownApp is null ||
            availableKnownApps.Contains(requiredKnownApp);
        var requiredKnownAppReason = requiredKnownApp is null || requiredKnownAppAvailable
            ? null
            : CustomScreenKnownAppDependency.UnavailableReason(requiredKnownApp);
        return new(
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
                    canControlVolume,
                    canOpenUrls,
                    permissions,
                    availableAppActions,
                    availableKnownApps,
                    unavailableHostActions ?? EmptyHostActions.Instance,
                    requiredKnownAppAvailable,
                    requiredKnownAppReason))]);
    }

    private static CustomScreenMobileSection ToMobileSection(
        CustomScreenSection section,
        bool canUseRemoteInput,
        bool canLaunchApps,
        bool canControlVolume,
        bool canOpenUrls,
        HostPermissionSet permissions,
        IReadOnlySet<string> availableAppActions,
        IReadOnlySet<string> availableKnownApps,
        IReadOnlySet<string> unavailableHostActions,
        bool requiredKnownAppAvailable,
        string? requiredKnownAppReason) =>
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
                    canLaunchApps,
                    canOpenUrls,
                    permissions,
                    availableAppActions,
                    availableKnownApps,
                    unavailableHostActions);
                if (!requiredKnownAppAvailable)
                {
                    availability = (false, requiredKnownAppReason);
                }
                var hostAction = button.Action.Kind == "hostAction"
                    ? CustomScreenHostActions.Find(button.Action.ActionId)
                    : null;
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
                    button.Row,
                    hostAction?.Confirmation == "none" ? null : hostAction?.Confirmation,
                    hostAction?.ConfirmationMessage,
                    button.Action.Kind == "laserPointer"
                        ? button.Action.Color
                        : null);
            })],
            CustomScreenSectionKinds.IsTrackpad(section.Kind)
                ? "trackpad"
                : CustomScreenSectionKinds.IsVolume(section.Kind)
                    ? "volume"
                    : CustomScreenSectionKinds.IsNavigationRing(section.Kind)
                        ? section.Kind == "dpad" ? "dpad" : "navigationRing"
                    : "buttons",
            CustomScreenSectionKinds.IsCollapsible(section.Kind),
            section.InitiallyExpanded,
            section.TrackpadLeftClick,
            section.TrackpadRightClick,
            section.TrackpadButtonSide,
            requiredKnownAppAvailable && canUseRemoteInput,
            !requiredKnownAppAvailable
                ? requiredKnownAppReason
                : canUseRemoteInput
                    ? null
                    : "Remote input is disabled for this device on the PC.",
            section.TrackpadFullscreenControl,
            section.TrackpadGyroControl,
            requiredKnownAppAvailable && canControlVolume,
            !requiredKnownAppAvailable
                ? requiredKnownAppReason
                : canControlVolume
                    ? null
                    : "Volume control is disabled for this device on the PC.");

    private static (bool Enabled, string? Reason) ResolveAvailability(
        CustomScreenAction action,
        bool canUseRemoteInput,
        bool canLaunchApps,
        bool canOpenUrls,
        HostPermissionSet permissions,
        IReadOnlySet<string> availableAppActions,
        IReadOnlySet<string> availableKnownApps,
        IReadOnlySet<string> unavailableHostActions)
    {
        if (action.Kind == "appLaunch")
        {
            if (!canLaunchApps)
            {
                return (
                    false,
                    "Application launch is disabled for this device on the PC.");
            }

            return action.ActionId is not null && availableAppActions.Contains(action.ActionId)
                ? (true, null)
                : (
                    false,
                    "This approved application action is no longer available.");
        }

        if (action.Kind == "knownApp")
        {
            if (!canLaunchApps)
            {
                return (false, "Application launch is disabled for this device on the PC.");
            }

            return action.ActionId is not null && availableKnownApps.Contains(action.ActionId)
                ? (true, null)
                : (false, "This known application is unavailable on the PC.");
        }

        if (action.Kind == "urlOpen")
        {
            return canOpenUrls
                ? (true, null)
                : (false, "Opening web addresses is disabled for this device on the PC.");
        }

        if (action.Kind == "hostAction")
        {
            if (action.ActionId is not null && unavailableHostActions.Contains(action.ActionId))
            {
                return (false, "This host or system action is unavailable on this PC.");
            }

            return CustomScreenHostActions.IsPermitted(action.ActionId, permissions)
                ? (true, null)
                : (false, "This host or system action is disabled for this device on the PC.");
        }

        if (action.Kind == "laserPointer")
        {
            return permissions.AllowPresentationControl
                ? (true, null)
                : (false, "Presentation control is disabled for this device on the PC.");
        }

        return canUseRemoteInput
            ? (true, null)
            : (
                false,
                "Remote input is disabled for this device on the PC.");
    }

    private sealed class EmptyHostActions : HashSet<string>
    {
        public static EmptyHostActions Instance { get; } = new();
    }
}
