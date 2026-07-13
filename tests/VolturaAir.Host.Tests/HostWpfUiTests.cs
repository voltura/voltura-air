using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
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
                window.Show();
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Connect");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "New code");

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
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Start Voltura Air hidden in the tray");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Show Voltura Air when the last device disconnects");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Write application log");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Keep application logs for");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Developer mode");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Allow paired devices to lock the PC");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Allow paired devices to turn off displays");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Allow paired devices to sign out");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Allow paired devices to restart the PC");
                Assert.Contains(FindWpfDescendants<CheckBox>(window), checkBox => checkBox.Content?.ToString() == "Allow paired devices to shut down the PC");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Default remote mode");
                Assert.Contains(FindWpfDescendants<ToggleButton>(window), button => button.Content?.ToString() == "Kodi");
                Assert.Contains(
                    FindWpfDescendants<ComboBox>(window),
                    combo => ReferenceEquals(window.Resources["ModernComboBoxStyle"], combo.Style));
                var preferenceSections = FindWpfDescendants<Expander>(window).ToArray();
                Assert.Equal(7, preferenceSections.Length);
                var preferencesScroller = Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                Assert.Equal(ScrollBarVisibility.Visible, preferencesScroller.VerticalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, preferencesScroller.HorizontalScrollBarVisibility);
                Assert.All(preferenceSections, section =>
                {
                    Assert.False(section.IsExpanded);
                    Assert.Same(window.Resources["PreferencesAccordionStyle"], section.Style);
                });
                preferenceSections[0].IsExpanded = true;
                preferenceSections[1].IsExpanded = true;
                Assert.False(preferenceSections[0].IsExpanded);
                Assert.True(preferenceSections[1].IsExpanded);
                Assert.Single(preferenceSections, section => section.IsExpanded);

                window.ShowPage(HostPage.Diagnostics);
                window.UpdateLayout();
                var applicationLogView = FindWpfDescendants<ToggleButton>(window)
                    .Single(button => button.Content?.ToString() == "Application log");
                var systemDetailsView = FindWpfDescendants<ToggleButton>(window)
                    .Single(button => button.Content?.ToString() == "System details");
                Assert.True(applicationLogView.IsChecked);
                Assert.False(systemDetailsView.IsChecked);
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Application log");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "No matching log entries.");
                Assert.DoesNotContain(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Apply filters");
                Assert.Contains(FindWpfDescendants<Button>(window), button => AutomationProperties.GetName(button) == "Clear filters");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Copy filtered log");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Open log folder");
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Delete logs");
                Assert.Single(FindWpfDescendants<ScrollViewer>(window));
                var dateRange = Assert.Single(FindWpfDescendants<ModernDateRangePicker>(window));
                Assert.Equal(DateTime.Today, dateRange.SelectedEndDate);
                Assert.Empty(FindWpfDescendants<DatePicker>(window));
                Assert.Single(FindWpfDescendants<EventMultiSelectFilter>(window));
                Assert.All(
                    FindWpfDescendants<ComboBox>(window),
                    combo => Assert.Same(window.Resources["ModernComboBoxStyle"], combo.Style));
                Assert.Empty(FindWpfDescendants<TextBox>(window));

                systemDetailsView.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
                window.UpdateLayout();
                Assert.True(systemDetailsView.IsChecked);
                Assert.Empty(FindWpfDescendants<ModernDateRangePicker>(window));
                Assert.Contains(FindWpfDescendants<Button>(window), button => button.Content?.ToString() == "Copy diagnostics");

                applicationLogView.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
                window.UpdateLayout();
                Assert.True(applicationLogView.IsChecked);
                Assert.Single(FindWpfDescendants<ModernDateRangePicker>(window));
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
                var accepted = manager.Accept("client-a", "Phone", Uri.UnescapeDataString(token), null);
                DoWpfEvents();

                Assert.True(accepted.Accepted);
                Assert.NotEqual(initialPairingUrl, window.PairingUrl);
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();

                var copyLink = FindWpfDescendants<Button>(window)
                    .Single(button => button.Content?.ToString() == "Copy link");
                copyLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DoWpfEvents();

                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Link copied");
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
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
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Host action");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "enable windows locking");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Windows host");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "VAIR-LOCK-POLICY-ACCESS-DENIED");
                Assert.Contains(FindWpfDescendants<TextBlock>(window), text => text.Text == "Access was denied.");
                var hostActionBadge = FindWpfDescendants<Border>(window)
                    .Single(border => border.Child is TextBlock text && text.Text == "Host action");
                Assert.Same(window.Resources["DangerBrush"], hostActionBadge.BorderBrush);
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    [Theory]
    [InlineData("http://192.168.68.51:5173", "http://192.168.68.51:51395", "51395")]
    [InlineData("http://192.168.68.51:5173", "http://10.0.0.20:51395", "http://10.0.0.20:51395")]
    public void HostHintUsesCompactPortOnlyForSameHost(string clientUrl, string serverUrl, string expectedHint)
    {
        Assert.Equal(expectedHint, MainWindow.CreateHostHint(clientUrl, serverUrl));
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
        public string LogDirectory => string.Empty;

        public void Write(AppLogEntry entry)
        {
        }

        public AppLogReadResult Read(AppLogQuery query) => new(true, entries);

        public AppLogDeleteResult DeleteAll() => new(true, 0);
    }
}
