using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VolturaAir.Host.Features.Connection;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ConnectionPageShowsSelectedAdapterAsPill()
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
                [item],
                useAutomaticNetwork: true,
                useAutomaticPort: true,
                manualPort: "51395",
                currentHostUrl: "http://192.168.1.20:51395",
                selectedIp: "192.168.1.20",
                selectedPort: "51395",
                status: "Ready",
                save: static () => { },
                refresh: static () => { });
            var list = Assert.Single(FindWpfDescendants<ListBox>(page));
            var templateRoot = Assert.IsAssignableFrom<FrameworkElement>(list.ItemTemplate.LoadContent());
            templateRoot.DataContext = item;
            var window = new Window { Content = templateRoot, Width = 900, Height = 600 };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VolturaAir.Host;component/MainWindow.Styles.xaml", UriKind.Relative)
            });
            WpfTheme.Apply(window);
            var selectedBrush = window.Resources["AccentStrongBrush"];
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal("Recommended", item.Status);
                var container = Assert.IsType<Grid>(templateRoot.FindName("SelectedStatusPillContainer"));
                var pill = Assert.IsType<PillBadge>(templateRoot.FindName("SelectedStatusPill"));
                _ = pill.ApplyTemplate();
                var chrome = Assert.IsType<Border>(pill.Template.FindName("PillChrome", pill));
                Assert.Equal(Visibility.Visible, container.Visibility);
                Assert.Equal(24, pill.ActualHeight);
                Assert.Equal(new Thickness(10, 0, 10, 0), pill.Padding);
                Assert.Equal((CornerRadius)window.Resources["RadiusLarge"], chrome.CornerRadius);
                Assert.True(pill.ActualWidth > pill.ActualHeight * 2);
                Assert.Same(selectedBrush, pill.Background);
                Assert.Equal(PillBadgeTone.Accent, pill.Tone);
                Assert.Equal("Selected", pill.Content);
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
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.Show();
                window.ShowPage(HostPage.Connection);
                window.UpdateLayout();

                var automatic = FindWpfDescendants<ToggleButton>(window).Single(button => button.Name == "PortAutomaticButton");
                var manual = FindWpfDescendants<ToggleButton>(window).Single(button => button.Name == "PortManualButton");
                var port = FindWpfDescendants<TextBox>(window).Single(textBox => textBox.Name == "ManualPortTextBox");
                var validation = FindWpfDescendants<TextBlock>(window).Single(text => text.Name == "ManualPortValidationText");

                automatic.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(port.IsEnabled);
                Assert.True(automatic.IsChecked);
                Assert.False(manual.IsChecked);

                manual.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(port.IsEnabled);
                Assert.False(automatic.IsChecked);
                Assert.True(manual.IsChecked);

                port.Text = "49151";
                Assert.Contains("49152", validation.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }
}
