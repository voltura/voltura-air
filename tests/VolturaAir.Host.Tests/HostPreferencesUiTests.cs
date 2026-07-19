using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VolturaAir.Host.Features.Preferences;
using WpfFocusManager = System.Windows.Input.FocusManager;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
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
                    "Application|More about application logs|Appearance|Trackpad defaults|Remote defaults|Keep awake|Global permissions|More about global permissions|Text destination|More about text destinations|Application launch buttons|More about app-launch buttons|Custom pointer|Developer tools|Windows locking",
                    string.Join('|', sections.Select(section => section.Header)));
                Assert.Single(FindWpfDescendants<ModernDatePicker>(window));
                Assert.Empty(FindWpfDescendants<DatePicker>(window));
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Allow paired devices to open web addresses", StringComparison.Ordinal));
                Assert.DoesNotContain(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Allow paired devices to control presentations", StringComparison.Ordinal));
                var alphaFeatures = Assert.Single(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Enable alpha features", StringComparison.Ordinal));
                Assert.False(alphaFeatures.IsChecked);
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
    public void DisabledCursorRecoveryWatchdogIsShownAsAWarning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AppPointerSettings.SetUseCursorRecoveryWatchdog(false);
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

                var useWatchdog = Assert.Single(
                    FindWpfDescendants<CheckBox>(window),
                    checkbox => string.Equals(
                        checkbox.Content as string,
                        "Use cursor recovery watchdog",
                        StringComparison.Ordinal));
                var info = Assert.Single(
                    FindWpfDescendants<Button>(window),
                    button => string.Equals(
                        System.Windows.Automation.AutomationProperties.GetName(button),
                        "About cursor recovery watchdog",
                        StringComparison.Ordinal));

                Assert.False(useWatchdog.IsChecked);
                Assert.Same(window.Resources["DangerBrush"], useWatchdog.Foreground);
                Assert.Equal("ⓘ", info.Content);
                Assert.Same(window.Resources["DangerBrush"], info.Foreground);
                Assert.Same(window.Resources["DangerBrush"], info.BorderBrush);
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
            using var awakeService = new NoOpAwakeService();
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
}
