using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolturaAir.Host;
using VolturaAir.Host.Features.Connect;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ConnectPageShowsPairingCodeRefreshCountdown()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var settingsScope = HostSettingsRegistry.BeginIsolatedScope();
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                window.ShowPage(HostPage.Connect);
                window.UpdateLayout();

                Assert.Equal("/pair", new Uri(window.PairingUrl).AbsolutePath);
                var codeCard = FindWpfDescendants<InfoCard>(window).Single(card => card.Title == "QR code");
                var qrImage = FindWpfDescendants<System.Windows.Controls.Image>(window).Single(image => image.Name == "QrCodeImage");
                var qrSource = Assert.IsAssignableFrom<BitmapSource>(qrImage.Source);
                Assert.StartsWith("Refreshes in ", codeCard.Value, StringComparison.Ordinal);
                Assert.True(qrSource.IsFrozen);
                Assert.True(qrSource.PixelWidth > 1);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void MainWindowReplacesConsumedPairingCodeForKnownClient()
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
            var initialToken = manager.CreatePairingToken();
            var initialPairing = manager.Accept("client-a", "Phone", initialToken, null);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            try
            {
                var displayedUrl = window.PairingUrl;
                var displayedToken = new Uri(displayedUrl).Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Single(part => part[0] == "t")[1];

                var repaired = manager.Accept(
                    "client-a",
                    "Phone",
                    Uri.UnescapeDataString(displayedToken),
                    initialPairing.Secret);
                DoWpfEvents();

                Assert.True(repaired.Accepted);
                Assert.Equal(1, manager.PairedDeviceCount);
                Assert.NotEqual(displayedUrl, window.PairingUrl);
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ConnectCountdownRequestsOneRefreshAndStopsAfterUnload()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var refreshCalls = 0;
            var now = DateTimeOffset.UtcNow;
            var view = new ConnectPageView(
                CreateTestBitmap(),
                "Ready to pair",
                "http://pc.local/pair?t=redacted",
                "http://pc.local",
                "Ethernet (Test adapter)",
                false,
                "127.0.0.1",
                "51395",
                null,
                null,
                null,
                now,
                () => refreshCalls += 1,
                static () => { },
                () => now);

            view.ProcessCountdown();
            view.ProcessCountdown();
            Assert.Equal(1, refreshCalls);
            Assert.Equal(Visibility.Collapsed, view.SelectedAdapterCard.Visibility);
            Assert.Equal("Refreshing code…", view.PairingCodeCard.Value);

            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            view.ProcessCountdown();
            Assert.Equal(1, refreshCalls);
        });
    }

    [Fact]
    public void PairingCountdownUsesMinuteSecondFormat()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("Refreshes in 4:45", ConnectPageView.FormatRefreshCountdown(now.AddMinutes(4).AddSeconds(45), now));
        Assert.Equal("Refreshes in 0:00", ConnectPageView.FormatRefreshCountdown(now.AddMilliseconds(-1), now));
    }

    [Fact]
    public void ConnectDetailsUseRemainingSpaceAndOwnScrolling()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var settingsScope = HostSettingsRegistry.BeginIsolatedScope();
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null)
            {
                Width = 920,
                Height = 620
            };
            try
            {
                window.ShowPage(HostPage.Connect);
                var windowContent = Assert.IsType<Grid>(window.Content);
                var compactClientSize = new Size(920, 580);
                windowContent.Measure(compactClientSize);
                windowContent.Arrange(new Rect(compactClientSize));
                windowContent.UpdateLayout();

                var codeCard = FindWpfDescendants<InfoCard>(window).Single(card => card.Title == "QR code");
                var actions = Assert.IsType<SpacingStackPanel>(codeCard.Actions);
                Assert.Collection(
                    actions.Children.OfType<Button>(),
                    button => Assert.Equal("New code", button.Content),
                    button => Assert.Equal("Copy link", button.Content));

                var details = FindWpfDescendants<Expander>(window).Single(expander => expander.Header is "Details");
                Assert.Same(window.Resources["BoundedAccordionStyle"], details.Style);
                var scroller = Assert.Single(FindWpfDescendants<ScrollViewer>(details));
                details.IsExpanded = true;
                windowContent.Measure(compactClientSize);
                windowContent.Arrange(new Rect(compactClientSize));
                windowContent.UpdateLayout();

                Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
                Assert.True(scroller.ViewportHeight > 0, $"Viewport height was {scroller.ViewportHeight}.");
                Assert.True(scroller.ScrollableHeight > 0, $"Scrollable height was {scroller.ScrollableHeight}; viewport {scroller.ViewportHeight}; extent {scroller.ExtentHeight}; actual {scroller.ActualHeight}.");
                Assert.True(scroller.ActualHeight < scroller.ExtentHeight, $"Actual height was {scroller.ActualHeight}; extent was {scroller.ExtentHeight}.");
            }
            finally
            {
                window.Close();
                DisposeWebHost(webHost);
            }
        });
    }

    [Fact]
    public void ConnectWarningEmphasizesTheSelectedAdapterName()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            const string adapterName = "Wi-Fi (Intel Wireless Adapter)";
            const string warning = "Multiple network adapters found. Voltura Air selected Wi-Fi (Intel Wireless Adapter). If your phone cannot connect, choose the adapter connected to the same Wi-Fi/LAN.";
            var now = DateTimeOffset.UtcNow;
            var view = new ConnectPageView(
                CreateTestBitmap(),
                "Ready to pair",
                "http://pc.local/pair?t=redacted",
                "http://pc.local",
                adapterName,
                true,
                "192.168.1.10",
                "51395",
                warning,
                adapterName,
                null,
                now.AddMinutes(5),
                static () => { },
                static () => { });

            var runs = view.AddressWarningText.Inlines.OfType<Run>().ToArray();
            Assert.Equal(3, runs.Length);
            Assert.Equal("Multiple network adapters found. Voltura Air selected ", runs[0].Text);
            Assert.Equal(adapterName, runs[1].Text);
            Assert.Equal(FontWeights.Bold, runs[1].FontWeight);
            Assert.Equal(". If your phone cannot connect, choose the adapter connected to the same Wi-Fi/LAN.", runs[2].Text);
            Assert.Equal(adapterName, view.SelectedAdapterCard.Value);
            Assert.Equal(Visibility.Visible, view.SelectedAdapterCard.Visibility);
        });
    }

    private static Button FindPairingCodeAction(DependencyObject root, string label)
    {
        var codeCard = FindWpfDescendants<InfoCard>(root).Single(card => card.Title == "QR code");
        var actions = Assert.IsAssignableFrom<Panel>(codeCard.Actions);
        return actions.Children.OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), label, StringComparison.Ordinal));
    }

    private static BitmapSource CreateTestBitmap()
    {
        return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
    }
}
