using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.Connect;
using VolturaAir.Host.Features.Preferences;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Theory]
    [InlineData(920, 620, AppThemeMode.Light, false)]
    [InlineData(1160, 760, AppThemeMode.Dark, false)]
    [InlineData(920, 620, AppThemeMode.System, true)]
    public void IntroductionKeepsPairingVisibleAndReplaysFromApplication(
        double width, double height, AppThemeMode theme, bool highContrast)
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var key = new PairingTestKey();
            using var inputInjector = new SendInputInjector();
            AppThemeSettings.SetMode(theme);
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var introduction = new IntroductionController(hasRememberedDevices: false);
            var window = new MainWindow(manager, webHost, clientUrl: null,
                usePublicScreenshotPairingUrl: true, introduction: introduction)
            {
                Width = width,
                Height = height
            };
            try
            {
                Assert.False(introduction.IsVisible);
                Assert.True(manager.AcceptPairing("introduction-device", "Phone", manager.CreatePairingToken(),
                    reconnectPublicKey: key.PublicKey).Accepted);
                using var connection = manager.TrackConnection("introduction-device");
                WaitForWpf(() => introduction.IsVisible, "passive first connection introduction");
                Assert.False(window.IsVisible);
                window.ShowPage(HostPage.Connect);
                WpfTheme.Apply(window, highContrast);
                window.UpdateLayout();
                var card = Assert.Single(FindWpfDescendants<IntroductionCard>(window));
                var qr = FindWpfDescendants<System.Windows.Controls.Image>(window).Single(image => image.Name == "QrCodeImage");
                WaitForWpf(() => FindVisualDescendants<Button>(window).Any(button => Equals(button.Content, "Copy link")), "pairing actions layout");
                var copyLink = FindVisualDescendants<Button>(window).Single(button => Equals(button.Content, "Copy link"));
                var gotIt = FindWpfDescendants<Button>(card).Single(button => Equals(button.Content, "Got it"));
                Assert.True(qr.IsVisible);
                Assert.True(copyLink.IsVisible);
                Assert.True(gotIt.IsVisible);
                Assert.True(qr.ActualWidth > 100);
                var corner = gotIt.TranslatePoint(new System.Windows.Point(gotIt.ActualWidth, gotIt.ActualHeight), card);
                Assert.InRange(corner.Y, 0, card.ActualHeight + 0.5);

                var captureDirectory = Environment.GetEnvironmentVariable("VOLTURA_AIR_INTRODUCTION_CAPTURE_DIRECTORY");
                if (!string.IsNullOrWhiteSpace(captureDirectory))
                {
                    var root = FindWpfDescendants<ScrollViewer>(window).Single(view => view.Name == "WindowScrollViewer");
                    var task = WpfPngRenderer.SaveAsync(root, window.Background,
                        Path.Combine(captureDirectory, $"windows-{theme}-{width}-{highContrast}.png"),
                        new System.Windows.Size(width, height));
                    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
                    while (!task.IsCompleted && DateTimeOffset.UtcNow < deadline)
                    {
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.SystemIdle,
                            new Action(() => frame.Continue = false));
                        System.Windows.Threading.Dispatcher.PushFrame(frame);
                    }
                    Assert.True(task.IsCompleted, "Introduction capture must finish while its dispatcher is alive.");
                    task.GetAwaiter().GetResult();
                }

                gotIt.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Empty(FindWpfDescendants<IntroductionCard>(window));
                Assert.False(introduction.IsVisible);
                window.ShowPage(HostPage.Preferences);
                var preferences = Assert.IsType<PreferencesPageView>(FindWpfDescendants<PreferencesPageView>(window).Single());
                preferences.FindSection("Application")?.SetCurrentValue(Expander.IsExpandedProperty, true);
                var replay = FindWpfDescendants<Button>(window).Single(button => Equals(button.Content, "Show introduction"));
                replay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Single(FindWpfDescendants<IntroductionCard>(window));
            }
            finally
            {
                window.AllowClose();
                window.Close();
                DisposeWebHost(webHost);
                manager.DisposeHostIdentity();
            }
        });
    }
}
