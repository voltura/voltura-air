namespace VolturaAir.Host;

internal readonly record struct PresentationShortcut(string Key, IReadOnlyList<string> Modifiers, string ResultMessage);

internal static class PresentationCommands
{
    private static readonly string[] Targets = ["powerpoint", "google-slides", "pdf"];
    private static readonly string[] Actions =
    [
        "next",
        "previous",
        "start",
        "start-current",
        "first",
        "last",
        "goto",
        "end",
        "black",
        "white",
        "pause",
        "pointer",
        "activate"
    ];

    public static bool IsTarget(string target) => Targets.Contains(target, StringComparer.Ordinal);

    public static bool IsAction(string action) => Actions.Contains(action, StringComparer.Ordinal);

    public static bool TryResolve(string target, string action, out PresentationShortcut shortcut)
    {
        shortcut = (target, action) switch
        {
            ("google-slides" or "pdf", "next") => new("ArrowRight", [], "Next slide command sent."),
            ("google-slides" or "pdf", "previous") => new("ArrowLeft", [], "Previous slide command sent."),
            ("google-slides" or "pdf", "end") => new("Escape", [], "End slideshow command sent."),
            ("google-slides", "black") => new("B", [], "Black screen command sent."),
            _ => default
        };

        return shortcut != default;
    }
}
