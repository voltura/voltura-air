using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Diagnostics;

public partial class DiagnosticsPageView : WpfUserControl
{
    internal DiagnosticsPageView(Func<System.Windows.UIElement> showApplicationLog, Func<System.Windows.UIElement> showSystemDetails)
    {
        InitializeComponent();
        WireSegmentPair(ApplicationLogButton, SystemDetailsButton);
        ApplicationLogButton.Click += (_, _) => ViewContent.Content = showApplicationLog();
        SystemDetailsButton.Click += (_, _) => ViewContent.Content = showSystemDetails();
        ViewContent.Content = showApplicationLog();
    }

    private static void WireSegmentPair(WpfToggleButton first, WpfToggleButton second)
    {
        first.Click += (_, _) => SetSelected(first, second);
        second.Click += (_, _) => SetSelected(second, first);
    }

    private static void SetSelected(WpfToggleButton selected, WpfToggleButton other)
    {
        selected.IsChecked = true;
        other.IsChecked = false;
    }
}
