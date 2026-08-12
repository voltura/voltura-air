using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DragCompletedEventArgs =
    System.Windows.Controls.Primitives.DragCompletedEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Thumb = System.Windows.Controls.Primitives.Thumb;
using Track = System.Windows.Controls.Primitives.Track;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void CustomScreenPaletteScrollsAndUsesCompactDisclosures()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window
            {
                Width = 1000,
                Height = 620
            };
            WpfTheme.Apply(owner);
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/PreferencesAccordion.Styles.xaml",
                    UriKind.Relative)
            });
            var page = new CustomScreensPageView(
                owner,
                new CustomScreenService(
                    new InMemoryCustomScreenStore(),
                    new FakeAppLaunchService()),
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var screenName = Assert.IsType<TextBox>(
                    page.FindName("ScreenNameInput"));
                Assert.Equal(
                    CustomScreenLimits.MaxScreenNameLength,
                    screenName.MaxLength);

                var scroller = FindVisualDescendants<ScrollViewer>(page)
                    .Single(candidate =>
                        candidate.Name == "ComponentPaletteScroller");
                Assert.Equal(
                    ScrollBarVisibility.Visible,
                    scroller.VerticalScrollBarVisibility);
                Assert.Equal(
                    ScrollBarVisibility.Disabled,
                    scroller.HorizontalScrollBarVisibility);
                var inactiveScrollBars = FindVisualDescendants<ScrollBar>(page)
                    .Where(scrollBar =>
                        scrollBar.Orientation == Orientation.Vertical &&
                        scrollBar.Maximum == 0)
                    .ToArray();
                Assert.NotEmpty(inactiveScrollBars);
                Assert.All(inactiveScrollBars, scrollBar =>
                {
                    scrollBar.ApplyTemplate();
                    var track = FindVisualDescendants<Track>(scrollBar)
                        .Single();
                    Assert.Equal(0, track.Opacity);
                });

                var availableComponentsExpander = Assert.IsType<Expander>(
                    page.FindName("AvailableComponentsExpander"));
                var layoutOptionsExpander = Assert.IsType<Expander>(
                    page.FindName("LayoutOptionsExpander"));
                var hiddenControlsExpander = Assert.IsType<Expander>(
                    page.FindName("HiddenControlsRoot"));
                var editingOptionsExpander = Assert.IsType<Expander>(
                    page.FindName("EditingOptionsExpander"));
                var expanders = new[]
                {
                    availableComponentsExpander,
                    layoutOptionsExpander,
                    hiddenControlsExpander,
                    editingOptionsExpander
                };
                Assert.All(expanders, expander =>
                {
                    Assert.Same(
                        page.FindResource("CustomScreenCompactPropertyGroupStyle"),
                        expander.Style);
                });
                Assert.All(
                    expanders.Where(expander =>
                        expander.Visibility == Visibility.Visible),
                    expander =>
                    {
                        var header =
                            FindVisualDescendants<ToggleButton>(expander)
                                .Single();
                        Assert.Same(
                            page.FindResource("TextBrush"),
                            header.Foreground);
                    });
                Assert.True(availableComponentsExpander.IsExpanded);
                Assert.All(
                    expanders.Where(expander =>
                        expander != availableComponentsExpander),
                    expander => Assert.False(expander.IsExpanded));
                Assert.Equal(
                    Visibility.Collapsed,
                    hiddenControlsExpander.Visibility);
                Assert.Equal(
                    9,
                    FindVisualDescendants<Button>(page).Count(button =>
                        AutomationProperties.GetName(button)
                            .StartsWith("Drag ", StringComparison.Ordinal) &&
                        AutomationProperties.GetName(button)
                            .EndsWith(" onto layout", StringComparison.Ordinal)));
                var paletteRows = new[]
                {
                    ("SectionPaletteItem", "SectionPaletteButton",
                        "SectionPaletteDragHandle"),
                    ("CollapsibleSectionPaletteItem",
                        "CollapsibleSectionPaletteButton",
                        "CollapsibleSectionPaletteDragHandle"),
                    ("ButtonPaletteItem", "ButtonPaletteButton",
                        "ButtonPaletteDragHandle"),
                    ("LaserPointerPaletteItem", "LaserPointerPaletteButton",
                        "LaserPointerPaletteDragHandle"),
                    ("VolumePaletteItem", "VolumePaletteButton",
                        "VolumePaletteDragHandle"),
                    ("TrackpadPaletteItem", "TrackpadPaletteButton",
                        "TrackpadPaletteDragHandle"),
                    ("CollapsibleTrackpadPaletteItem",
                        "CollapsibleTrackpadPaletteButton",
                        "CollapsibleTrackpadPaletteDragHandle"),
                    ("NavigationRingPaletteItem",
                        "NavigationRingPaletteButton",
                        "NavigationRingPaletteDragHandle")
                };
                foreach (var names in paletteRows)
                {
                    var row = Assert.IsType<Border>(
                        page.FindName(names.Item1));
                    var grid = Assert.IsType<Grid>(row.Child);
                    var addButton = Assert.IsType<Button>(
                        page.FindName(names.Item2));
                    var dragHandle = Assert.IsType<Button>(
                        page.FindName(names.Item3));

                    Assert.Equal(2, grid.ColumnDefinitions.Count);
                    Assert.Equal(0, Grid.GetColumn(addButton));
                    Assert.Equal(1, Grid.GetColumn(dragHandle));
                    var handlePosition = dragHandle.TranslatePoint(
                        new Point(),
                        row);
                    Assert.True(
                        handlePosition.X + dragHandle.ActualWidth <=
                            row.ActualWidth + 0.5);
                }

                Assert.Equal(
                    "+ Collapsible trackpad",
                    Assert.IsType<Button>(
                        page.FindName("CollapsibleTrackpadPaletteButton"))
                        .Content);
                Assert.IsType<Button>(page.FindName("TrackpadPaletteButton"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                FindVisualDescendants<Expander>(page)
                    .Single(expander => Equals(expander.Header, "Trackpad"))
                    .IsExpanded = true;
                owner.UpdateLayout();
                Assert.Contains(
                    FindVisualDescendants<CheckBox>(page),
                    checkBox => Equals(
                        checkBox.Content,
                        "Show fullscreen control"));
                var enableGyro = FindVisualDescendants<CheckBox>(page)
                    .Single(checkBox => Equals(checkBox.Content, "Enable Gyro"));
                Assert.False(enableGyro.IsChecked);
                enableGyro.IsChecked = true;
                enableGyro.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                Assert.Contains(
                    FindVisualDescendants<Border>(page),
                    border => AutomationProperties.GetName(border) ==
                        "Trackpad movement selector");
                Assert.DoesNotContain(
                    FindVisualDescendants<TextBlock>(page),
                    text => Equals(text.Text, "Trackpad height"));
                Assert.Equal(
                    CustomScreenLimits.MaxSectionNameLength,
                    FindVisualDescendants<TextBox>(page)
                        .Single(textBox =>
                            AutomationProperties.GetName(textBox) == "Name")
                        .MaxLength);

                Assert.IsType<Button>(page.FindName("LaserPointerPaletteButton"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                Assert.DoesNotContain(
                    FindVisualDescendants<ComboBox>(page),
                    combo => AutomationProperties.GetName(combo) == "Action type");
                var color = FindVisualDescendants<ComboBox>(page)
                    .Single(combo => AutomationProperties.GetName(combo) == "Color");
                Assert.Equal("Default", Assert.IsType<ComboBoxItem>(color.SelectedItem).Content);
                var repeat = FindVisualDescendants<CheckBox>(page)
                    .Single(checkBox => Equals(checkBox.Content, "Repeat while held"));
                Assert.False(repeat.IsEnabled);

                FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Collapse all component sections")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.All(expanders, expander =>
                    Assert.False(expander.IsExpanded));

                FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Expand all component sections")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.All(expanders, expander =>
                    Assert.True(expander.IsExpanded));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void CustomScreenEditorSidePanelsResizeScaleAndPersist()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window
            {
                Width = 1500,
                Height = 720
            };
            WpfTheme.Apply(owner);
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/PreferencesAccordion.Styles.xaml",
                    UriKind.Relative)
            });
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            var pairingManager = new PairingManager(pairingStore.Store);
            var page = new CustomScreensPageView(
                owner,
                service,
                pairingManager);
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var componentColumn = Assert.IsType<ColumnDefinition>(
                    page.FindName("ComponentPaletteColumn"));
                var previewColumn = Assert.IsType<ColumnDefinition>(
                    page.FindName("PreviewColumn"));
                var propertiesColumn = Assert.IsType<ColumnDefinition>(
                    page.FindName("PropertiesPanelColumn"));
                Assert.Equal(210, componentColumn.MinWidth);
                Assert.Equal(290, propertiesColumn.MinWidth);
                Assert.Equal(240, previewColumn.MinWidth);
                Assert.Equal(210, componentColumn.ActualWidth);
                Assert.Equal(290, propertiesColumn.ActualWidth);

                var collapsibleButton = Assert.IsType<Button>(
                    page.FindName("CollapsibleSectionPaletteButton"));
                var wrappedLabel =
                    FindVisualDescendants<TextBlock>(collapsibleButton)
                        .Single(text =>
                            text.Text == "+ Collapsible panel");
                Assert.Equal(TextWrapping.Wrap, wrappedLabel.TextWrapping);
                Assert.True(
                    wrappedLabel.ActualHeight >
                    wrappedLabel.FontSize * 1.5);

                var splitters = new[]
                {
                    Assert.IsType<GridSplitter>(
                        page.FindName("ComponentPaletteSplitter")),
                    Assert.IsType<GridSplitter>(
                        page.FindName("PropertiesPanelSplitter"))
                };
                Assert.All(splitters, splitter =>
                {
                    Assert.Same(
                        page.FindResource("CustomScreenColumnSplitterStyle"),
                        splitter.Style);
                    Assert.Equal(
                        GridResizeBehavior.PreviousAndNext,
                        splitter.ResizeBehavior);
                    Assert.Equal(
                        GridResizeDirection.Columns,
                        splitter.ResizeDirection);
                    Assert.False(splitter.ShowsPreview);
                    Assert.Equal(
                        System.Windows.Input.Cursors.SizeWE,
                        splitter.Cursor);
                    splitter.ApplyTemplate();
                    owner.UpdateLayout();
                    var grip = FindVisualDescendants<Border>(splitter)
                        .Single(border =>
                            border.Width == 3 &&
                            border.Height == 48);
                    Assert.Same(
                        page.FindResource("BorderBrush"),
                        grip.Background);
                });

                var previewScaler = Assert.IsType<Viewbox>(
                    page.FindName("DevicePreviewScaler"));
                Assert.Equal(
                    System.Windows.Media.Stretch.Uniform,
                    previewScaler.Stretch);
                var initialPreviewWidth = previewColumn.ActualWidth;

                componentColumn.Width = new GridLength(300);
                propertiesColumn.Width = new GridLength(380);
                owner.UpdateLayout();
                splitters[0].RaiseEvent(new DragCompletedEventArgs(
                    0,
                    0,
                    canceled: false)
                {
                    RoutedEvent = Thumb.DragCompletedEvent
                });

                Assert.True(previewColumn.ActualWidth < initialPreviewWidth);
                Assert.Equal(
                    (300d, 380d),
                    CustomScreenEditorSettings.PanelWidths());

                var restoredPage = new CustomScreensPageView(
                    owner,
                    service,
                    pairingManager);
                Assert.Equal(
                    300,
                    Assert.IsType<ColumnDefinition>(
                        restoredPage.FindName("ComponentPaletteColumn"))
                        .Width.Value);
                Assert.Equal(
                    380,
                    Assert.IsType<ColumnDefinition>(
                        restoredPage.FindName("PropertiesPanelColumn"))
                        .Width.Value);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void HiddenControlsCanBeRestoredFromTheActiveOrientation()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var owner = new Window();
            WpfTheme.Apply(owner);
            var page = new CustomScreensPageView(
                owner,
                new CustomScreenService(
                    new InMemoryCustomScreenStore(),
                    new FakeAppLaunchService()),
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var orientationLayouts = Assert.IsType<CheckBox>(
                    page.FindName("OrientationLayoutsCheckBox"));
                orientationLayouts.IsChecked = true;
                owner.UpdateLayout();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "+ Button"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                var newButton = FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Select button New button");
                var menu = Assert.IsType<ContextMenu>(newButton.ContextMenu);
                Assert.Equal(
                    ["Hide in Portrait", "Delete everywhere"],
                    menu.Items.OfType<MenuItem>().Select(item => item.Header));

                var orientation = FindVisualDescendants<ComboBox>(page)
                    .Single(combo =>
                        AutomationProperties.GetName(combo) ==
                            "Preview orientation");
                orientation.SelectedItem = orientation.Items
                    .OfType<ComboBoxItem>()
                    .Single(item => Equals(item.Tag, "landscape"));
                owner.UpdateLayout();

                var hiddenList = Assert.IsType<VolturaAir.Host.Ui.SpacingStackPanel>(
                    page.FindName("HiddenControlsList"));
                var show = FindVisualDescendants<Button>(hiddenList)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Show Button · New button from panel Controls in landscape");
                Assert.Contains(
                    FindVisualDescendants<TextBlock>(hiddenList),
                    text => text.Text == "Panel · Controls");
                show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                Assert.Contains(
                    FindVisualDescendants<Button>(page),
                    button => AutomationProperties.GetName(button) ==
                        "Select button New button");
                var visibility = FindVisualDescendants<Expander>(page)
                    .Single(expander => AutomationProperties.GetName(expander) ==
                        "Visibility property group");
                visibility.IsExpanded = true;
                owner.UpdateLayout();
                Assert.Contains(
                    FindVisualDescendants<CheckBox>(visibility),
                    checkBox => Equals(checkBox.Content, "Show in Landscape") &&
                        checkBox.IsChecked == true);
            }
            finally
            {
                owner.Close();
            }
        });
    }
}
