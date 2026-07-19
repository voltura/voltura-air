using System.Runtime.InteropServices;
using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using WpfSystemColors = System.Windows.SystemColors;

namespace VolturaAir.Host;

internal static partial class WpfTheme
{
    public static void Apply(Window window)
    {
        Apply(window, SystemParameters.HighContrast);
    }

    internal static void Apply(Window window, bool highContrast)
    {
        if (highContrast)
        {
            ApplyHighContrast(window);
            return;
        }

        var theme = WindowsTheme.Current();
        var resources = window.Resources;
        resources["WindowBrush"] = ToBrush(theme.Window);
        resources["SurfaceBrush"] = ToBrush(theme.Surface);
        resources["SurfaceRaisedBrush"] = ToBrush(theme.SurfaceRaised);
        resources["TextBrush"] = ToBrush(theme.Text);
        resources["MutedTextBrush"] = ToBrush(theme.MutedText);
        resources["BorderBrush"] = ToBrush(theme.Border);
        resources["AccentBrush"] = ToBrush(theme.Accent);
        resources["AccentStrongBrush"] = ToBrush(theme.AccentStrong);
        resources["AccentTextBrush"] = ToBrush(theme.AccentText);
        resources["FocusBrush"] = ToBrush(theme.Focus);
        resources["SuccessStrongBrush"] = ToBrush(theme.SuccessStrong);
        resources["DangerBrush"] = ToBrush(theme.Danger);
        resources["DangerStrongBrush"] = ToBrush(theme.DangerStrong);
        resources["QrBackgroundBrush"] = ToBrush(theme.QrBackground);

        window.Background = (Brush)resources["WindowBrush"];
        window.Foreground = (Brush)resources["TextBrush"];
        ApplyImmersiveDarkMode(window, theme.IsDark);
    }

    public static void TrackAccessibilityChanges(Window window, Action afterApply)
    {
        var refreshQueued = false;
        void ScheduleRefresh()
        {
            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            _ = window.Dispatcher.InvokeAsync(() =>
            {
                refreshQueued = false;
                Apply(window);
                afterApply();
            }, DispatcherPriority.ApplicationIdle);
        }

        PropertyChangedEventHandler? handler = null;
        handler = (_, eventArgs) =>
        {
            if (SystemParameters.HighContrast || eventArgs.PropertyName == nameof(SystemParameters.HighContrast))
            {
                ScheduleRefresh();
            }
        };
        SystemParameters.StaticPropertyChanged += handler;
        window.Closed += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= handler;
        };
    }

    private static void ApplyHighContrast(Window window)
    {
        var resources = window.Resources;
        resources["WindowBrush"] = WpfSystemColors.WindowBrush;
        resources["SurfaceBrush"] = WpfSystemColors.ControlBrush;
        resources["SurfaceRaisedBrush"] = WpfSystemColors.ControlBrush;
        resources["TextBrush"] = WpfSystemColors.ControlTextBrush;
        resources["MutedTextBrush"] = WpfSystemColors.GrayTextBrush;
        resources["BorderBrush"] = WpfSystemColors.WindowTextBrush;
        resources["AccentBrush"] = WpfSystemColors.HighlightBrush;
        resources["AccentStrongBrush"] = WpfSystemColors.HighlightBrush;
        resources["AccentTextBrush"] = WpfSystemColors.HighlightTextBrush;
        resources["FocusBrush"] = WpfSystemColors.WindowTextBrush;
        resources["SuccessStrongBrush"] = WpfSystemColors.HighlightBrush;
        resources["DangerBrush"] = WpfSystemColors.WindowTextBrush;
        resources["DangerStrongBrush"] = WpfSystemColors.HighlightBrush;
        resources["QrBackgroundBrush"] = WpfSystemColors.WindowBrush;

        window.Background = WpfSystemColors.WindowBrush;
        window.Foreground = WpfSystemColors.WindowTextBrush;
        ApplyImmersiveDarkMode(window, false);
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

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int attributeValue, int attributeSize);
}
