namespace VolturaAir.Host;

internal readonly record struct PresentationShortcut(string Key, IReadOnlyList<string> Modifiers, string ResultMessage);

internal static class PresentationCommands
{
    private static readonly string[] Targets = ["powerpoint", "google-slides", "pdf"];
    private static readonly string[] Actions = ["next", "previous", "start", "end", "black", "pointer"];

    public static bool IsTarget(string target) => Targets.Contains(target, StringComparer.Ordinal);

    public static bool IsAction(string action) => Actions.Contains(action, StringComparer.Ordinal);

    public static bool TryResolve(string target, string action, out PresentationShortcut shortcut)
    {
        shortcut = (target, action) switch
        {
            (_, "next") when IsTarget(target) => new("ArrowRight", [], "Next slide command sent."),
            (_, "previous") when IsTarget(target) => new("ArrowLeft", [], "Previous slide command sent."),
            (_, "end") when IsTarget(target) => new("Escape", [], "End slideshow command sent."),
            ("powerpoint", "start") => new("F5", [], "Start slideshow command sent."),
            ("powerpoint" or "google-slides", "black") => new("B", [], "Black screen command sent."),
            ("powerpoint", "pointer") => new("L", ["Control"], "PowerPoint laser pointer command sent."),
            ("google-slides", "pointer") => new("L", [], "Google Slides laser pointer command sent."),
            _ => default
        };

        return shortcut != default;
    }
}
