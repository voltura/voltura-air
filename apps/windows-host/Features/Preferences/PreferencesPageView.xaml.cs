using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Preferences;

public partial class PreferencesPageView : WpfUserControl
{
    private readonly Expander[] _sections;
    private readonly PreferencesSearchRegistry _searchRegistry;
    private readonly Action<string?> _titleChanged;
    private readonly Action<Expander, StackPanel> _revealSection;
    private readonly Action<string> _searchQueryChanged;
    private bool _suppressNextSearchOpen;

    internal PreferencesPageView(
        string? sectionToOpen,
        string searchQuery,
        PreferencesSearchRegistry searchRegistry,
        Action<string?> titleChanged,
        Action<Expander, StackPanel> revealSection,
        Action<string> searchQueryChanged)
    {
        InitializeComponent();
        _searchRegistry = searchRegistry;
        _titleChanged = titleChanged;
        _revealSection = revealSection;
        _searchQueryChanged = searchQueryChanged;
        _sections =
        [
            ApplicationSection,
            AppearanceSection,
            TrackpadSection,
            RemoteSection,
            PresentationSection,
            AwakeSection,
            PermissionsSection,
            ScreenViewSection,
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
        SearchBox.Text = searchQuery;
    }

    internal ScrollViewer Scroller => PreferencesScroller;

    internal string SearchQuery => SearchBox.Text;

    internal WatermarkedTextBox SearchInput => SearchBox;

    internal Button ClearSearch => ClearSearchButton;

    internal Popup SearchPopup => SearchResultsPopup;

    internal ListBox SearchResults => SearchResultsList;

    internal TextBlock NoSearchResults => NoSearchResultsText;

    internal Task PendingSearchActivation { get; private set; } = Task.CompletedTask;

    internal string? ExpandedSectionTitle =>
        _sections.FirstOrDefault(section => section.IsExpanded)?.Header as string;

    internal Expander? FindSection(string? title) =>
        _sections.FirstOrDefault(section => string.Equals(section.Header as string, title, StringComparison.Ordinal));

    internal void CompleteSearchRegistration()
    {
        UpdateSearchResults(openPopup: false);
    }

    internal void ActivateSearchResult(PreferenceSearchResult result)
    {
        PendingSearchActivation = ActivateSearchResultAsync(result);
    }

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

    private void OnSearchTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _searchQueryChanged(SearchBox.Text);
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateSearchResults(openPopup: SearchBox.IsKeyboardFocusWithin);
    }

    private void OnSearchGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_suppressNextSearchOpen)
        {
            _suppressNextSearchOpen = false;
            return;
        }

        OpenSearchResults();
    }

    private void OnSearchPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (SearchBox.Text.Trim().Length == 0 || SearchResultsPopup.IsOpen)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(OpenSearchResults, DispatcherPriority.Input);
    }

    private void OnSearchPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Key == Key.Escape)
        {
            SearchResultsPopup.IsOpen = false;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key is not (Key.Down or Key.Up))
        {
            return;
        }

        OpenSearchResults();
        if (SearchResultsList.Items.Count == 0)
        {
            return;
        }

        SearchResultsList.SelectedIndex = eventArgs.Key == Key.Down
            ? 0
            : SearchResultsList.Items.Count - 1;
        _ = SearchResultsList.Focus();
        SearchResultsList.ScrollIntoView(SearchResultsList.SelectedItem);
        eventArgs.Handled = true;
    }

    private void OnResultsPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Key == Key.Escape)
        {
            SearchResultsPopup.IsOpen = false;
            _suppressNextSearchOpen = true;
            _ = SearchBox.Focus();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Enter &&
            SearchResultsList.SelectedItem is PreferenceSearchResult result)
        {
            ActivateSearchResult(result);
            eventArgs.Handled = true;
        }
    }

    private void OnResultsPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        _ = sender;
        if (ItemsControl.ContainerFromElement(
                SearchResultsList,
                eventArgs.OriginalSource as DependencyObject) is not ListBoxItem item ||
            item.DataContext is not PreferenceSearchResult result)
        {
            return;
        }

        SearchResultsList.SelectedItem = result;
        ActivateSearchResult(result);
        eventArgs.Handled = true;
    }

    private void OnClearSearchClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        SearchResultsPopup.IsOpen = false;
        SearchBox.Clear();
        _ = SearchBox.Focus();
    }

    private void OnSearchResultsOpened(object sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        SearchResultsBorder.Width = SearchChrome.ActualWidth;
    }

    private void OnSearchChromeSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (SearchResultsPopup.IsOpen)
        {
            SearchResultsBorder.Width = SearchChrome.ActualWidth;
        }
    }

    private void OpenSearchResults()
    {
        if (SearchBox.Text.Trim().Length == 0)
        {
            return;
        }

        UpdateSearchResults(openPopup: true);
    }

    private void UpdateSearchResults(bool openPopup)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            SearchResultsList.ItemsSource = null;
            SearchResultsList.SelectedIndex = -1;
            NoSearchResultsText.Visibility = Visibility.Collapsed;
            SearchResultsPopup.IsOpen = false;
            return;
        }

        var matches = _searchRegistry.Match(query);
        SearchResultsList.ItemsSource = matches;
        SearchResultsList.SelectedIndex = -1;
        SearchResultsList.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NoSearchResultsText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (openPopup)
        {
            SearchResultsPopup.IsOpen = true;
        }
    }

    private async Task ActivateSearchResultAsync(PreferenceSearchResult result)
    {
        SearchResultsPopup.IsOpen = false;

        var expanders = PreferencesSearchRegistry.FindContainingExpanders(result.Entry.RevealTarget).ToList();
        if (result.Entry.RevealTarget is Expander targetExpander)
        {
            expanders.Add(targetExpander);
        }

        foreach (var expander in expanders)
        {
            expander.SetCurrentValue(Expander.IsExpandedProperty, true);
        }

        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);

        PreferencesScrollCoordinator.RevealTarget(PreferencesScroller, result.Entry.RevealTarget);
        var focusTarget = FindFocusable(result.Entry.FocusTarget) ?? FindFallbackFocusable(expanders);
        if (focusTarget is not null)
        {
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(focusTarget), focusTarget);
            _ = focusTarget.Focus();
        }
    }

    private static UIElement? FindFallbackFocusable(List<Expander> expanders)
    {
        for (var index = expanders.Count - 1; index >= 0; index--)
        {
            if (FindFocusable(expanders[index]) is { } focusable)
            {
                return focusable;
            }
        }

        return null;
    }

    private static UIElement? FindFocusable(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindFocusable(VisualTreeHelper.GetChild(root, index)) is { } descendant)
            {
                return descendant;
            }
        }

        return root is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } focusable
            ? focusable
            : null;
    }
}
