using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VolturaAir.Host.Features.Preferences;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfFocusManager = System.Windows.Input.FocusManager;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void CustomPointerUnavailableMessageIsShort()
    {
        Assert.Equal(
            "Custom pointer is temporarily unavailable.",
            CustomPointerSettingsSection.TemporarilyUnavailableMessage);
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
                    "Application|More about application logs|Appearance|Trackpad defaults|Remote defaults|Presentation|Keep awake|Device access|More about device access|Screen viewing|Text destination|More about text destinations|Application launch buttons|More about app-launch buttons|Custom pointer|Developer tools|Windows locking",
                    string.Join('|', sections.Select(section => section.Header)));
                var presentation = Assert.Single(
                    sections,
                    section => string.Equals(section.Header as string, "Presentation", StringComparison.Ordinal));
                Assert.Equal(Visibility.Visible, presentation.Visibility);
                Assert.Single(FindWpfDescendants<ModernDatePicker>(window));
                Assert.Empty(FindWpfDescendants<DatePicker>(window));
                Assert.Contains(FindWpfDescendants<ComboBox>(window), comboBox =>
                    string.Equals(comboBox.GetValue(AutomationProperties.NameProperty) as string, "Default access for newly paired devices", StringComparison.Ordinal));
                Assert.DoesNotContain(FindWpfDescendants<CheckBox>(window), checkbox =>
                    string.Equals(checkbox.Content?.ToString(), "Enable alpha features", StringComparison.Ordinal));
                Assert.DoesNotContain(FindWpfDescendants<CheckBox>(window), checkbox =>
                    checkbox.Content?.ToString()?.Contains("Screen viewing", StringComparison.OrdinalIgnoreCase) == true);
                Assert.Contains(FindWpfDescendants<Button>(window), button =>
                    string.Equals(button.Content as string, "Voltura default", StringComparison.Ordinal));
                Assert.Equal(
                    ["Automatic (recommended)", "Full resolution", "Data saver"],
                    FindWpfDescendants<RadioButton>(window)
                        .Where(choice => Equals(choice.GroupName, "DirectScreenQuality"))
                        .Select(choice => choice.Content?.ToString())
                        .ToArray());
                Assert.Equal(
                    ["High", "Standard", "Low"],
                    FindWpfDescendants<RadioButton>(window)
                        .Where(choice => Equals(choice.GroupName, "ScreenViewSoundQuality"))
                        .Select(choice => choice.Content?.ToString())
                        .ToArray());
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text =>
                    text.Text == "Quality for Direct connections. Relay quality is set under Connection.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text =>
                    text.Text == "Limits video to 4 Mbps and 1080p.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text =>
                    text.Text == "Best detail for music and movies. Stereo.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text =>
                    text.Text == "Good stereo sound with lower network use.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text =>
                    text.Text == "Reduced-detail mono sound with the lowest network use.");
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
                window.ShowPreferencesSectionForScreenshot("Device access");
                window.UpdateLayout();

                var selectedSection = Assert.Single(
                    FindWpfDescendants<Expander>(window),
                    section => string.Equals(section.Header as string, "Device access", StringComparison.Ordinal));
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
                var scroller = FindWpfDescendants<ScrollViewer>(window)
                    .Single(viewer => viewer.Name == "PreferencesScroller");
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
                var refreshedScroller = FindWpfDescendants<ScrollViewer>(window)
                    .Single(viewer => viewer.Name == "PreferencesScroller");
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
    public void SimulatedActivityPreferenceTracksServiceAndReleasesRebuiltViewSubscription()
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
            var activity = new RecordingActivitySimulationService();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(injector), isolatedTestMode: true);
            var window = new MainWindow(
                manager,
                webHost,
                clientUrl: null,
                activitySimulationService: activity);
            try
            {
                window.Show();
                window.ShowAwakePreferences();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var checkbox = Assert.Single(
                    FindWpfDescendants<CheckBox>(window),
                    item => string.Equals(item.Content as string, "Simulate activity every 59 seconds", StringComparison.Ordinal));
                Assert.False(checkbox.IsChecked);
                Assert.Equal(1, activity.StateSubscriberCount);

                activity.SetEnabledAsync(true).GetAwaiter().GetResult();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.True(checkbox.IsChecked);

                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Equal(1, activity.StateSubscriberCount);
                Assert.True(Assert.Single(
                    FindWpfDescendants<CheckBox>(window),
                    item => string.Equals(item.Content as string, "Simulate activity every 59 seconds", StringComparison.Ordinal)).IsChecked);
            }
            finally
            {
                window.AllowClose();
                window.Close();
                DisposeWebHost(webHost);
            }

            Assert.Equal(0, activity.StateSubscriberCount);
        });
    }

    [Fact]
    public void SimulatedActivityTrayItemTracksServiceState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var activity = new RecordingActivitySimulationService();
            var awake = new NoOpAwakeService();
            try
            {
                using var controller = new TrayAwakeMenuController(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher,
                    awake,
                    activity,
                    static () => { },
                    static _ => { },
                    static _ => { });
                var item = Assert.IsType<System.Windows.Forms.ToolStripMenuItem>(
                    controller.MenuItem.DropDownItems
                        .Cast<System.Windows.Forms.ToolStripItem>()
                        .Single(candidate => candidate.Text == "Simulate activity every 59 seconds"));
                Assert.False(item.Checked);

                activity.SetEnabledAsync(true).GetAwaiter().GetResult();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                Assert.True(item.Checked);
            }
            finally
            {
                awake.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void PreferencesSearchMatchesSettingLabelsInScreenOrderWithBreadcrumbs()
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
                var view = Assert.Single(FindWpfDescendants<PreferencesPageView>(window));

                var signIn = Search(view, "SIGN IN");
                var start = Assert.Single(signIn);
                Assert.Equal("Start Voltura Air when I sign in to Windows", start.Label);
                Assert.Equal("Application", start.Breadcrumb);

                var duplicates = Search(view, "3D effect on controls");
                Assert.Equal(2, duplicates.Length);
                Assert.Equal(
                    ["Appearance > Theme", "Appearance > Device"],
                    duplicates.Select(result => result.Breadcrumb));

                var accessSettings = Search(view, "access");
                Assert.Contains(accessSettings, result => result.Label == "Default access for newly paired devices");
                Assert.Single(Search(view, "Allow trusted devices to control the Voltura Air application"));

                Assert.Empty(Search(view, "Save URL"));
                Assert.Empty(Search(view, "Off by default"));
                Assert.Empty(Search(view, "setting that does not exist"));
                Assert.Equal(Visibility.Visible, view.NoSearchResults.Visibility);
                Assert.False(view.SearchPopup.StaysOpen);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void PreferencesSearchOpensRevealsAndFocusesTheSelectedSetting()
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
                var view = Assert.Single(FindWpfDescendants<PreferencesPageView>(window));

                var section = Search(view, "Application")
                    .Single(result => string.Equals(result.Label, "Application", StringComparison.Ordinal));
                view.ActivateSearchResult(section);
                WaitForWpf(() => view.PendingSearchActivation.IsCompleted, "section search activation");
                view.PendingSearchActivation.GetAwaiter().GetResult();
                Assert.True(view.ApplicationSection.IsExpanded);
                Assert.True(HasFocus(section.Entry.FocusTarget));

                view.AppearanceSection.IsExpanded = true;

                var start = Assert.Single(Search(view, "sign in"));
                view.ActivateSearchResult(start);
                WaitForWpf(() => view.PendingSearchActivation.IsCompleted, "preference search activation");
                view.PendingSearchActivation.GetAwaiter().GetResult();
                Assert.True(HasFocus(start.Entry.FocusTarget));
                window.UpdateLayout();

                Assert.Equal("sign in", view.SearchQuery);
                Assert.False(view.SearchPopup.IsOpen);
                Assert.True(view.ApplicationSection.IsExpanded);
                Assert.False(view.AppearanceSection.IsExpanded);
                AssertTargetIsVisible(view, start.Entry.RevealTarget);

                var nested = Assert.Single(Search(view, "Windows locking"));
                view.ActivateSearchResult(nested);
                WaitForWpf(() => view.PendingSearchActivation.IsCompleted, "nested preference activation");
                view.PendingSearchActivation.GetAwaiter().GetResult();
                Assert.True(HasFocus(nested.Entry.FocusTarget));
                Assert.True(view.DeveloperSection.IsExpanded);
                Assert.True(Assert.IsType<Expander>(nested.Entry.RevealTarget).IsExpanded);

                var field = Assert.Single(Search(view, "YouTube URL"));
                view.ActivateSearchResult(field);
                WaitForWpf(() => view.PendingSearchActivation.IsCompleted, "field preference activation");
                view.PendingSearchActivation.GetAwaiter().GetResult();
                Assert.True(HasFocus(field.Entry.FocusTarget));
                Assert.True(view.RemoteSection.IsExpanded);

                view.SearchInput.Text = "Developer mode";
                view.SearchInput.Focus();
                view.SearchInput.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    Keyboard.PrimaryDevice.ActiveSource,
                    0,
                    Key.Down)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                Assert.Equal(0, view.SearchResults.SelectedIndex);
                view.SearchResults.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    Keyboard.PrimaryDevice.ActiveSource,
                    0,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                Assert.False(view.SearchPopup.IsOpen);
                Assert.True(view.SearchInput.IsKeyboardFocused);

                view.SearchInput.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    Keyboard.PrimaryDevice.ActiveSource,
                    0,
                    Key.Up)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                Assert.Equal(view.SearchResults.Items.Count - 1, view.SearchResults.SelectedIndex);
                view.SearchInput.Focus();
                view.SearchInput.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    Keyboard.PrimaryDevice.ActiveSource,
                    0,
                    Key.Down)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                view.SearchResults.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    Keyboard.PrimaryDevice.ActiveSource,
                    0,
                    Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                WaitForWpf(
                    () => view.DeveloperSection.IsExpanded && !view.SearchPopup.IsOpen,
                    "keyboard search activation");

                view.SearchInput.Text = "sign in";
                Assert.Equal(Visibility.Visible, view.ClearSearch.Visibility);
                view.ClearSearch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(string.Empty, view.SearchQuery);
                Assert.Equal(Visibility.Collapsed, view.ClearSearch.Visibility);
                Assert.False(view.SearchPopup.IsOpen);
                Assert.True(view.SearchInput.IsKeyboardFocused);

                view.SearchInput.Text = "sign in";
                view.SearchInput.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    Keyboard.PrimaryDevice.ActiveSource,
                    0,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                Assert.False(view.SearchPopup.IsOpen);
                Assert.Equal("sign in", view.SearchQuery);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void PreferencesSearchRebuildsRegistryAndRetainsQuery()
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
                var initial = Assert.Single(FindWpfDescendants<PreferencesPageView>(window));
                var initialPermissions = initial.PermissionsSection;
                var initialResult = Assert.Single(Search(initial, "Default access for newly paired devices"));

                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();
                var rebuilt = Assert.Single(FindWpfDescendants<PreferencesPageView>(window));

                Assert.Equal("Default access for newly paired devices", rebuilt.SearchQuery);
                var result = Assert.Single(rebuilt.SearchResults.Items.Cast<PreferenceSearchResult>());
                Assert.Equal(initialResult.Label, result.Label);
                Assert.NotSame(initial, rebuilt);
                Assert.NotSame(initialPermissions, rebuilt.PermissionsSection);
                Assert.Same(
                    rebuilt.PermissionsSection,
                    Assert.Single(PreferencesSearchRegistry.FindContainingExpanders(result.Entry.RevealTarget)));
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

        public AppLaunchExecutionResult ExecutePowerPointFile(string path) =>
            new(true, "started", "Started PowerPoint.");
    }

    private sealed class RecordingActivitySimulationService : IActivitySimulationService
    {
        private EventHandler? _stateChanged;

        public bool Enabled { get; private set; }

        public int StateSubscriberCount { get; private set; }

        public event EventHandler? StateChanged
        {
            add
            {
                _stateChanged += value;
                StateSubscriberCount++;
            }
            remove
            {
                _stateChanged -= value;
                StateSubscriberCount--;
            }
        }

        public event EventHandler<ActivitySimulationFailureEventArgs>? FailureStreakStarted
        {
            add { }
            remove { }
        }

        public Task<ActivitySimulationOperationResult> SetEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            Enabled = enabled;
            _stateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(ActivitySimulationOperationResult.Success);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static PreferenceSearchResult[] Search(PreferencesPageView view, string query)
    {
        view.SearchInput.Text = query;
        view.UpdateLayout();
        return view.SearchResults.Items.Cast<PreferenceSearchResult>().ToArray();
    }

    private static void AssertTargetIsVisible(PreferencesPageView view, FrameworkElement target)
    {
        var targetTop = target.TransformToAncestor(view.Scroller).Transform(new Point()).Y;
        Assert.InRange(targetTop, 0, view.Scroller.ViewportHeight);
        Assert.InRange(targetTop + target.ActualHeight, 0, view.Scroller.ViewportHeight);
    }

    private static bool HasFocus(FrameworkElement target)
    {
        if (target.IsKeyboardFocusWithin)
        {
            return true;
        }

        var focused = FocusManager.GetFocusedElement(FocusManager.GetFocusScope(target)) as DependencyObject;
        return ReferenceEquals(focused, target) ||
            (focused is not null && FindWpfDescendants<DependencyObject>(target).Contains(focused));
    }

    private sealed class SearchPowerController : ISystemPowerController
    {
        public bool ScreenSaverAvailable { get; set; }

        public SystemPowerExecutionResult TryExecute(string action) => SystemPowerExecutionResult.Success;

        public bool IsActionAvailable(string action) =>
            action != SystemPowerActions.ScreenSaver || ScreenSaverAvailable;

        public bool DismissBlackoutIfActive() => false;
    }
}
