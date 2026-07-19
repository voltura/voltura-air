using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;
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
                window.Show();
                var progress = Assert.Single(FindWpfDescendants<ProgressBar>(window));
                var progressGroup = Assert.IsType<VolturaAir.Host.Ui.SpacingStackPanel>(progress.Parent);
                Assert.Equal(UiTokens.SpaceLg, progressGroup.Spacing);
                Assert.Contains(
                    progressGroup.Children.OfType<TextBlock>(),
                    text => string.Equals(text.Text, "Starting connection services.", StringComparison.Ordinal));
                window.ShowError("Could not bind to port.", "details");
                window.UpdateLayout();

                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Voltura Air could not start.");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Could not bind to port.");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Copy details");
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
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connect");
                Assert.Equal("New code", FindPairingCodeAction(window, "New code").Content);

                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connection");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Save");

                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Devices");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Device details");

                window.ShowPage(HostPage.Preferences);
                window.UpdateLayout();
                var sections = FindWpfDescendants<Expander>(window).ToArray();
                Assert.Equal(15, sections.Length);
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

                var devices = Assert.Single(FindWpfDescendants<ListBox>(window));
                devices.SelectedIndex = 0;
                WaitForWpf(() => FindPermissionButton(window, "Blackout display", "✓ Allow") is not null, "blackout display effective state");

                var blackoutAllow = Assert.IsType<Button>(FindPermissionButton(window, "Blackout display", "✓ Allow"));
                var clipboardBlock = Assert.IsType<Button>(FindPermissionButton(window, "Read PC clipboard", "✓ Block"));
                Assert.Same(window.Resources["AccentBrush"], blackoutAllow.Background);
                Assert.Equal(new Thickness(2), clipboardBlock.BorderThickness);
                var urlAllow = Assert.IsType<Button>(FindPermissionButton(window, "Open web addresses", "✓ Allow"));
                Assert.Same(window.Resources["AccentBrush"], urlAllow.Background);
                Assert.DoesNotContain(FindWpfDescendants<TextBlock>(window), text => text.Text == "Presentation control");

                AppDeveloperSettings.SetEnableAlphaFeatures(true);
                window.ShowPage(HostPage.Devices);
                window.UpdateLayout();
                var refreshedDevices = Assert.Single(FindWpfDescendants<ListBox>(window));
                refreshedDevices.SelectedIndex = 0;
                WaitForWpf(
                    () => FindWpfDescendants<TextBlock>(window).Any(text => text.Text == "Presentation control"),
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
    public void InformationDialogUsesASingleCloseAction()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var dialog = new ThemedConfirmationDialog(
                "Cursor recovery watchdog",
                "Explains the recovery behavior.",
                "Close",
                null,
                ConfirmationTone.Warning);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                var action = Assert.Single(FindWpfDescendants<Button>(dialog));
                Assert.Equal("Close", action.Content);
                Assert.True(action.IsDefault);
                Assert.DoesNotContain(FindWpfDescendants<Button>(dialog), button => button.IsCancel);
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
        foreach (var panel in FindWpfDescendants<StackPanel>(root))
        {
            for (var index = 0; index < panel.Children.Count - 1; index++)
            {
                if (panel.Children[index] is not TextBlock { Text: var rowLabel } ||
                    !string.Equals(rowLabel, label, StringComparison.Ordinal) ||
                    panel.Children[index + 1] is not StackPanel row)
                {
                    continue;
                }

                return row.Children
                    .OfType<Button>()
                    .SingleOrDefault(button => string.Equals(button.Content?.ToString(), text, StringComparison.Ordinal));
            }
        }

        return null;
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
        private int _readCount;

        public string LogDirectory => string.Empty;

        public int ReadCount => Volatile.Read(ref _readCount);

        public event EventHandler? Changed;

        public void Write(AppLogEntry entry)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public AppLogReadResult Read(AppLogQuery query)
        {
            Interlocked.Increment(ref _readCount);
            return new(true, entries);
        }

        public AppLogDeleteResult DeleteAll() => new(true, 0);
    }
}
