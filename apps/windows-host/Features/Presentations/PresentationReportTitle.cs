namespace VolturaAir.Host.Features.Presentations;

internal static class PresentationReportTitle
{
    public const int MaxLength = 120;
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool TryNormalize(string candidate, string currentTitle, out string title, out string? error)
    {
        title = candidate.Trim();
        if (title.Length == 0)
        {
            title = currentTitle;
            error = null;
            return true;
        }

        if (title.Length > MaxLength)
        {
            error = $"Use {MaxLength} characters or fewer.";
            return false;
        }

        if (title.EndsWith('.') ||
            candidate.EndsWith(' ') ||
            title.Any(character => character < 32 || Path.GetInvalidFileNameChars().Contains(character)))
        {
            error = "Use a valid Windows filename without reserved characters or a trailing period.";
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(title);
        if (ReservedNames.Contains(baseName))
        {
            error = "That name is reserved by Windows. Choose another name.";
            return false;
        }

        error = null;
        return true;
    }
}
