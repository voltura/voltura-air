using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void NestedPreferenceAccordionsUseSharedSpacingStyle()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        using var settingsScope = HostSettingsRegistry.BeginIsolatedScope();
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
                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();

                var application = FindWpfDescendants<Expander>(window)
                    .Single(section => string.Equals(section.Header as string, "Application", StringComparison.Ordinal));
                var applicationLogDetails = FindWpfDescendants<Expander>(application)
                    .Single(section => string.Equals(section.Header as string, "More about application logs", StringComparison.Ordinal));
                var globalPermissions = FindWpfDescendants<Expander>(window)
                    .Single(section => string.Equals(section.Header as string, "Global permissions", StringComparison.Ordinal));
                var globalPermissionDetails = FindWpfDescendants<Expander>(globalPermissions)
                    .Single(section => string.Equals(section.Header as string, "More about global permissions", StringComparison.Ordinal));
                var textDestination = FindWpfDescendants<Expander>(window)
                    .Single(section => string.Equals(section.Header as string, "Text destination", StringComparison.Ordinal));
                textDestination.IsExpanded = true;
                var mode = FindWpfDescendants<ComboBox>(textDestination)
                    .First(comboBox => comboBox.Items.OfType<ComboBoxItem>().Any(item => item.Tag is TextDestinationMode.Managed));
                mode.SelectedItem = mode.Items.OfType<ComboBoxItem>()
                    .Single(item => item.Tag is TextDestinationMode.Managed);
                SettleLayout(window);
                var details = FindWpfDescendants<Expander>(textDestination)
                    .Single(section => string.Equals(section.Header as string, "More about text destinations", StringComparison.Ordinal));
                var appLaunch = FindWpfDescendants<Expander>(window)
                    .Single(section => string.Equals(section.Header as string, "Application launch buttons", StringComparison.Ordinal));
                var appLaunchDetails = FindWpfDescendants<Expander>(appLaunch)
                    .Single(section => string.Equals(section.Header as string, "More about app-launch buttons", StringComparison.Ordinal));
                var developerTools = FindWpfDescendants<Expander>(window)
                    .Single(section => string.Equals(section.Header as string, "Developer tools", StringComparison.Ordinal));
                var windowsLocking = FindWpfDescendants<Expander>(developerTools)
                    .Single(section => string.Equals(section.Header as string, "Windows locking", StringComparison.Ordinal));
                var nestedStyle = window.Resources["PreferencesNestedAccordionStyle"];
                var expectedGap = (double)window.Resources["SpaceMd"];

                Assert.False(applicationLogDetails.IsExpanded);
                Assert.Same(nestedStyle, applicationLogDetails.Style);
                Assert.False(globalPermissionDetails.IsExpanded);
                Assert.Same(nestedStyle, globalPermissionDetails.Style);
                Assert.False(details.IsExpanded);
                Assert.Same(nestedStyle, details.Style);
                Assert.Same(nestedStyle, appLaunchDetails.Style);
                Assert.Same(nestedStyle, windowsLocking.Style);
                Assert.Equal(new Thickness(), details.Margin);
                Assert.Equal(expectedGap, GetGapBefore(details), 5);
                application.IsExpanded = true;
                SettleLayout(window);
                Assert.Equal(expectedGap, GetGapBefore(applicationLogDetails), 5);
                globalPermissions.IsExpanded = true;
                SettleLayout(window);
                Assert.Equal(expectedGap, GetGapBefore(globalPermissionDetails), 5);
                appLaunch.IsExpanded = true;
                SettleLayout(window);
                Assert.Equal(expectedGap, GetGapBefore(appLaunchDetails), 5);
                developerTools.IsExpanded = true;
                SettleLayout(window);
                Assert.Equal(expectedGap, GetGapBefore(windowsLocking), 5);
                Assert.DoesNotContain(
                    FindWpfDescendants<Button>(application),
                    button => string.Equals(button.Content?.ToString(), "More about application logs", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    FindWpfDescendants<Button>(textDestination),
                    button => string.Equals(button.Content?.ToString(), "More about text destinations", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    FindWpfDescendants<Button>(appLaunch),
                    button => string.Equals(button.Content?.ToString(), "More about app-launch buttons", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    FindWpfDescendants<Button>(globalPermissions),
                    button => string.Equals(button.Content?.ToString(), "More about global permissions", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void PreferenceAccordionsUseBalancedContentInset()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        using var settingsScope = HostSettingsRegistry.BeginIsolatedScope();
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
                window.ShowPage(HostPage.Preferences);
                var application = FindWpfDescendants<Expander>(window)
                    .Single(section => string.Equals(section.Header as string, "Application", StringComparison.Ordinal));
                application.IsExpanded = true;
                SettleLayout(window);
                application.ApplyTemplate();

                var contentPresenter = Assert.IsType<ContentPresenter>(application.Template.FindName("ExpandSite", application));
                Assert.Equal(new Thickness(UiTokens.SpaceLg), contentPresenter.Margin);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    private static double GetGapBefore(FrameworkElement element)
    {
        var parent = Assert.IsType<SpacingStackPanel>(element.Parent);
        Assert.Equal(UiTokens.SpaceMd, parent.Spacing);
        var layoutWidth = Math.Max(parent.ActualWidth, 600d);
        parent.Measure(new WpfSize(layoutWidth, double.PositiveInfinity));
        parent.Arrange(new WpfRect(0d, 0d, layoutWidth, parent.DesiredSize.Height));
        var elementIndex = parent.Children.IndexOf(element);
        var previous = parent.Children
            .OfType<FrameworkElement>()
            .Take(elementIndex)
            .Last(child => child.Visibility == Visibility.Visible);
        Assert.Equal(new Thickness(), previous.Margin);
        var elementTop = element.TranslatePoint(new WpfPoint(), parent).Y;
        var previousTop = previous.TranslatePoint(new WpfPoint(), parent).Y;
        var gap = elementTop - previousTop - previous.ActualHeight;
        Assert.True(
            gap > 0,
            $"Expected a positive composed gap before {element.GetType().Name}; parent spacing={parent.Spacing}, previous={previous.GetType().Name}, previous top={previousTop}, previous height={previous.ActualHeight}, element top={elementTop}.");
        return gap;
    }

    private static void SettleLayout(Window window)
    {
        window.Dispatcher.Invoke(window.UpdateLayout, DispatcherPriority.Loaded);
        window.UpdateLayout();
    }
}
