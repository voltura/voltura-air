using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VolturaAir.Host.Features.Connection;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ConnectionPageShowsSelectedAdapterWithAccessibleIndicator()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var candidate = LanAddressSelector.CreateCandidate(
                IPAddress.Parse("192.168.1.20"),
                "Wi-Fi",
                "Wireless adapter",
                NetworkInterfaceType.Wireless80211,
                hasIpv4Gateway: true);
            var item = Assert.Single(ConnectionCandidateItem.Create([candidate], candidate));
            var page = new ConnectionPageView(
                static () => { },
                static () => { },
                static () => { },
                static () => { },
                static _ => { },
                static _ => { },
                static () => { },
                static () => { });
            page.Candidates = [item];
            page.IsAdapterChooserOpen = true;
            var list = Assert.Single(FindWpfDescendants<ListBox>(page));
            var window = new Window { Content = page, Width = 900, Height = 600 };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal("Recommended", item.Status);
                Assert.Equal("Wi-Fi — Wireless adapter", item.Adapter);
                Assert.Contains("192.168.1.20", item.AccessibleName, StringComparison.Ordinal);
                Assert.Same(item, list.SelectedItem);
                Assert.True(double.IsPositiveInfinity(list.MaxHeight));
                Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(list));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ConnectionPageSwitchesAndValidatesManualPortMode()
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
            var window = new MainWindow(manager, webHost, clientUrl: null)
            {
                Width = 720,
                Height = 540
            };
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();

                var advanced = FindWpfDescendants<Expander>(window).Single(expander => expander.Name == "AdvancedConnectionExpander");
                var custom = FindWpfDescendants<CheckBox>(window).Single(checkBox => checkBox.Name == "UseSpecificPortCheckBox");
                var port = FindWpfDescendants<TextBox>(window).Single(textBox => textBox.Name == "ManualPortTextBox");
                var validation = FindWpfDescendants<TextBlock>(window).Single(text => text.Name == "ManualPortValidationText");
                var scroller = FindWpfDescendants<ScrollViewer>(window).Single(viewer => viewer.Name == "ConnectionScrollViewer");
                var actionPanel = FindWpfDescendants<Border>(window).Single(border => border.Name == "PendingActionsPanel");

                Assert.False(advanced.IsExpanded);
                Assert.StartsWith("Port settings, Automatic · ", AutomationProperties.GetName(advanced), StringComparison.Ordinal);
                Assert.Equal(Visibility.Collapsed, port.Parent is FrameworkElement parent ? parent.Visibility : Visibility.Visible);
                advanced.IsExpanded = true;
                custom.IsChecked = true;
                WaitForWpf(() => port.IsKeyboardFocusWithin, "custom port focus");
                window.UpdateLayout();
                Assert.True(custom.IsChecked);
                Assert.Equal("Use a custom port", custom.Content);
                Assert.True(port.IsVisible);
                Assert.Equal(180, port.Width);
                Assert.Equal("Port is available.", validation.Text);
                var validPortLeft = port.TranslatePoint(new System.Windows.Point(0, 0), advanced).X;
                var validationBottom = validation.TranslatePoint(new System.Windows.Point(0, validation.ActualHeight), scroller).Y;
                Assert.InRange(validationBottom, 0, scroller.ViewportHeight + 0.5);
                Assert.True(actionPanel.IsVisible);
                Assert.Contains("Automatic → Custom", FindNamedText(window, "PendingPortChangeText"), StringComparison.Ordinal);
                Assert.Equal("Restart required", FindNamedText(window, "PendingActionHeadingText"));
                var actionRoot = Assert.IsAssignableFrom<FrameworkElement>(actionPanel.Parent);
                var actionBottom = actionPanel.TranslatePoint(new System.Windows.Point(0, actionPanel.ActualHeight), actionRoot).Y;
                Assert.InRange(actionBottom, 0, actionRoot.ActualHeight + 0.5);

                foreach (var button in FindWpfDescendants<Button>(actionPanel).Where(button => button.IsVisible))
                {
                    var buttonBottomRight = button.TranslatePoint(
                        new System.Windows.Point(button.ActualWidth, button.ActualHeight),
                        actionPanel);
                    Assert.InRange(buttonBottomRight.X, 0, actionPanel.ActualWidth + 0.5);
                    Assert.InRange(buttonBottomRight.Y, 0, actionPanel.ActualHeight + 0.5);
                }

                port.Text = "49151";
                window.UpdateLayout();
                Assert.Contains("49152", validation.Text, StringComparison.Ordinal);
                var invalidPortLeft = port.TranslatePoint(new System.Windows.Point(0, 0), advanced).X;
                Assert.Equal(validPortLeft, invalidPortLeft, precision: 3);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ConnectionAdapterChoicesUsePageScrollWheel()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var candidates = Enumerable.Range(20, 8)
                .Select(lastOctet => LanAddressSelector.CreateCandidate(
                    IPAddress.Parse($"192.168.1.{lastOctet}"),
                    $"Adapter {lastOctet}",
                    "Long network adapter description used to exercise page scrolling",
                    NetworkInterfaceType.Ethernet,
                    hasIpv4Gateway: lastOctet == 20,
                    adapterId: $"adapter-{lastOctet}"))
                .ToArray();
            var items = ConnectionCandidateItem.Create(candidates, candidates[0]);
            var page = new ConnectionPageView(
                static () => { },
                static () => { },
                static () => { },
                static () => { },
                static _ => { },
                static _ => { },
                static () => { },
                static () => { })
            {
                Candidates = items,
                IsAdapterChooserOpen = true
            };
            var window = new Window { Content = page, Width = 720, Height = 420 };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                var list = FindWpfDescendants<ListBox>(page).Single();
                var scroller = FindWpfDescendants<ScrollViewer>(page).Single(viewer => viewer.Name == "ConnectionScrollViewer");
                Assert.True(scroller.ScrollableHeight > 0);

                list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                });
                window.UpdateLayout();

                Assert.True(scroller.VerticalOffset > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false, "Automatic")]
    [InlineData(true, "Adapter: Automatic · Port: Custom")]
    public void ConnectionSummaryReflectsActualRuntimeSelectionModes(
        bool customPort,
        string expectedSelectionMode)
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            AppNetworkSettings.Save(CreateSettings(customAdapter: false, customPort));
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();

                Assert.Equal(expectedSelectionMode, FindNamedText(window, "ActiveSelectionModeText"));
                var activeAdapter = FindNamedText(window, "ActiveAdapterText");
                Assert.Single(FindWpfDescendants<TextBlock>(window), text => text.Text == activeAdapter);
                Assert.DoesNotContain(
                    FindWpfDescendants<TextBlock>(window),
                    text => text.IsVisible && text.Text == "Network adapter");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    private static string FindNamedText(DependencyObject root, string name) =>
        FindWpfDescendants<TextBlock>(root).Single(text => text.Name == name).Text;

    private static NetworkSettingsSnapshot CreateSettings(bool customAdapter, bool customPort) => new(
        customAdapter ? NetworkSelectionMode.Manual : NetworkSelectionMode.Automatic,
        customAdapter ? "192.168.1.20" : null,
        customAdapter ? "adapter-id" : null,
        customAdapter ? "Wi-Fi (Test adapter)" : null,
        customPort ? PortSelectionMode.Manual : PortSelectionMode.Automatic,
        customPort ? 51395 : null,
        LastAutomaticPort: null,
        LastAutomaticHostAddress: null);
}
