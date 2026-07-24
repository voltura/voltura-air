using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed partial class HostUiLayoutTests : IsolatedHostSettingsTest
{
    [Fact]
    public void StartupWindowCanTransitionToErrorState()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new StartupWindow();
            try
            {
                Assert.Equal(520, window.Width);
                Assert.Equal(360, window.Height);
                window.Show();
                var progress = Assert.Single(FindWpfDescendants<ProgressBar>(window));
                var progressGroup = Assert.IsType<VolturaAir.Host.Ui.SpacingStackPanel>(progress.Parent);
                Assert.Equal(UiTokens.SpaceLg, progressGroup.Spacing);
                Assert.Contains(
                    progressGroup.Children.OfType<TextBlock>(),
                    text => string.Equals(text.Text, "Starting connection services.", StringComparison.Ordinal));
                window.ShowError("An unexpected startup error occurred.", "details");
                window.UpdateLayout();

                Assert.Equal(620, window.Width);
                Assert.Equal(440, window.Height);
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Voltura Air could not start.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "An unexpected startup error occurred.");
                var scroller = Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                Assert.Equal(0, scroller.ScrollableHeight);
                var copyDetailsButton = FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Copy details");
                var closeButton = FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Close");
                Assert.Equal(2, FindWpfDescendants<Button>(window).Count());
                Assert.False(IsDescendantOf(copyDetailsButton, scroller));
                AssertControlReceivesPointerHit(window, copyDetailsButton);
                AssertControlIsFullyWithinWindow(window, copyDetailsButton);
                AssertControlReceivesPointerHit(window, closeButton);
                AssertControlIsFullyWithinWindow(window, closeButton);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MainWindowNavigatesToPrimaryPages()
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
                Assert.Null(window.PageContent.Content);
                window.Show();
                var sidebarHeader = Assert.IsType<VolturaAir.Host.Ui.SpacingStackPanel>(window.NavStatusText.Parent);
                var sidebarLayout = Assert.IsType<Grid>(sidebarHeader.Parent);
                Assert.Equal(new GridLength(UiTokens.SpaceXl), sidebarLayout.RowDefinitions[1].Height);
                Assert.DoesNotContain(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Hide to tray");
                Assert.Equal("Ready to pair", window.NavStatusText.Text);
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connect");
                Assert.Contains(
                    FindWpfDescendants<TextBlock>(window),
                    text => text.Text == "Pair a phone, tablet, or browser on the same network.");
                Assert.Equal("New code", FindPairingCodeAction(window, "New code").Content);

                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connection");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Choose another adapter");
                Assert.DoesNotContain(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Save and restart" && button.IsVisible);

                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Devices");
                Assert.Contains(FindWpfDescendants<ListBox>(window), list => Equals(list.GetValue(AutomationProperties.NameProperty), "Paired devices"));

                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();
                var sections = FindWpfDescendants<Expander>(window).ToArray();
                Assert.Equal(16, sections.Length);
                var scroller = Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                Assert.False(scroller.CanContentScroll);
                Assert.Equal(ScrollBarVisibility.Visible, scroller.VerticalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
                Assert.All(sections, section => Assert.False(section.IsExpanded));
                var nestedSections = sections
                    .Where(section => section.Header is "More about application logs" or "More about global permissions" or "More about text destinations" or "More about app-launch buttons" or "Windows locking")
                    .ToArray();
                Assert.Equal(5, nestedSections.Length);
                Assert.All(
                    sections.Except(nestedSections),
                    section => Assert.Same(window.Resources["PreferencesAccordionStyle"], section.Style));
                Assert.All(
                    nestedSections,
                    section => Assert.Same(window.Resources["PreferencesNestedAccordionStyle"], section.Style));
                var topLevelSections = sections.Except(nestedSections).ToArray();
                topLevelSections[0].IsExpanded = true;
                topLevelSections[1].IsExpanded = true;
                Assert.False(topLevelSections[0].IsExpanded);
                Assert.True(topLevelSections[1].IsExpanded);
                Assert.Single(sections, section => section.IsExpanded);

                window.ShowPage(HostPage.Diagnostics);
                window.UpdateLayout();
                var appLogView = FindWpfDescendants<ToggleButton>(window)
                    .Single(button => button.Content?.ToString() == "Application log");
                var systemView = FindWpfDescendants<ToggleButton>(window)
                    .Single(button => button.Content?.ToString() == "System details");
                Assert.True(appLogView.IsChecked);
                Assert.False(systemView.IsChecked);
                var dateRange = Assert.Single(FindWpfDescendants<ModernDateRangePicker>(window));
                Assert.Equal(DateTime.Today, dateRange.SelectedEndDate);

                systemView.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
                window.UpdateLayout();
                Assert.True(systemView.IsChecked);
                Assert.Empty(FindWpfDescendants<ModernDateRangePicker>(window));

                appLogView.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
                window.UpdateLayout();
                Assert.True(appLogView.IsChecked);
                Assert.Single(FindWpfDescendants<ModernDateRangePicker>(window));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void MainWindowRotatesPairingCodeAfterDeviceCountChanges()
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
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();
                var initialPairingUrl = window.PairingUrl;
                var initialParameters = new Uri(initialPairingUrl).Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .ToDictionary(part => part[0], part => part[1]);
                var token = initialParameters["t"];

                Assert.Equal(AppVersion.Display, Uri.UnescapeDataString(initialParameters["v"]));
                using var key = new PairingTestKey();
                var accepted = manager.AcceptPairing("client-a", "Phone", Uri.UnescapeDataString(token), reconnectPublicKey: key.PublicKey);
                DoWpfEvents();

                Assert.True(accepted.Accepted);
                Assert.Equal("1 paired device", window.NavStatusText.Text);
                Assert.NotEqual(initialPairingUrl, window.PairingUrl);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void MainWindowShowsFeedbackAfterCopyingPairingLink()
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
            var clipboard = new RecordingClipboardTextWriter();
            var window = new MainWindow(manager, webHost, clientUrl: null, clipboardTextWriter: clipboard);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();

                var copyLink = FindPairingCodeAction(window, "Copy link");
                copyLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DoWpfEvents();

                Assert.Equal(window.PairingUrl, clipboard.Text);
                Assert.Equal(1, clipboard.WriteCount);
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Link copied");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ApplicationLogControlsRemainHitTestableAndUpdateTheirState()
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
            var appLog = new FakeAppLog();
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), appLog: appLog, isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, appLog: appLog);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Diagnostics);
                window.UpdateLayout();
                WaitForWpf(() => appLog.ReadCount > 0, "initial application log read");

                var loggingToggle = FindWpfDescendants<CheckBox>(window)
                    .Single(checkBox => checkBox.Content?.ToString() == "Write application log");
                var automaticRefreshToggle = FindWpfDescendants<CheckBox>(window)
                    .Single(checkBox => checkBox.Content?.ToString() == "Automatic log refresh");
                var refreshButton = FindWpfDescendants<Button>(window)
                    .Single(button => button.Content?.ToString() == "Refresh");
                var dateRangeButton = FindWpfDescendants<ModernDateRangePicker>(window)
                    .SelectMany(FindWpfDescendants<Button>)
                    .Single(button => AutomationProperties.GetName(button) == "Choose application log date range");
                var eventFilterButton = Assert.IsType<Button>(
                    Assert.Single(FindWpfDescendants<EventMultiSelectFilter>(window)).Content);
                var filters = FindWpfDescendants<ComboBox>(window).ToArray();

                AssertControlReceivesPointerHit(window, loggingToggle);
                Assert.Equal(HorizontalAlignment.Left, loggingToggle.HorizontalAlignment);
                AssertControlReceivesPointerHit(window, automaticRefreshToggle);
                AssertControlReceivesPointerHit(window, refreshButton);
                AssertControlReceivesPointerHit(window, dateRangeButton);
                AssertControlReceivesPointerHit(window, eventFilterButton);
                Assert.All(filters, filter => AssertControlReceivesPointerHit(window, filter));
                Assert.Null(loggingToggle.FocusVisualStyle);
                Assert.Null(automaticRefreshToggle.FocusVisualStyle);
                Assert.Null(dateRangeButton.FocusVisualStyle);
                Assert.Null(eventFilterButton.FocusVisualStyle);
                Assert.All(filters, filter =>
                {
                    Assert.Null(filter.FocusVisualStyle);
                    Assert.Equal(new Thickness(1), filter.BorderThickness);
                });

                Assert.False(AppLoggingSettings.IsEnabled());
                loggingToggle.IsChecked = true;
                Assert.True(AppLoggingSettings.IsEnabled());

                var previousReadCount = appLog.ReadCount;
                automaticRefreshToggle.IsChecked = true;
                WaitForWpf(() => appLog.ReadCount > previousReadCount, "automatic log refresh subscription");
                DoWpfEvents();

                var sourceFilter = filters.Single(filter => filter.Items.Count == 3);
                sourceFilter.IsDropDownOpen = true;
                Assert.True(sourceFilter.IsDropDownOpen);
                sourceFilter.IsDropDownOpen = false;
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ApplicationLogKeepsActionsVisibleAndScrollsContentAtCompactHeight()
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
            var records = Enumerable.Range(0, 30)
                .Select(index => new AppLogRecord(
                    DateTimeOffset.Now.AddMinutes(-index),
                    "host_action",
                    "windows_host",
                    null,
                    null,
                    "application_logging",
                    "changed",
                    null,
                    null,
                    $"Record {index}"))
                .ToArray();
            var appLog = new FakeAppLog(records);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), appLog: appLog, isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, appLog: appLog)
            {
                Width = 920,
                Height = 620
            };
            try
            {
                window.Show();
                window.ShowPage(HostPage.Diagnostics);
                window.UpdateLayout();
                WaitForWpf(
                    () => FindWpfDescendants<TextBlock>(window).Any(text => text.Text == "Record 0"),
                    "initial compact application log render");
                window.UpdateLayout();

                var logScroller = Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                Assert.Equal(ScrollBarVisibility.Auto, logScroller.VerticalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, logScroller.HorizontalScrollBarVisibility);
                Assert.True(logScroller.ScrollableHeight > 0, $"Scrollable height was {logScroller.ScrollableHeight}; viewport {logScroller.ViewportHeight}; extent {logScroller.ExtentHeight}; actual {logScroller.ActualHeight}.");

                var applicationLogView = Assert.Single(FindWpfDescendants<VolturaAir.Host.Features.Diagnostics.ApplicationLogView>(window));
                var refreshButton = FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Refresh");
                var actionRow = Assert.IsAssignableFrom<FrameworkElement>(refreshButton.Parent);
                Assert.False(IsDescendantOf(actionRow, logScroller));
                Assert.True(IsDescendantOf(FindWpfDescendants<TextBlock>(window).Single(text => text.Text == "Record 0"), logScroller));
                var actionRowBottom = actionRow.TranslatePoint(new Point(0, actionRow.ActualHeight), applicationLogView).Y;
                Assert.InRange(actionRowBottom, applicationLogView.ActualHeight - 0.5, applicationLogView.ActualHeight + 0.5);

                AssertControlReceivesPointerHit(
                    window,
                    refreshButton);
                AssertControlIsFullyWithinWindow(
                    window,
                    refreshButton);
                AssertControlReceivesPointerHit(
                    window,
                    FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Copy filtered log"));
                AssertControlIsFullyWithinWindow(
                    window,
                    FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Copy filtered log"));
                AssertControlReceivesPointerHit(
                    window,
                    FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Open log folder"));
                AssertControlIsFullyWithinWindow(
                    window,
                    FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Open log folder"));
                AssertControlReceivesPointerHit(
                    window,
                    FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Delete logs"));
                AssertControlIsFullyWithinWindow(
                    window,
                    FindWpfDescendants<Button>(window).Single(button => button.Content?.ToString() == "Delete logs"));
                AssertControlReceivesPointerHit(
                    window,
                    FindWpfDescendants<CheckBox>(window).Single(checkBox => checkBox.Content?.ToString() == "Automatic log refresh"));
                AssertControlIsFullyWithinWindow(
                    window,
                    FindWpfDescendants<CheckBox>(window).Single(checkBox => checkBox.Content?.ToString() == "Automatic log refresh"));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ApplicationLogAutomaticRefreshCoalescesBurstsAndRecoversAfterReadFailure()
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
            var appLog = new FakeAppLog();
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), appLog: appLog, isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, appLog: appLog);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Diagnostics);
                WaitForWpf(() => appLog.ReadCount > 0, "initial application log read");
                var automaticRefresh = FindWpfDescendants<CheckBox>(window)
                    .Single(checkBox => checkBox.Content?.ToString() == "Automatic log refresh");
                var refresh = FindWpfDescendants<Button>(window)
                    .Single(button => button.Content?.ToString() == "Refresh");
                var beforeAutomaticRefresh = appLog.ReadCount;
                automaticRefresh.IsChecked = true;
                WaitForWpf(() => appLog.SubscriberCount == 1, "automatic refresh subscription");
                WaitForWpf(() => appLog.ReadCount > beforeAutomaticRefresh, "automatic refresh read");
                WaitForWpf(() => appLog.CompletedReadCount == appLog.ReadCount, "automatic refresh completion");
                DoWpfEvents();

                appLog.BlockReads();
                var beforeBurst = appLog.ReadCount;
                appLog.RaiseChanged();
                DoWpfEvents();
                Assert.True(appLog.ReadEntered.Wait(TimeSpan.FromSeconds(1)));
                Task.Run(() => appLog.RaiseChanged(100)).GetAwaiter().GetResult();
                DoWpfEvents();
                appLog.ReleaseReads();
                try
                {
                    WaitForWpf(() => appLog.ReadCount >= beforeBurst + 2, "coalesced follow-up application log read");
                }
                catch (TimeoutException exception)
                {
                    throw new TimeoutException($"{exception.Message} Reads: {appLog.ReadCount}; expected at least {beforeBurst + 2}; subscribers: {appLog.SubscriberCount}; delivered changes: {appLog.DeliveredChangeCount}.", exception);
                }
                DoWpfEvents();
                Assert.Equal(beforeBurst + 2, appLog.ReadCount);
                WaitForWpf(() => appLog.CompletedReadCount == appLog.ReadCount, "coalesced follow-up completion");
                automaticRefresh.IsChecked = false;
                WaitForWpf(() => appLog.SubscriberCount == 0, "automatic refresh unsubscription");
                DoWpfEvents();

                appLog.ThrowOnNextRead = true;
                var beforeFailure = appLog.ReadCount;
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWpf(() => appLog.ReadCount > beforeFailure, "application log failing read");
                WaitForWpf(() => appLog.FailedReadCount == 1, "application log read failure containment");
                var beforeRecovery = appLog.ReadCount;
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWpf(() => appLog.ReadCount > beforeRecovery, "application log recovery read");
                WaitForWpf(
                    () => FindWpfDescendants<TextBlock>(window).Any(text => text.Text.Contains("Showing", StringComparison.Ordinal)),
                    "application log read recovery");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ApplicationLogUnloadReleasesAutomaticRefreshSubscriptionDuringRead()
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
            var appLog = new FakeAppLog();
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), appLog: appLog, isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, appLog: appLog);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Diagnostics);
                WaitForWpf(() => appLog.ReadCount > 0, "initial application log read");
                var automaticRefresh = FindWpfDescendants<CheckBox>(window)
                    .Single(checkBox => checkBox.Content?.ToString() == "Automatic log refresh");
                automaticRefresh.IsChecked = true;
                WaitForWpf(() => appLog.SubscriberCount == 1, "automatic refresh subscription");

                appLog.BlockReads();
                appLog.RaiseChanged();
                DoWpfEvents();
                Assert.True(appLog.ReadEntered.Wait(TimeSpan.FromSeconds(1)));
                window.ShowPage(HostPage.Connection);
                DoWpfEvents();

                Assert.Equal(0, appLog.SubscriberCount);
                appLog.ReleaseReads();
                DoWpfEvents();
                Assert.DoesNotContain(FindWpfDescendants<VolturaAir.Host.Features.Diagnostics.ApplicationLogView>(window), _ => true);
            }
            finally
            {
                appLog.ReleaseReads();
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    private static void AssertControlReceivesPointerHit(Window window, FrameworkElement control)
    {
        Assert.True(control.IsEnabled, $"{control.GetType().Name} is disabled.");
        Assert.True(control.IsHitTestVisible, $"{control.GetType().Name} is not hit-test visible.");
        Assert.True(control.ActualWidth > 0 && control.ActualHeight > 0, $"{control.GetType().Name} has no arranged size.");

        var center = control.TranslatePoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2), window);
        var hit = window.InputHitTest(center) as DependencyObject;
        Assert.NotNull(hit);
        Assert.True(
            IsDescendantOf(hit, control),
            $"{control.GetType().Name} is covered by {hit.GetType().Name} at its center point.");
    }

    private static void AssertControlIsFullyWithinWindow(Window window, FrameworkElement control)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        var topLeft = control.TranslatePoint(new Point(0, 0), content);
        var bottomRight = control.TranslatePoint(new Point(control.ActualWidth, control.ActualHeight), content);

        Assert.True(topLeft.X >= 0, $"{control.GetType().Name} starts before the window content.");
        Assert.True(topLeft.Y >= 0, $"{control.GetType().Name} starts above the window content.");
        Assert.True(bottomRight.X <= content.ActualWidth, $"{control.GetType().Name} extends past the window content width.");
        Assert.True(bottomRight.Y <= content.ActualHeight, $"{control.GetType().Name} extends past the window content height.");
    }

    private static bool IsDescendantOf(DependencyObject candidate, DependencyObject ancestor)
    {
        for (DependencyObject? current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class RecordingClipboardTextWriter : IClipboardTextWriter
    {
        public string? Text { get; private set; }

        public int WriteCount { get; private set; }

        public void WriteText(string text)
        {
            Text = text;
            WriteCount++;
        }
    }

    [Fact]
    public void PreferencesOffersLocalEnablementWhenWindowsLockingIsDisabled()
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
            var policy = new FakeWorkstationLockPolicy(WorkstationLockPolicyState.Disabled);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), workstationLockPolicy: policy, isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, workstationLockPolicy: policy);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();

                var enable = FindWpfDescendants<Button>(window)
                    .Single(button => button.Content?.ToString() == "Enable Windows locking");

                Assert.NotNull(enable);
                Assert.Equal(HorizontalAlignment.Left, enable.HorizontalAlignment);
                Assert.Equal(0, policy.EnableCalls);
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Controls whether Windows allows Lock PC and Win+L for this user.");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void DevicePermissionRowsShowEffectiveStatesForBlackoutClipboardAndUrlOpen()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            AppPermissionSettings.Save(AppPermissionSettings.Load() with
            {
                AllowBlackoutDisplay = true,
                AllowClipboardRead = false,
                AllowUrlOpen = false
            });

            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var token = manager.CreatePairingToken();
            using var key = new PairingTestKey();
            Assert.True(manager.AcceptPairing("client-a", "Phone", token, reconnectPublicKey: key.PublicKey).Accepted);
            Assert.True(manager.SetDevicePermissionOverrides("client-a", new DevicePermissionOverrides(AllowBlackoutDisplay: true, AllowUrlOpen: true)));

            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();

                ExpandDevicePermissions(window);
                WaitForWpf(() => FindPermissionButton(window, "Blackout display", "✓ Allow") is not null, "blackout display effective state");

                var blackoutAllow = Assert.IsType<Button>(FindPermissionButton(window, "Blackout display", "✓ Allow"));
                var clipboardUseGlobal = Assert.IsType<Button>(FindPermissionButton(window, "Read PC clipboard", "✓ Use global"));
                var clipboardAllow = Assert.IsType<Button>(FindPermissionButton(window, "Read PC clipboard", "Allow"));
                var clipboardBlock = Assert.IsType<Button>(FindPermissionButton(window, "Read PC clipboard", "✓ Block"));
                Assert.Equal("✓ Allow", blackoutAllow.Content);
                Assert.Equal("✓ Block", clipboardBlock.Content);
                var choiceStyle = Assert.IsType<Style>(window.Resources["ChoiceStateButtonStyle"]);
                Assert.Same(choiceStyle, clipboardUseGlobal.Style);
                Assert.Same(window.Resources["StandardButtonStyle"], choiceStyle.BasedOn);
                Assert.Equal(new Thickness(14, 6, 14, 6), clipboardUseGlobal.Padding);
                Assert.Equal(112, clipboardUseGlobal.Width);
                Assert.Equal(112, clipboardAllow.Width);
                Assert.Equal(112, clipboardBlock.Width);
                var permissionButtonY = clipboardUseGlobal.TranslatePoint(new Point(), window).Y;
                Assert.InRange(Math.Abs(clipboardAllow.TranslatePoint(new Point(), window).Y - permissionButtonY), 0, 0.1);
                Assert.InRange(Math.Abs(clipboardBlock.TranslatePoint(new Point(), window).Y - permissionButtonY), 0, 0.1);
                Assert.Same(window.Resources["AccentBrush"], blackoutAllow.Background);
                Assert.Same(window.Resources["AccentBrush"], clipboardUseGlobal.Background);
                Assert.Same(window.Resources["AccentBrush"], clipboardBlock.BorderBrush);
                Assert.Equal(new Thickness(2), clipboardBlock.BorderThickness);
                var urlAllow = Assert.IsType<Button>(FindPermissionButton(window, "Open web addresses", "✓ Allow"));
                Assert.Equal("✓ Allow", urlAllow.Content);

                var blackoutBlock = Assert.IsType<Button>(FindPermissionButton(window, "Blackout display", "Block"));
                blackoutBlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWpf(
                    () => manager.GetDevicePermissionOverrides("client-a").AllowBlackoutDisplay == false,
                    "blackout display block override");
                var selectedBlackoutBlock = Assert.IsType<Button>(FindPermissionButton(window, "Blackout display", "✓ Block"));
                Assert.Equal(112, selectedBlackoutBlock.Width);
                Assert.Same(window.Resources["AccentBrush"], selectedBlackoutBlock.Background);
                Assert.True(FindVisualDescendants<Expander>(window)
                    .First(expander => string.Equals(expander.Header as string, "Permissions", StringComparison.Ordinal))
                    .IsExpanded);
                Assert.DoesNotContain(FindWpfDescendants<TextBlock>(window), text => text.Text == "Presentation control");

                AppDeveloperSettings.SetEnableAlphaFeatures(true);
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                ExpandDevicePermissions(window);
                WaitForWpf(
                    () => FindVisualDescendants<TextBlock>(window).Any(text => text.Text == "Presentation control"),
                    "alpha Presentation permission");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void DeviceTrackpadChangesUpdateInPlaceAndKeepAccordionExpanded()
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
            var token = manager.CreatePairingToken();
            using var key = new PairingTestKey();
            Assert.True(manager.AcceptPairing("client-a", "Phone", token, reconnectPublicKey: key.PublicKey).Accepted);

            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();

                var page = window.PageContent.Content;
                var device = FindVisualDescendants<Expander>(window)
                    .First(expander => expander.Header is DeviceListItem);
                device.IsExpanded = true;
                window.UpdateLayout();
                var trackpad = FindVisualDescendants<Expander>(window)
                    .First(expander => string.Equals(expander.Header as string, "Trackpad profile", StringComparison.Ordinal));
                trackpad.IsExpanded = true;
                window.UpdateLayout();

                var slider = Assert.Single(FindVisualDescendants<Slider>(trackpad));
                slider.Value = 75;
                FindVisualDescendants<Button>(trackpad)
                    .Single(button => string.Equals(button.Content as string, "Save speed", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWpf(
                    () => manager.GetDevices().Single().PointerSpeedOverride == 75,
                    "device pointer-speed override");
                DoWpfEvents();

                Assert.Same(page, window.PageContent.Content);
                Assert.True(trackpad.IsExpanded);
                Assert.Contains(FindVisualDescendants<TextBlock>(trackpad), text => text.Text == "Override active. Effective speed: 75%.");

                FindVisualDescendants<Button>(trackpad)
                    .Single(button => string.Equals(button.Content as string, "Use global", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWpf(
                    () => manager.GetDevices().Single().PointerSpeedOverride is null,
                    "global device pointer speed");
                DoWpfEvents();

                Assert.Same(page, window.PageContent.Content);
                Assert.True(trackpad.IsExpanded);
                Assert.Contains(FindVisualDescendants<TextBlock>(trackpad), text => text.Text.StartsWith("Using global default:", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void DeviceDisclosureCollapsesAfterLeavingAndReturningToPage()
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
            var token = manager.CreatePairingToken();
            using var key = new PairingTestKey();
            Assert.True(manager.AcceptPairing("client-a", "Phone", token, reconnectPublicKey: key.PublicKey).Accepted);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                var device = FindVisualDescendants<Expander>(window)
                    .First(expander => expander.Header is DeviceListItem);
                device.IsExpanded = true;
                window.UpdateLayout();
                Assert.True(device.IsExpanded);

                window.ShowPage(HostPage.Preferences);
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();

                Assert.All(
                    FindVisualDescendants<Expander>(window).Where(expander => expander.Header is DeviceListItem),
                    expander => Assert.False(expander.IsExpanded));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void CollapsedDeviceStaysCollapsedAfterConnectionRefresh()
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
            var token = manager.CreatePairingToken();
            using var key = new PairingTestKey();
            Assert.True(manager.AcceptPairing("client-a", "Phone", token, reconnectPublicKey: key.PublicKey).Accepted);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                var originalPage = window.PageContent.Content;
                var device = FindVisualDescendants<Expander>(window)
                    .First(expander => expander.Header is DeviceListItem);
                device.IsExpanded = true;
                DoWpfEvents();
                Assert.True(device.IsExpanded);

                device.IsExpanded = false;
                DoWpfEvents();
                Assert.False(device.IsExpanded);

                using var connection = manager.TrackConnection("client-a");
                WaitForWpf(
                    () => !ReferenceEquals(originalPage, window.PageContent.Content),
                    "Devices page connection refresh");
                window.UpdateLayout();

                Assert.All(
                    FindVisualDescendants<Expander>(window).Where(expander => expander.Header is DeviceListItem),
                    expander => Assert.False(expander.IsExpanded));
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void DiagnosticsRendersStructuredApplicationLogAsThemedCards()
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
            var appLog = new FakeAppLog(
                new AppLogRecord(
                    DateTimeOffset.Now,
                    "host_action",
                    "windows_host",
                    null,
                    null,
                    "enable_windows_locking",
                    "failed",
                    "VAIR-LOCK-POLICY-ACCESS-DENIED",
                    null,
                    "Access was denied."));
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), appLog: appLog, isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, appLog: appLog);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Diagnostics);
                window.UpdateLayout();
                WaitForWpf(
                    () => FindWpfDescendants<PillBadge>(window).Any(badge => Equals(badge.Content, "Host action")),
                    "initial log render");

                Assert.Contains(FindWpfDescendants<PillBadge>(window), badge => Equals(badge.Content, "Host action"));
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "enable windows locking");
                Assert.Contains(
                    FindWpfDescendants<PillBadge>(window),
                    badge => Equals(badge.Content, "Windows host") && badge.Tone == PillBadgeTone.Outline);
                Assert.Contains(
                    FindWpfDescendants<PillBadge>(window),
                    badge => Equals(badge.Content, "VAIR-LOCK-POLICY-ACCESS-DENIED") && badge.Tone == PillBadgeTone.DangerOutline);
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Access was denied.");
                var hostActionBadge = FindWpfDescendants<PillBadge>(window)
                    .Single(badge => Equals(badge.Content, "Host action"));
                Assert.Equal(PillBadgeTone.DangerOutline, hostActionBadge.Tone);
                Assert.Same(window.Resources["DangerBrush"], hostActionBadge.BorderBrush);

                var previousReadCount = appLog.ReadCount;
                FindWpfDescendants<Button>(window)
                    .Single(button => button.Content?.ToString() == "Refresh")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForWpf(() => appLog.ReadCount > previousReadCount, "manual refresh read");
                Thread.Sleep(50);
                DoWpfEvents();

                var unchangedHostActionBadge = FindWpfDescendants<PillBadge>(window)
                    .Single(badge => Equals(badge.Content, "Host action"));
                Assert.Same(hostActionBadge, unchangedHostActionBadge);

            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ConfirmationDialogUsesWpfThemedControls()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var dialog = new ThemedConfirmationDialog(
                "Clean up duplicates",
                "Remove 2 older disconnected duplicate pairings? Connected devices are kept.",
                "Clean up",
                "Cancel",
                ConfirmationTone.Question);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                Assert.Same(dialog.Resources["WindowBrush"], dialog.Background);
                Assert.Contains(FindWpfDescendants<TextBlock>(dialog), text => text.Text == "Clean up duplicates");
                Assert.Contains(FindWpfDescendants<Button>(dialog), button => button.Content?.ToString() == "Clean up");
                Assert.Contains(FindWpfDescendants<Button>(dialog), button => button.Content?.ToString() == "Cancel");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static IEnumerable<T> FindWpfDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T directMatch)
            {
                yield return directMatch;
            }

            if (child is not DependencyObject childObject)
            {
                continue;
            }

            foreach (var descendant in FindWpfDescendants<T>(childObject))
            {
                yield return descendant;
            }
        }
    }

    private static void DoWpfEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            frame.Continue = false;
        }), DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void InformationDialogUsesASingleOkAction()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var dialog = new ThemedConfirmationDialog(
                "Setting information",
                "Explains the setting.",
                "OK",
                null,
                ConfirmationTone.Information);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                var action = Assert.Single(FindWpfDescendants<Button>(dialog));
                Assert.Equal("OK", action.Content);
                Assert.True(action.IsDefault);
                Assert.DoesNotContain(FindWpfDescendants<Button>(dialog), button => button.IsCancel);
                Assert.Contains(FindWpfDescendants<TextBlock>(dialog), text => text.Text == "i");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static void WaitForWpf(Func<bool> condition, string expectation = "WPF update")
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"The expected {expectation} did not complete.");
            }

            DoWpfEvents();
            Thread.Sleep(10);
        }
    }

    private static Button? FindPermissionButton(DependencyObject root, string label, string text)
    {
        foreach (var card in FindVisualDescendants<Border>(root))
        {
            if (card.DataContext is not DevicePermissionItem)
            {
                continue;
            }

            if (!FindVisualDescendants<TextBlock>(card).Any(textBlock => string.Equals(textBlock.Text, label, StringComparison.Ordinal)))
            {
                continue;
            }

            return FindVisualDescendants<Button>(card)
                .SingleOrDefault(button => string.Equals(button.Content?.ToString(), text, StringComparison.Ordinal));
        }

        return null;
    }

    private static void ExpandDevicePermissions(Window window)
    {
        var device = FindVisualDescendants<Expander>(window)
            .First(expander => expander.Header is DeviceListItem);
        device.IsExpanded = true;
        window.UpdateLayout();

        var permissions = FindVisualDescendants<Expander>(window)
            .First(expander => string.Equals(expander.Header as string, "Permissions", StringComparison.Ordinal));
        permissions.IsExpanded = true;
        window.UpdateLayout();
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class WpfApplicationScope : IDisposable
    {
        private readonly Application? _created;

        public WpfApplicationScope()
        {
            if (Application.Current is null)
            {
                _created = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
        }

        public void Dispose()
        {
            _created?.Shutdown();
        }
    }

    private sealed class FakeWorkstationLockPolicy : IWorkstationLockPolicy
    {
        public FakeWorkstationLockPolicy(WorkstationLockPolicyState state)
        {
            State = state;
        }

        public WorkstationLockPolicyState State { get; private set; }

        public int EnableCalls { get; private set; }

        public event EventHandler? Changed;

        public WorkstationLockPolicyStatus GetStatus() => new(State);

        public WorkstationLockEnableResult TryEnable()
        {
            EnableCalls += 1;
            State = WorkstationLockPolicyState.NotExplicitlyDisabled;
            Changed?.Invoke(this, EventArgs.Empty);
            return new WorkstationLockEnableResult(true, "Windows locking is enabled for this user.");
        }
    }

    private sealed class FakeAppLog(params AppLogRecord[] entries) : IAppLog
    {
        private readonly object _eventGate = new();
        private EventHandler? _changed;
        private int _readCount;
        private int _completedReadCount;
        private int _subscriberCount;
        private int _deliveredChangeCount;
        private int _throwOnNextRead;
        private int _failedReadCount;
        private ManualResetEventSlim? _readGate;

        public string LogDirectory => string.Empty;

        public int ReadCount => Volatile.Read(ref _readCount);

        public int CompletedReadCount => Volatile.Read(ref _completedReadCount);

        public int SubscriberCount => Volatile.Read(ref _subscriberCount);

        public int DeliveredChangeCount => Volatile.Read(ref _deliveredChangeCount);

        public int FailedReadCount => Volatile.Read(ref _failedReadCount);

        public ManualResetEventSlim ReadEntered { get; private set; } = new(false);

        public bool ThrowOnNextRead
        {
            get => Volatile.Read(ref _throwOnNextRead) != 0;
            set => Volatile.Write(ref _throwOnNextRead, value ? 1 : 0);
        }

        public event EventHandler? Changed
        {
            add
            {
                lock (_eventGate)
                {
                    _changed += value;
                    Interlocked.Increment(ref _subscriberCount);
                }
            }
            remove
            {
                lock (_eventGate)
                {
                    _changed -= value;
                    Interlocked.Decrement(ref _subscriberCount);
                }
            }
        }

        public void Write(AppLogEntry entry)
        {
            RaiseChanged();
        }

        public AppLogReadResult Read(AppLogQuery query)
        {
            Interlocked.Increment(ref _readCount);
            var readGate = Volatile.Read(ref _readGate);
            if (readGate is not null)
            {
                ReadEntered.Set();
                readGate.Wait();
            }

            if (Interlocked.Exchange(ref _throwOnNextRead, 0) != 0)
            {
                Interlocked.Increment(ref _failedReadCount);
                throw new IOException("Expected application log read failure.");
            }

            Interlocked.Increment(ref _completedReadCount);
            return new(true, entries);
        }

        public AppLogDeleteResult DeleteAll() => new(true, 0);

        public void BlockReads()
        {
            ReadEntered = new ManualResetEventSlim(false);
            Volatile.Write(ref _readGate, new ManualResetEventSlim(false));
        }

        public void ReleaseReads()
        {
            Interlocked.Exchange(ref _readGate, null)?.Set();
        }

        public void RaiseChanged(int count = 1)
        {
            EventHandler? changed;
            lock (_eventGate)
            {
                changed = _changed;
            }

            for (var index = 0; index < count; index += 1)
            {
                changed?.Invoke(this, EventArgs.Empty);
                Interlocked.Increment(ref _deliveredChangeCount);
            }
        }
    }
}
