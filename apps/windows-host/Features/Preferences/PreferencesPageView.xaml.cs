using System.Windows.Controls;
using System.Windows.Threading;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Preferences;

public partial class PreferencesPageView : WpfUserControl
{
    private readonly Expander[] _sections;
    private readonly Action<string?> _titleChanged;
    private readonly Action<Expander, StackPanel> _revealSection;

    internal PreferencesPageView(
        string? sectionToOpen,
        Action<string?> titleChanged,
        Action<Expander, StackPanel> revealSection)
    {
        InitializeComponent();
        _titleChanged = titleChanged;
        _revealSection = revealSection;
        _sections =
        [
            ApplicationSection,
            AppearanceSection,
            TrackpadSection,
            RemoteSection,
            AwakeSection,
            PermissionsSection,
            TextDestinationSection,
            AppLaunchSection,
            CustomPointerSection,
            DeveloperSection
        ];

        System.Windows.Input.KeyboardNavigation.SetIsTabStop(PreferencesScroller, false);
        foreach (var section in _sections)
        {
            section.Expanded += OnSectionExpanded;
            section.Collapsed += OnSectionCollapsed;
        }

        FindSection(sectionToOpen)?.SetCurrentValue(Expander.IsExpandedProperty, true);
    }

    internal ScrollViewer Scroller => PreferencesScroller;

    internal string? ExpandedSectionTitle =>
        _sections.FirstOrDefault(section => section.IsExpanded)?.Header as string;

    internal Expander? FindSection(string? title) =>
        _sections.FirstOrDefault(section => string.Equals(section.Header as string, title, StringComparison.Ordinal));

    private void OnSectionExpanded(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        var expanded = (Expander)sender;
        foreach (var section in _sections)
        {
            if (!ReferenceEquals(section, expanded))
            {
                section.IsExpanded = false;
            }
        }

        _titleChanged(expanded.Header as string);
        if (expanded.Content is StackPanel content)
        {
            _ = expanded.Dispatcher.InvokeAsync(
                () => _revealSection(expanded, content),
                DispatcherPriority.Loaded);
        }

        eventArgs.Handled = true;
    }

    private void OnSectionCollapsed(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        if (_sections.All(section => !section.IsExpanded))
        {
            _titleChanged(null);
        }

        eventArgs.Handled = true;
    }
}
