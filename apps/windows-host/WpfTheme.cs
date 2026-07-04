using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace VolturaAir.Host;

internal static class WpfTheme
{
    public static void Apply(Window window)
    {
        var theme = WindowsTheme.Current();
        var resources = window.Resources;
        resources["WindowBrush"] = ToBrush(theme.Window);
        resources["SurfaceBrush"] = ToBrush(theme.Surface);
        resources["SurfaceRaisedBrush"] = ToBrush(theme.SurfaceRaised);
        resources["TextBrush"] = ToBrush(theme.Text);
        resources["MutedTextBrush"] = ToBrush(theme.MutedText);
        resources["BorderBrush"] = ToBrush(theme.Border);
        resources["AccentBrush"] = ToBrush(theme.Accent);
        resources["AccentTextBrush"] = ToBrush(theme.AccentText);
        resources["DangerBrush"] = ToBrush(theme.Danger);
        resources["QrBackgroundBrush"] = ToBrush(theme.QrBackground);

        window.Background = (Brush)resources["WindowBrush"];
        window.Foreground = (Brush)resources["TextBrush"];
        ApplyImmersiveDarkMode(window, theme.IsDark);
    }

    public static SolidColorBrush ToBrush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static void ApplyImmersiveDarkMode(Window window, bool isDark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var helper = new WindowInteropHelper(window);
        if (helper.Handle == 0)
        {
            window.SourceInitialized += (_, _) => ApplyImmersiveDarkMode(window, isDark);
            return;
        }

        var value = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(helper.Handle, 20, ref value, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int attributeValue, int attributeSize);
}
