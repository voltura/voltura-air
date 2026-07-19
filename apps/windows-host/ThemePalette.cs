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
    Color AccentStrong,
    Color AccentText,
    Color Focus,
    Color SuccessStrong,
    Color Danger,
    Color DangerStrong,
    Color QrBackground);

public static partial class WindowsTheme
{
    public static Color DarkAccent => UiTokens.DarkPalette.Accent;

    public static ThemePalette Current()
    {
        var isDark = AppThemeSettings.GetMode() switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsDarkAppTheme()
        };

        return isDark ? UiTokens.DarkPalette : UiTokens.LightPalette;
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
