using System.Windows;
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
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void MainWindowReplacesPairingCodeThatIsDueForRefresh()
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
                var initialUrl = window.PairingUrl;

                var refreshed = window.RefreshPairingCodeIfDue(DateTimeOffset.UtcNow.Add(PairingManager.TokenLifetime));

                Assert.True(refreshed);
                Assert.NotEqual(initialUrl, window.PairingUrl);
            }
            finally
            {
                window.Close();
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
                webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
                "127.0.0.1",
                "51395",
                null,
                null,
                now,
                () => refreshCalls += 1,
                static () => { },
                () => now);

            view.ProcessCountdown();
            view.ProcessCountdown();
            Assert.Equal(1, refreshCalls);
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

    private static BitmapSource CreateTestBitmap()
    {
        return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
    }
}
