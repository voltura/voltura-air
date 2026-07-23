using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VolturaAir.Host.Features.Preferences;
using WpfFocusManager = System.Windows.Input.FocusManager;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void WatchdogStartFailureDirectsTheUserToReinstall()
    {
        Assert.Equal(
            "Cursor recovery watchdog could not be started. Reinstall Voltura Air to restore it.",
            CustomPointerSettingsSection.WatchdogStartFailureMessage);
    }

    [Fact]
    public void PreferencesExpansionRevealsFirstControlWithoutMovingHeaderFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var (scroller, section, content, header, firstControl) = CreatePreferencesScrollFixture(260);
            var sectionTopAtStart = section.TransformToAncestor(scroller).Transform(new Point()).Y;
            scroller.ScrollToVerticalOffset(sectionTopAtStart - 100);
            scroller.UpdateLayout();

            var firstControlTopBefore = firstControl.TransformToAncestor(scroller).Transform(new Point()).Y;
            Assert.True(firstControlTopBefore + firstControl.RenderSize.Height > scroller.ViewportHeight - 16);
            var initialOffset = scroller.VerticalOffset;
            WpfFocusManager.SetFocusedElement(scroller, header);
            Assert.Same(header, WpfFocusManager.GetFocusedElement(scroller));

            PreferencesScrollCoordinator.RevealExpandedSection(scroller, section, content);
            scroller.UpdateLayout();

            var firstControlTop = firstControl.TransformToAncestor(scroller).Transform(new Point()).Y;
            var sectionTop = section.TransformToAncestor(scroller).Transform(new Point()).Y;
            Assert.True(scroller.VerticalOffset > initialOffset + 0.5);
            Assert.True(firstControlTop + firstControl.RenderSize.Height <= scroller.ViewportHeight - 15.5);
            Assert.True(sectionTop >= 15.5);
            Assert.Same(header, WpfFocusManager.GetFocusedElement(scroller));
        });
    }

    [Fact]
    public void PreferencesExpansionDoesNotScrollAlreadyVisibleContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var (scroller, section, content, _, _) = CreatePreferencesScrollFixture(0);

            PreferencesScrollCoordinator.RevealExpandedSection(scroller, section, content);

            Assert.InRange(scroller.VerticalOffset, 0, 0.5);
        });
    }

    private static (ScrollViewer Scroller, Expander Section, StackPanel Content, ToggleButton Header, Button FirstControl)
        CreatePreferencesScrollFixture(double leadingHeight)
    {
        var header = new ToggleButton { Content = "Section header", Height = 48 };
        var firstControl = new Button { Content = "First setting", Height = 40 };
        var content = new StackPanel();
        content.Children.Add(firstControl);
        content.Children.Add(new Border { Height = 160 });
        var section = new Expander
        {
            Header = header,
            Content = content,
            IsExpanded = true
        };
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = leadingHeight });
        panel.Children.Add(section);
        panel.Children.Add(new Border { Height = 120 });
        var scroller = new ScrollViewer
        {
            Width = 320,
            Height = 180,
            CanContentScroll = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = panel
        };
        WpfFocusManager.SetIsFocusScope(scroller, true);
        var viewportSize = new Size(scroller.Width, scroller.Height);
        scroller.Measure(viewportSize);
        scroller.Arrange(new Rect(0, 0, viewportSize.Width, viewportSize.Height));
        scroller.UpdateLayout();
        return (scroller, section, content, header, firstControl);
    }

    [Fact]
    public void PreferencesUseIntentionalOrderAndThemedExpirationPicker()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();

                var sections = FindWpfDescendants<Expander>(window).ToArray();
                Assert.Equal(
                    "Application|More about application logs|Appearance|Trackpad defaults|Remote defaults|Presentation|Keep awake|Global permissions|More about global permissions|Text destination|More about text destinations|Application launch buttons|More about app-launch buttons|Custom pointer|Developer tools|Windows locking",
                    string.Join('|', sections.Select(section => section.Header)));
                var presentation = Assert.Single(
                    sections,
                    section => string.Equals(section.Header as string, "Presentation", StringComparison.Ordinal));
                Assert.Equal(Visibility.Visible, presentation.Visibility);
                Assert.Single(FindWpfDescendants<ModernDatePicker>(window));
                Assert.Empty(FindWpfDescendants<DatePicker>(window));
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Allow paired devices to open web addresses", StringComparison.Ordinal));
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Allow paired devices to control presentations", StringComparison.Ordinal));
                var alphaFeatures = Assert.Single(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Enable alpha features", StringComparison.Ordinal));
                Assert.True(alphaFeatures.IsChecked);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void AlphaFeatureSettingRefreshesPresentationPermissionVisibility()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();

                var alphaFeatures = Assert.Single(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Enable alpha features", StringComparison.Ordinal));
                alphaFeatures.IsChecked = true;
                window.UpdateLayout();

                Assert.True(AppDeveloperSettings.EnableAlphaFeatures());
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Allow paired devices to control presentations", StringComparison.Ordinal));
                var presentation = Assert.Single(
                    FindWpfDescendants<Expander>(window),
                    section => string.Equals(section.Header as string, "Presentation", StringComparison.Ordinal));
                Assert.Equal(Visibility.Visible, presentation.Visibility);
                presentation.IsExpanded = true;
                window.UpdateLayout();
                Assert.Contains(
                    FindWpfDescendants<TextBlock>(presentation),
                    text => string.Equals(text.Text, "Laser pointer size", StringComparison.Ordinal));
                Assert.Contains(
                    FindWpfDescendants<ToggleButton>(presentation),
                    button => string.Equals(button.Content?.ToString(), "Red", StringComparison.Ordinal));

                var refreshedAlphaFeatures = Assert.Single(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Enable alpha features", StringComparison.Ordinal));
                refreshedAlphaFeatures.IsChecked = false;
                window.UpdateLayout();

                Assert.False(AppDeveloperSettings.EnableAlphaFeatures());
                Assert.DoesNotContain(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Allow paired devices to control presentations", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ScreenshotPreferencesSelectionOpensTheRequestedSection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPreferencesSectionForScreenshot("Global permissions");
                window.UpdateLayout();

                var selectedSection = Assert.Single(
                    FindWpfDescendants<Expander>(window),
                    section => string.Equals(section.Header as string, "Global permissions", StringComparison.Ordinal));
                Assert.True(selectedSection.IsExpanded);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ChangingKeepAwakeSettingKeepsItsSectionExpanded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var awakeService = new NoOpAwakeService();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, awakeService: awakeService);
            try
            {
                window.Show();
                window.ShowAwakePreferences();
                window.UpdateLayout();
                var scroller = Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                scroller.ScrollToVerticalOffset(Math.Min(240, scroller.ScrollableHeight));
                scroller.UpdateLayout();
                var offsetBeforeChange = scroller.VerticalOffset;

                var keepScreenOn = Assert.Single(
                    FindWpfDescendants<CheckBox>(window),
                    checkbox => string.Equals(
                        checkbox.Content as string,
                        "Keep screen on while Keep awake is active",
                        StringComparison.Ordinal));
                keepScreenOn.IsChecked = true;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var keepAwake = Assert.Single(
                    FindWpfDescendants<Expander>(window),
                    section => string.Equals(section.Header as string, "Keep awake", StringComparison.Ordinal));
                Assert.True(keepAwake.IsExpanded);
                var refreshedScroller = Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                Assert.InRange(refreshedScroller.VerticalOffset, offsetBeforeChange - 0.5, offsetBeforeChange + 0.5);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void AppLaunchPresetTestButtonUsesTheSharedLaunchService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(AppLaunchSettings.SetPresetEnabled(AppLaunchKind.Browser, true, out var error), error);
        Assert.True(
            AppLaunchSettings.TrySaveCustom("Example", Environment.ProcessPath!, null, null, out var customAction, out error),
            error);
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var injector = new SendInputInjector();
            var appLaunch = new RecordingAppLaunchService();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(
                manager,
                new InputDispatcher(injector),
                appLaunchService: appLaunch,
                isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Preferences);
                var section = Assert.Single(
                    FindWpfDescendants<Expander>(window),
                    item => string.Equals(item.Header as string, "Application launch buttons", StringComparison.Ordinal));
                section.IsExpanded = true;
                window.UpdateLayout();

                var test = Assert.Single(
                    FindWpfDescendants<Button>(section),
                    button => string.Equals(
                        System.Windows.Automation.AutomationProperties.GetName(button),
                        "Test Browser launch",
                        StringComparison.Ordinal));
                var disabledTest = Assert.Single(
                    FindWpfDescendants<Button>(section),
                    button => string.Equals(
                        System.Windows.Automation.AutomationProperties.GetName(button),
                        "Test Spotify launch",
                        StringComparison.Ordinal));
                var customTest = Assert.Single(
                    FindWpfDescendants<Button>(section),
                    button => string.Equals(
                        System.Windows.Automation.AutomationProperties.GetName(button),
                        "Test Example launch",
                        StringComparison.Ordinal));

                Assert.True(test.IsEnabled);
                Assert.False(disabledTest.IsEnabled);
                test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                customTest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(["preset.browser", customAction.Id], appLaunch.ActionIds);
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Started WWW.");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    private sealed class RecordingAppLaunchService : IAppLaunchService
    {
        public List<string> ActionIds { get; } = [];

        public IReadOnlyList<AppLaunchActionSummary> GetActions() => [];

        public AppLaunchExecutionResult Execute(string actionId)
        {
            ActionIds.Add(actionId);
            return new AppLaunchExecutionResult(true, "started", "Started WWW.");
        }
    }
}
