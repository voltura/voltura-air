namespace VolturaAir.Host;

internal static class CustomScreenKnownAppDependency
{
    public static string? Find(CustomScreenDefinition screen)
    {
        var targets = screen.Sections
            .SelectMany(section => section.Buttons)
            .Where(button => button.Action.Kind == "knownApp")
            .Select(button => button.Action.ActionId)
            .Where(actionId => !string.IsNullOrWhiteSpace(actionId))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        return targets.Length == 1 ? targets[0] : null;
    }

    public static string UnavailableReason(string actionId)
    {
        var label = KnownAppProfiles.Find(actionId)?.Label ?? "target application";
        return $"This Custom Screen requires {label} on the PC.";
    }
}
