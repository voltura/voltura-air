using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using VolturaAir.Host.Ui;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void HighContrastThemeUsesWindowsSystemColors()
    {
        RunOnStaThread(() =>
        {
            var window = new Window();

            WpfTheme.Apply(window, highContrast: true);

            Assert.Same(SystemColors.WindowBrush, window.Resources["WindowBrush"]);
            Assert.Same(SystemColors.ControlTextBrush, window.Resources["TextBrush"]);
            Assert.Same(SystemColors.HighlightBrush, window.Resources["AccentBrush"]);
            Assert.Same(SystemColors.HighlightTextBrush, window.Resources["AccentTextBrush"]);
            Assert.Same(SystemColors.WindowTextBrush, window.Resources["FocusBrush"]);
        });
    }

    [Fact]
    public void SharedControlsMeetTheTouchTargetMinimum()
    {
        RunOnStaThread(() =>
        {
            var window = new Window();
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            var button = new Button();
            var iconButton = new Button { Style = (Style)window.Resources["CompactIconButtonStyle"] };
            var segment = new ToggleButton { Style = (Style)window.Resources["SegmentButtonStyle"] };
            var input = new TextBox();
            var filterInput = new TextBox { Style = (Style)window.Resources["FilterTextBoxStyle"] };
            var comboBox = new ComboBox { Style = (Style)window.Resources["ModernComboBoxStyle"] };
            window.Content = new StackPanel { Children = { button, iconButton, segment, input, filterInput, comboBox } };

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(button.ActualHeight >= 40);
                Assert.True(iconButton.ActualHeight >= 40);
                Assert.True(segment.ActualHeight >= 40);
                Assert.True(input.ActualHeight >= 40);
                Assert.True(filterInput.ActualHeight >= 40);
                Assert.True(comboBox.ActualHeight >= 40);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ModernHostListRowsLeaveSelectionChromeToTheirCards()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            };
            var itemStyle = Assert.IsType<Style>(resources["ModernListBoxItemStyle"]);
            var focusSetter = Assert.Single(
                itemStyle.Setters.OfType<Setter>(),
                setter => setter.Property == FrameworkElement.FocusVisualStyleProperty);
            Assert.Null(focusSetter.Value);
            Assert.Empty(itemStyle.Triggers);
        });
    }

    [Fact]
    public void SelectableListCardsUseSingleThemedSelectionBorder()
    {
        RunOnStaThread(() =>
        {
            var window = new Window();
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            var card = new Border
            {
                Style = (Style)window.Resources["SelectableListCardStyle"]
            };
            var item = new ListBoxItem
            {
                Content = card,
                IsSelected = true
            };
            window.Content = item;

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Same(window.Resources["AccentBrush"], card.BorderBrush);
                Assert.Equal(new Thickness(1), card.BorderThickness);

                item.IsSelected = false;
                window.UpdateLayout();

                Assert.Same(window.Resources["BorderBrush"], card.BorderBrush);

                Assert.True(item.Focus());
                window.UpdateLayout();

                Assert.Same(window.Resources["FocusBrush"], card.BorderBrush);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SharedInputStylesUseExistingChromeForKeyboardFocus()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            };

            AssertSingleBorderFocus(
                Assert.IsType<Style>(resources[typeof(Button)]),
                UIElement.IsKeyboardFocusedProperty);
            AssertSingleBorderFocus(
                Assert.IsType<Style>(resources[typeof(CheckBox)]),
                UIElement.IsKeyboardFocusedProperty);
            AssertSingleBorderFocus(
                Assert.IsType<Style>(resources["FilterTextBoxStyle"]),
                UIElement.IsKeyboardFocusedProperty);
            AssertSingleBorderFocus(
                Assert.IsType<Style>(resources["ModernComboBoxStyle"]),
                UIElement.IsKeyboardFocusWithinProperty);
        });
    }

    [Fact]
    public void PillBadgeStyleOwnsCylinderGeometryAndSemanticTones()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            };
            var style = Assert.IsType<Style>(resources["PillBadgeStyle"]);
            var height = Assert.Single(
                style.Setters.OfType<Setter>(),
                setter => setter.Property == FrameworkElement.HeightProperty);
            var padding = Assert.Single(
                style.Setters.OfType<Setter>(),
                setter => setter.Property == Control.PaddingProperty);
            var templateSetter = Assert.Single(
                style.Setters.OfType<Setter>(),
                setter => setter.Property == Control.TemplateProperty);
            var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
            var chrome = Assert.IsType<Border>(template.LoadContent());
            chrome.Resources["RadiusLarge"] = new CornerRadius(12);
            var tones = style.Triggers
                .OfType<Trigger>()
                .Where(trigger => trigger.Property == PillBadge.ToneProperty)
                .Select(trigger => Assert.IsType<PillBadgeTone>(trigger.Value))
                .ToHashSet();

            Assert.Equal(24d, height.Value);
            Assert.Equal(new Thickness(10, 0, 10, 0), padding.Value);
            Assert.Equal(new CornerRadius(12), chrome.CornerRadius);
            Assert.Equal(5, tones.Count);
            Assert.Contains(PillBadgeTone.AccentOutline, tones);
            Assert.Contains(PillBadgeTone.DangerOutline, tones);
            Assert.Contains(PillBadgeTone.Accent, tones);
            Assert.Contains(PillBadgeTone.Success, tones);
            Assert.Contains(PillBadgeTone.Danger, tones);
        });
    }

    private static void AssertSingleBorderFocus(Style style, DependencyProperty focusProperty)
    {
        var focusVisualSetter = Assert.Single(
            style.Setters.OfType<Setter>(),
            setter => setter.Property == FrameworkElement.FocusVisualStyleProperty);
        Assert.Null(focusVisualSetter.Value);

        var templateSetter = Assert.Single(
            style.Setters.OfType<Setter>(),
            setter => setter.Property == Control.TemplateProperty);
        var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
        var focusTrigger = Assert.Single(
            template.Triggers.OfType<Trigger>(),
            trigger => trigger.Property == focusProperty && Equals(trigger.Value, true));

        Assert.Contains(
            focusTrigger.Setters.OfType<Setter>(),
            setter => setter.TargetName == "Chrome" && setter.Property == Border.BorderBrushProperty);
        Assert.DoesNotContain(
            focusTrigger.Setters.OfType<Setter>(),
            setter => setter.Property == Border.BorderThicknessProperty);
    }

    [Fact]
    public void EventFilterMenuOwnsItsEntireThemedPopupSurface()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            };
            var menuStyle = Assert.IsType<Style>(resources["EventMultiSelectContextMenuStyle"]);
            var templateSetter = Assert.Single(
                menuStyle.Setters.OfType<Setter>(),
                setter => setter.Property == Control.TemplateProperty);
            var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
            var menuChrome = Assert.IsType<Border>(template.LoadContent());
            var borderSetter = Assert.Single(
                menuStyle.Setters.OfType<Setter>(),
                setter => setter.Property == Control.BorderThicknessProperty);

            Assert.Equal(new CornerRadius(9), menuChrome.CornerRadius);
            Assert.Equal(new Thickness(1), borderSetter.Value);
            Assert.IsType<ScrollViewer>(menuChrome.Child);
            var minWidthBinding = BindingOperations.GetBinding(menuChrome, FrameworkElement.MinWidthProperty);
            Assert.NotNull(minWidthBinding);
            Assert.Equal("PlacementTarget.ActualWidth", minWidthBinding.Path.Path);

            var filter = new EventMultiSelectFilter(("Host actions", "host_action"));
            filter.Resources.MergedDictionaries.Add(resources);
            var button = Assert.IsType<Button>(filter.Content);
            var menu = button.ContextMenu;
            Assert.NotNull(menu);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

            Assert.Same(menuStyle, menu.Style);
            Assert.All(menu.Items.Cast<MenuItem>(), item => Assert.Same(resources["EventMultiSelectMenuItemStyle"], item.Style));
        });
    }

    [Fact]
    public void MainWindowPagesKeepCompositionSpacingAndModernListsVirtualized()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                AssertModernListBehavior(window);
                foreach (var page in Enum.GetValues<HostPage>())
                {
                    window.ShowPage(page);
                    window.UpdateLayout();

                    Assert.All(
                        FindWpfDescendants<Control>(window.PageContent),
                        control => Assert.Equal(new Thickness(), control.Margin));
                }
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    private static void AssertModernListBehavior(MainWindow window)
    {
        var list = new ListBox
        {
            Style = (Style)window.Resources["ModernListBoxStyle"],
            ItemContainerStyle = (Style)window.Resources["ModernListBoxItemStyle"]
        };

        Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        Assert.True(list.IsTabStop);
        Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Once, System.Windows.Input.KeyboardNavigation.GetTabNavigation(list));
        Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Contained, System.Windows.Input.KeyboardNavigation.GetDirectionalNavigation(list));
        var itemsHost = Assert.IsType<VirtualizingStackPanel>(list.ItemsPanel.LoadContent());
        Assert.IsAssignableFrom<IScrollInfo>(itemsHost);
    }

    [Fact]
    public void SpacingIsAppliedOnceBetweenVisibleChildren()
    {
        RunOnStaThread(() =>
        {
            var first = new Border { Height = 20 };
            var collapsed = new Border { Height = 100, Visibility = Visibility.Collapsed };
            var last = new Border { Height = 30 };
            var panel = new SpacingStackPanel { Spacing = 12 };
            panel.Children.Add(first);
            panel.Children.Add(collapsed);
            panel.Children.Add(last);

            panel.Measure(new WpfSize(100, double.PositiveInfinity));
            panel.Arrange(new WpfRect(0, 0, 100, panel.DesiredSize.Height));

            Assert.Equal(62, panel.DesiredSize.Height);
            Assert.Equal(12, last.TranslatePoint(new WpfPoint(), panel).Y - first.ActualHeight);
        });
    }

    [Fact]
    public void WrapSpacingIsAppliedBetweenVisibleItemsAndRows()
    {
        RunOnStaThread(() =>
        {
            var first = new Border { Width = 40, Height = 20 };
            var collapsed = new Border { Width = 100, Height = 100, Visibility = Visibility.Collapsed };
            var second = new Border { Width = 40, Height = 20 };
            var third = new Border { Width = 40, Height = 20 };
            var panel = new SpacingWrapPanel { HorizontalSpacing = 8, VerticalSpacing = 12 };
            panel.Children.Add(first);
            panel.Children.Add(collapsed);
            panel.Children.Add(second);
            panel.Children.Add(third);

            panel.Measure(new WpfSize(90, double.PositiveInfinity));
            panel.Arrange(new WpfRect(0, 0, 90, panel.DesiredSize.Height));

            Assert.Equal(52, panel.DesiredSize.Height);
            Assert.Equal(8, second.TranslatePoint(new WpfPoint(), panel).X - first.ActualWidth);
            Assert.Equal(12, third.TranslatePoint(new WpfPoint(), panel).Y - first.ActualHeight);
        });
    }

    [Fact]
    public void SettingsCheckBoxFactoryDefaultsToContentWidthAndSupportsSizingOverrides()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            };
            var visuals = new HostVisualFactory(resources);

            var intrinsic = visuals.CreateCheckBox("Intrinsic", false);
            var minimum = visuals.CreateCheckBox("Minimum", false, minimumWidth: 240);
            var fill = visuals.CreateCheckBox("Fill", false, fillAvailableWidth: true);
            var information = visuals.CreateCheckBox("Information", false, showInformation: static () => { });

            Assert.Equal(HorizontalAlignment.Left, intrinsic.HorizontalAlignment);
            Assert.Equal(0d, intrinsic.MinWidth);
            Assert.Equal(240d, minimum.MinWidth);
            Assert.Equal(HorizontalAlignment.Stretch, fill.HorizontalAlignment);
            Assert.True(information.HasInformation);
            Assert.Equal("More information about Information", information.InformationAccessibleName);
        });
    }

}
