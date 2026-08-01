using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void CustomScreensNavigationAndEmptyUndoRemainSafe()
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
            AppDeveloperSettings.SetEnableAlphaFeatures(true);
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);

            try
            {
                window.Show();
                window.ShowPage(HostPage.CustomScreens);
                window.UpdateLayout();

                Assert.IsType<CustomScreensPageView>(window.PageContent.Content);
                var undo = FindVisualDescendants<Button>(window)
                    .Single(button => Equals(button.Content, "Undo"));
                Assert.False(undo.IsEnabled);
                undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsType<CustomScreensPageView>(window.PageContent.Content);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void CustomScreenScreenshotEditorIsMaximized()
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
            AppDeveloperSettings.SetEnableAlphaFeatures(true);
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);

            try
            {
                window.ShowCustomScreenEditorForScreenshot();

                Assert.Equal(WindowState.Maximized, window.WindowState);
                Assert.IsType<CustomScreensPageView>(window.PageContent.Content);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void CustomScreenRowsShowAndRetainTheActiveAddTarget()
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
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var row = FindRow(page, 2);
                var blankRowSurface =
                    FindVisualDescendants<CustomScreenButtonFlowPanel>(row)
                    .Single(panel => Equals(panel.Tag, 2));
                blankRowSurface.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseDownEvent,
                    Source = blankRowSurface
                });
                owner.UpdateLayout();

                row = FindRow(page, 2);
                Assert.Same(owner.FindResource("AccentBrush"), row.BorderBrush);
                Assert.Contains(
                    FindVisualDescendants<TextBlock>(page),
                    text => text.Text.Contains("Row 2 target", StringComparison.Ordinal));

                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "+ Button"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var saved = Assert.Single(service.GetAll());
                Assert.Equal(2, saved.Sections[0].Buttons[^1].Row);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void GeneratedButtonPropertiesOpenAndHeaderCanToggleAllGroups()
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
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "+ Button"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var groups = FindVisualDescendants<Expander>(page)
                    .Where(group => AutomationProperties.GetName(group)
                        .EndsWith(" property group", StringComparison.Ordinal))
                    .ToArray();
                Assert.True(groups.Single(group =>
                    AutomationProperties.GetName(group) ==
                        "Name property group").IsExpanded);
                Assert.True(groups.Single(group =>
                    AutomationProperties.GetName(group) ==
                        "Label property group").IsExpanded);

                FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Collapse all properties")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.All(groups, group => Assert.False(group.IsExpanded));

                FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button) ==
                        "Expand all properties")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.All(groups, group => Assert.True(group.IsExpanded));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void DeleteAndHideConfirmationsAreIndependentAndSynchronized()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var pairingStore = new TempPairingStore();
            var previousDeletes = CustomScreenEditorSettings.ConfirmDeletes();
            var previousHides = CustomScreenEditorSettings.ConfirmHides();
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
                CustomScreenEditorSettings.SetConfirmDeletes(true);
                CustomScreenEditorSettings.SetConfirmHides(true);
                owner.Show();

                var libraryDelete = Assert.IsType<CheckBox>(
                    page.FindName("LibraryConfirmDeletesCheckBox"));
                var libraryHide = Assert.IsType<CheckBox>(
                    page.FindName("LibraryConfirmHidesCheckBox"));
                var editorDelete = Assert.IsType<CheckBox>(
                    page.FindName("EditorConfirmDeletesCheckBox"));
                var editorHide = Assert.IsType<CheckBox>(
                    page.FindName("EditorConfirmHidesCheckBox"));
                Assert.Equal("Confirm on delete", libraryDelete.Content);
                Assert.Equal("Confirm on hide", libraryHide.Content);

                libraryDelete.IsChecked = false;
                Assert.False(CustomScreenEditorSettings.ConfirmDeletes());
                Assert.True(CustomScreenEditorSettings.ConfirmHides());
                Assert.False(editorDelete.IsChecked);
                Assert.True(editorHide.IsChecked);

                libraryHide.IsChecked = false;
                Assert.False(CustomScreenEditorSettings.ConfirmHides());
                Assert.False(editorHide.IsChecked);
            }
            finally
            {
                CustomScreenEditorSettings.SetConfirmDeletes(previousDeletes);
                CustomScreenEditorSettings.SetConfirmHides(previousHides);
                owner.Close();
            }
        });
    }

    [Fact]
    public void PreviewControlsUseThemedContextMenuDelete()
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
            owner.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/VolturaAir.Host;component/MainWindow.Styles.xaml",
                    UriKind.Relative)
            });
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;
            var previousSetting = CustomScreenEditorSettings.ConfirmDeletes();

            try
            {
                CustomScreenEditorSettings.SetConfirmDeletes(false);
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var previewButtons = FindVisualDescendants<Button>(page)
                    .Where(button => AutomationProperties.GetName(button)
                        .StartsWith("Select button ", StringComparison.Ordinal))
                    .ToArray();
                var previewButton = previewButtons[0];
                var menu = Assert.IsType<ContextMenu>(previewButton.ContextMenu);
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                Assert.Same(
                    page.FindResource("CustomScreenComponentContextMenuStyle"),
                    menu.Style);
                Assert.Equal(128, menu.MinWidth);
                Assert.Equal(220, menu.MaxWidth);

                var deleteItem = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal("Delete", deleteItem.Header);
                Assert.Same(
                    owner.FindResource("EventMultiSelectMenuItemStyle"),
                    deleteItem.Style);
                deleteItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                owner.UpdateLayout();

                Assert.Equal(
                    previewButtons.Length - 1,
                    FindVisualDescendants<Button>(page).Count(button =>
                        AutomationProperties.GetName(button)
                            .StartsWith("Select button ", StringComparison.Ordinal)));
            }
            finally
            {
                CustomScreenEditorSettings.SetConfirmDeletes(previousSetting);
                owner.Close();
            }
        });
    }

    [Fact]
    public void CustomScreenCollapsiblePanelRequiresHeaderAndKeepsPanelProperties()
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
            var service = new CustomScreenService(
                new InMemoryCustomScreenStore(),
                new FakeAppLaunchService());
            var page = new CustomScreensPageView(
                owner,
                service,
                new PairingManager(pairingStore.Store));
            owner.Content = page;

            try
            {
                owner.Show();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "New screen"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "+ Collapsible panel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();

                var propertyGroups = FindVisualDescendants<Expander>(page)
                    .Where(expander => AutomationProperties.GetName(expander)
                        .EndsWith(" property group", StringComparison.Ordinal))
                    .ToArray();
                Assert.Contains(propertyGroups, group =>
                    AutomationProperties.GetName(group) == "Name property group");
                Assert.Contains(propertyGroups, group =>
                    AutomationProperties.GetName(group) == "Header property group");
                Assert.Contains(propertyGroups, group =>
                    AutomationProperties.GetName(group) == "Layout property group" &&
                    group.IsExpanded);
                Assert.Contains(propertyGroups, group =>
                    AutomationProperties.GetName(group) == "Buttons property group");
                propertyGroups.Single(group =>
                    AutomationProperties.GetName(group) ==
                        "Header property group").IsExpanded = true;
                propertyGroups.Single(group =>
                    AutomationProperties.GetName(group) ==
                        "Buttons property group").IsExpanded = true;
                owner.UpdateLayout();

                var labels = FindVisualDescendants<TextBlock>(page)
                    .Select(text => text.Text)
                    .ToArray();
                Assert.Contains("Width", labels);
                Assert.Contains("Height", labels);
                Assert.Contains("Button placement", labels);
                Assert.Contains("Button rows", labels);
                Assert.Contains(
                    FindVisualDescendants<CheckBox>(page),
                    checkBox => Equals(checkBox.Content, "Expanded by default") &&
                        checkBox.IsChecked == true);
                Assert.DoesNotContain(
                    FindVisualDescendants<CheckBox>(page),
                    checkBox => Equals(checkBox.Content, "Show header"));

                var collapse = FindVisualDescendants<Button>(page)
                    .Single(button => AutomationProperties.GetName(button)
                        .StartsWith("Collapse panel Collapsible panel",
                            StringComparison.Ordinal));
                collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                owner.UpdateLayout();
                Assert.Contains(
                    FindVisualDescendants<Button>(page),
                    button => AutomationProperties.GetName(button)
                        .StartsWith("Expand panel Collapsible panel",
                            StringComparison.Ordinal));

                FindVisualDescendants<Button>(page)
                    .Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var collapsible = Assert.Single(service.GetAll())
                    .Sections[^1];
                Assert.Equal("collapsible", collapsible.Kind);
                Assert.True(collapsible.ShowHeader);
                Assert.False(collapsible.InitiallyExpanded);
            }
            finally
            {
                owner.Close();
            }
        });
    }

}
