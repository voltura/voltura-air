using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Diagnostics;

public partial class DiagnosticsPageView : WpfUserControl
{
    internal DiagnosticsPageView(
        Func<System.Windows.UIElement> showApplicationLog,
        Func<System.Windows.UIElement> showSystemDetails,
        Func<System.Windows.UIElement> showUsageStatistics)
    {
        InitializeComponent();
        var segments = new[] { ApplicationLogButton, SystemDetailsButton, UsageStatisticsButton };
        WireSegments(segments);
        ApplicationLogButton.Click += (_, _) => ViewContent.Content = showApplicationLog();
        SystemDetailsButton.Click += (_, _) => ViewContent.Content = showSystemDetails();
        UsageStatisticsButton.Click += (_, _) => ViewContent.Content = showUsageStatistics();
        ViewContent.Content = showApplicationLog();
    }

    private static void WireSegments(IReadOnlyList<WpfToggleButton> segments)
    {
        foreach (var segment in segments)
        {
            segment.Click += (_, _) =>
            {
                foreach (var candidate in segments)
                {
                    candidate.IsChecked = ReferenceEquals(candidate, segment);
                }
            };
        }
    }
}
