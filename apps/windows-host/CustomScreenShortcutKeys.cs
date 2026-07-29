namespace VolturaAir.Host;

public static class CustomScreenShortcutKeys
{
    private static readonly string[] OtherNamedKeys =
    [
        "BrowserBack", "BrowserForward", "+"
    ];

    private static readonly string[] SymbolKeyNames =
    [
        ".", ",", ";", "/", "\\", "'", "`", "[", "]", "-", "="
    ];

    private static readonly string[] SpecialKeyNames =
    [
        "Backspace", "Delete", "Enter", "Insert", "Tab", "Escape", "Space",
        "PageUp", "PageDown", "Home", "End",
        "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"
    ];

    private static readonly string[] FunctionKeyNames =
    [
        "F1", "F2", "F3", "F4", "F5", "F6",
        "F7", "F8", "F9", "F10", "F11", "F12"
    ];

    private static readonly string[] NamedKeys =
    [
        .. OtherNamedKeys,
        .. SymbolKeyNames,
        .. SpecialKeyNames,
        .. FunctionKeyNames
    ];

    public static IReadOnlyList<string> Suggestions { get; } =
    [
        .. Enumerable.Range('A', 26).Select(value => ((char)value).ToString()),
        .. Enumerable.Range('0', 10).Select(value => ((char)value).ToString())
    ];

    public static IReadOnlyList<string> FunctionKeys { get; } =
        Array.AsReadOnly(FunctionKeyNames);

    public static IReadOnlyList<string> SpecialKeys { get; } =
        Array.AsReadOnly(SpecialKeyNames);

    public static IReadOnlyList<string> SymbolKeys { get; } =
        Array.AsReadOnly(SymbolKeyNames);

    public static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length == 1 && char.IsAsciiLetterOrDigit(candidate[0]))
        {
            normalized = candidate.ToUpperInvariant();
            return true;
        }

        var named = NamedKeys.FirstOrDefault(key =>
            string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase));
        if (named is null)
        {
            normalized = candidate;
            return false;
        }

        normalized = named;
        return true;
    }
}
