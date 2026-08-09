namespace VolturaAir.Host.Features.CustomScreens;

internal enum CustomScreenValidationSeverity
{
    CannotSave,
    Warning
}

internal sealed record CustomScreenLayoutIssue(
    string Kind,
    string ButtonId,
    string Label,
    string Orientation,
    string Size);

internal sealed record CustomScreenValidationFinding(
    CustomScreenValidationSeverity Severity,
    string Title,
    string Message,
    string Resolution,
    string? SectionId = null,
    string? ButtonId = null);

internal sealed record CustomScreenValidationReport(
    IReadOnlyList<CustomScreenValidationFinding> Findings,
    IReadOnlyList<string> PassedChecks);

internal static class CustomScreenValidationAnalyzer
{
    private static readonly HashSet<string> Modifiers =
        ["Control", "Shift", "Alt", "AltGr", "Win"];

    public static CustomScreenValidationReport Analyze(
        CustomScreenDefinition draft,
        IReadOnlyList<KnownAppProfileSummary> knownApplications,
        IReadOnlyList<AppLaunchActionSummary> approvedAppActions,
        IReadOnlyList<CustomScreenLayoutIssue>? layoutIssues,
        string? layoutFailure = null,
        HostPermissionSet? permissions = null)
    {
        var findings = new List<CustomScreenValidationFinding>();
        var passed = new List<string>();
        var specificSaveIssue = false;
        var shortcutsValid = true;
        var knownApplicationAvailability = knownApplications.ToDictionary(
            application => application.Id,
            application => application,
            StringComparer.Ordinal);
        var approvedActions = approvedAppActions
            .Select(action => action.Id)
            .ToHashSet(StringComparer.Ordinal);
        var currentPermissions = permissions ?? HostPermissions.DefaultGlobal;
        var reportedApplications = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in draft.Sections)
        {
            foreach (var button in section.Buttons)
            {
                var action = button.Action;
                if (action.Kind == "shortcut" &&
                    (!CustomScreenShortcutKeys.TryNormalize(action.Key, out _) ||
                     action.Modifiers is null ||
                     action.Modifiers.Count > 5 ||
                     action.Modifiers.Distinct(StringComparer.Ordinal).Count() !=
                        action.Modifiers.Count ||
                     action.Modifiers.Any(modifier => !Modifiers.Contains(modifier))))
                {
                    shortcutsValid = false;
                    specificSaveIssue = true;
                    findings.Add(CannotSave(
                        "Shortcut cannot be dispatched",
                        $"{button.Name} contains an unsupported key or modifier combination.",
                        "Choose a key and modifiers offered by the shortcut editor.",
                        section.Id,
                        button.Id));
                }

                if (action.Kind == "urlOpen" &&
                    (action.Url is null ||
                     !UrlOpenService.TryNormalize(action.Url, out _, out _, out _)))
                {
                    specificSaveIssue = true;
                    findings.Add(CannotSave(
                        "Web address is invalid",
                        $"{button.Name} does not contain a valid HTTP or HTTPS address.",
                        "Enter a complete address beginning with https:// or http://.",
                        section.Id,
                        button.Id));
                }

                if (action.Kind == "knownApp" &&
                    action.ActionId is { } knownAppId &&
                    reportedApplications.Add(knownAppId))
                {
                    if (!KnownAppProfiles.IsSupported(knownAppId))
                    {
                        specificSaveIssue = true;
                        findings.Add(CannotSave(
                            "Target application is unsupported",
                            $"{button.Name} refers to an application profile the host does not support.",
                            "Choose an application offered by the action editor.",
                            section.Id,
                            button.Id));
                    }
                    else if (!knownApplicationAvailability.TryGetValue(
                                 knownAppId,
                                 out var application) ||
                             !application.Available)
                    {
                        var label = KnownAppProfiles.Find(knownAppId)?.Label ??
                            "The target application";
                        findings.Add(Warning(
                            "Target application is unavailable",
                            $"{label} is not currently available on this PC.",
                            knownAppId == "windowsPhotos"
                                ? "Install or repair Microsoft Photos and its ms-photos URI handler, or choose another target application."
                                : $"Install or repair {label}, or choose another target application.",
                            section.Id,
                            button.Id));
                    }
                }

                if (action.Kind == "appLaunch" &&
                    (action.ActionId is null ||
                     !approvedActions.Contains(action.ActionId)))
                {
                    findings.Add(Warning(
                        "Approved application action is unavailable",
                        $"{button.Name} refers to a host-local application action that is not currently configured.",
                        "Choose an available approved application action in the editor.",
                        section.Id,
                        button.Id));
                }
            }
        }

        CustomScreenPermissionValidation.AddWarnings(
            draft,
            currentPermissions,
            findings);

        if (!CustomScreenValidator.TryValidate(draft, out var saveError) &&
            !specificSaveIssue)
        {
            findings.Add(CannotSave(
                "Draft does not satisfy the save contract",
                saveError,
                "Review the selected screen, panel, and button properties marked by the editor."));
        }

        if (layoutFailure is not null)
        {
            findings.Add(Warning(
                "Layout check could not run",
                layoutFailure,
                "Try Validate again. Saving remains available when the current draft satisfies the save contract."));
        }
        else
        {
            foreach (var issue in layoutIssues ?? [])
            {
                if (issue.Kind == "page")
                {
                    findings.Add(Warning(
                        $"Screen overflows in {issue.Orientation}",
                        "The rendered Custom Screen is larger than the compact phone viewport.",
                        "Reduce fixed-size content or use orientation-specific section layouts."));
                    continue;
                }

                if (issue.Kind == "button")
                {
                    var (overflowSectionId, overflowButton) = FindButton(draft, issue.ButtonId);
                    findings.Add(Warning(
                        $"Button overflows in {issue.Orientation}",
                        $"{issue.Label} extends outside its rendered panel or viewport.",
                        "Use a smaller button size, a new row, or an orientation-specific layout.",
                        overflowSectionId,
                        overflowButton?.Id));
                    continue;
                }

                var (sectionId, button) = FindButton(draft, issue.ButtonId);
                findings.Add(Warning(
                    $"Label is clipped in {issue.Orientation}",
                    $"{issue.Label} does not fit inside its {issue.Size} button at the compact phone viewport.",
                    SuggestedLayoutResolution(issue.Size),
                    sectionId,
                    button?.Id));
            }

            if ((layoutIssues?.Count ?? 0) == 0)
            {
                passed.Add("Button labels fit at 360 × 640 and 640 × 360.");
            }
        }

        if (findings.All(finding =>
                finding.Severity != CustomScreenValidationSeverity.CannotSave))
        {
            passed.Add("The current draft satisfies the existing save contract.");
        }
        if (shortcutsValid)
        {
            passed.Add("Every keyboard shortcut can be represented and dispatched by the host.");
        }
        if (!findings.Any(finding =>
                finding.Title is "Target application is unavailable" or
                    "Approved application action is unavailable"))
        {
            passed.Add("Referenced application actions are currently available on this PC.");
        }

        return new(findings, passed);
    }

    private static (string? SectionId, CustomScreenButton? Button) FindButton(
        CustomScreenDefinition draft,
        string buttonId)
    {
        foreach (var section in draft.Sections)
        {
            var button = section.Buttons.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, buttonId, StringComparison.Ordinal));
            if (button is not null)
            {
                return (section.Id, button);
            }
        }
        return (null, null);
    }

    private static string SuggestedLayoutResolution(string size) => size switch
    {
        "compact" => "Change the button size to Standard, Wide, or Fill, or intentionally keep the clipped label.",
        "standard" => "Change the button size to Wide or Fill, or intentionally keep the clipped label.",
        "wide" => "Change the button size to Fill, use an orientation-specific size, or intentionally keep the clipped label.",
        _ => "Use an orientation-specific layout or intentionally keep the clipped label."
    };

    private static CustomScreenValidationFinding CannotSave(
        string title,
        string message,
        string resolution,
        string? sectionId = null,
        string? buttonId = null) =>
        new(CustomScreenValidationSeverity.CannotSave, title, message,
            resolution, sectionId, buttonId);

    private static CustomScreenValidationFinding Warning(
        string title,
        string message,
        string resolution,
        string? sectionId = null,
        string? buttonId = null) =>
        new(CustomScreenValidationSeverity.Warning, title, message,
            resolution, sectionId, buttonId);
}
