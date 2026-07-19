using System.Drawing;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ThemePaletteContrastTests
{
    [Theory]
    [MemberData(nameof(Palettes))]
    public void FilledSemanticColorsKeepReadableText(ThemePalette palette)
    {
        Assert.True(ContrastRatio(palette.AccentStrong, palette.AccentText) >= 4.5);
        Assert.True(ContrastRatio(palette.SuccessStrong, palette.AccentText) >= 4.5);
        Assert.True(ContrastRatio(palette.DangerStrong, palette.AccentText) >= 4.5);
    }

    public static TheoryData<ThemePalette> Palettes => new()
    {
        UiTokens.DarkPalette,
        UiTokens.LightPalette
    };

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        return 0.2126 * Linearize(color.R) +
            0.7152 * Linearize(color.G) +
            0.0722 * Linearize(color.B);
    }

    private static double Linearize(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
