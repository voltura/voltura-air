using Microsoft.Win32;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public sealed record ThemePalette(
    bool IsDark,
    Color Window,
    Color Surface,
    Color SurfaceRaised,
    Color Text,
    Color MutedText,
    Color Border,
    Color Accent,
    Color AccentText,
    Color Danger,
    Color QrBackground);

public static partial class WindowsTheme
{
    public static Color DarkAccent { get; } = Color.FromArgb(18, 168, 148);

    public static ThemePalette Current()
    {
        var isDark = AppThemeSettings.GetMode() switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsDarkAppTheme()
        };

        return isDark
            ? new ThemePalette(
                true,
                Color.FromArgb(20, 24, 28),
                Color.FromArgb(28, 34, 39),
                Color.FromArgb(35, 42, 48),
                Color.FromArgb(244, 247, 248),
                Color.FromArgb(178, 188, 195),
                Color.FromArgb(60, 70, 78),
                DarkAccent,
                Color.White,
                Color.FromArgb(215, 91, 70),
                Color.White)
            : new ThemePalette(
                false,
                Color.FromArgb(246, 248, 250),
                Color.White,
                Color.FromArgb(238, 242, 244),
                Color.FromArgb(28, 34, 39),
                Color.FromArgb(92, 103, 112),
                Color.FromArgb(214, 221, 226),
                Color.FromArgb(15, 123, 108),
                Color.White,
                Color.FromArgb(192, 83, 58),
                Color.White);
    }

    public static void ApplyImmersiveDarkMode(Form form, bool isDark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var value = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref value, Marshal.SizeOf<int>());
    }

    private static bool IsDarkAppTheme()
    {
        var registryValue = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1);
        return registryValue is int lightTheme && lightTheme == 0;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int attributeValue, int attributeSize);
}
