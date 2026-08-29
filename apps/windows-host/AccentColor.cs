using System.Globalization;

namespace VolturaAir.Host;

internal static class AccentColor
{
    public const string DefaultSeed = "#12A894";

    public static bool IsCanonical(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index]) && value[index] is not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }
        return true;
    }

    public static string? NormalizePersisted(string? value) => IsCanonical(value) ? value : null;

    public static uint ToRgb(string value) =>
        uint.Parse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    public static string FromRgb(uint value) => $"#{value & 0xFFFFFF:X6}";
}
