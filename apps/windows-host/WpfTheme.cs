using System.Runtime.InteropServices;
using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColors = System.Windows.Media.Colors;
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
        resources["PresentationSegmentBrush"] = ToBrush(theme.PresentationSegment);
        resources["PresentationBreakBrush"] = ToBrush(theme.PresentationBreak);
        var controlDepth = AppAppearanceSettings.HostControlDepth();
        var controlHighlight = controlDepth ? theme.ControlHighlight : System.Drawing.Color.Transparent;
        var controlShadow = controlDepth ? theme.ControlShadow : System.Drawing.Color.Transparent;
        resources["ControlHighlightBrush"] = ToBrush(controlHighlight);
        resources["ControlHighlightColor"] = ToMediaColor(controlHighlight);
        resources["ControlShadowColor"] = ToMediaColor(controlShadow);
        resources["ControlElevationShadowColor"] = ToMediaColor(controlShadow);
        resources["ControlDepthShadowOpacity"] = controlDepth ? 0.7d : 0d;
        resources["ControlDepthSubtleShadowOpacity"] = controlDepth ? 0.65d : 0d;
        resources["ControlDepthPressedShadowOpacity"] = controlDepth ? 0.55d : 0d;
        resources["ControlDepthInsetShadowOpacity"] = controlDepth ? 0.72d : 0d;
        resources["ControlDepthRaisedBrush"] = controlDepth ? ToBrush(theme.SurfaceRaised) : WpfBrushes.Transparent;
        resources["QrBackgroundBrush"] = ToBrush(theme.QrBackground);

        window.Background = (Brush)resources["WindowBrush"];
        window.Foreground = (Brush)resources["TextBrush"];
        ApplyImmersiveDarkMode(window, theme.IsDark);
    }

    public static void TrackAccessibilityChanges(Window window, Action afterApply)
    {
        // The tracked window's Closed event owns and disposes this queued action.
#pragma warning disable CA2000
        var refresh = new OwnedDispatcherAction(window.Dispatcher, () =>
        {
            Apply(window);
            afterApply();
        });
#pragma warning restore CA2000

        PropertyChangedEventHandler? handler = null;
        handler = (_, eventArgs) =>
        {
            if (SystemParameters.HighContrast || eventArgs.PropertyName == nameof(SystemParameters.HighContrast))
            {
                refresh.Queue(DispatcherPriority.ApplicationIdle);
            }
        };
        SystemParameters.StaticPropertyChanged += handler;
        window.Closed += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= handler;
            refresh.Dispose();
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
        resources["PresentationSegmentBrush"] = WpfSystemColors.WindowTextBrush;
        resources["PresentationBreakBrush"] = WpfSystemColors.HighlightBrush;
        resources["ControlHighlightBrush"] = WpfBrushes.Transparent;
        resources["ControlHighlightColor"] = WpfColors.Transparent;
        resources["ControlShadowColor"] = WpfColors.Transparent;
        resources["ControlElevationShadowColor"] = WpfColors.Transparent;
        resources["ControlDepthShadowOpacity"] = 0d;
        resources["ControlDepthSubtleShadowOpacity"] = 0d;
        resources["ControlDepthPressedShadowOpacity"] = 0d;
        resources["ControlDepthInsetShadowOpacity"] = 0d;
        resources["ControlDepthRaisedBrush"] = WpfBrushes.Transparent;
        resources["QrBackgroundBrush"] = WpfSystemColors.WindowBrush;

        window.Background = WpfSystemColors.WindowBrush;
        window.Foreground = WpfSystemColors.WindowTextBrush;
        ApplyImmersiveDarkMode(window, false);
    }

    public static SolidColorBrush ToBrush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(ToMediaColor(color));
        brush.Freeze();
        return brush;
    }

    private static Color ToMediaColor(System.Drawing.Color color) =>
        Color.FromArgb(color.A, color.R, color.G, color.B);

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
