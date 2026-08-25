using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host;
using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ReadyUpdateIsACompactDistinctActionAndCanBeOpenedFromNotification()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            using var directory = new UpdateTemporaryDirectory();
            var release = UpdateTestSupport.CreateRelease("1.1.0");
            var pending = Path.Combine(directory.Path, "pending");
            UpdateTestSupport.WriteReadyPackage(pending, release);
            var modifyInstaller = Path.Combine(directory.Path, "VolturaAir-Modify.exe");
            File.WriteAllBytes(modifyInstaller, []);
            var manager = new PairingManager(store.Store);
            var updates = new UpdateService(
                manager,
                [],
                eligibleOverride: true,
                modifyInstallerOverride: modifyInstaller,
                pendingDirectoryOverride: pending,
                currentVersionOverride: new Version(1, 0, 9),
                manifestVerifier: static (_, _) => true);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null, updates: updates);
            try
            {
                window.Show();
                window.UpdateLayout();
                var updateButton = Assert.IsType<Button>(window.FindName("UpdateButton"));
                var connectButton = Assert.IsType<Button>(window.FindName("ConnectNavButton"));

                Assert.Equal(Visibility.Visible, updateButton.Visibility);
                Assert.Equal("Install update", updateButton.Content);
                Assert.Equal(36, updateButton.MinHeight);
                Assert.Equal(new Thickness(2), updateButton.BorderThickness);
                Assert.Contains("1.1.0", Assert.IsType<string>(updateButton.ToolTip), StringComparison.Ordinal);
                Assert.NotSame(updateButton.Style, connectButton.Style);
                Assert.Null(window.FindName("UpdateActionPanel"));
                Assert.Null(window.FindName("UpdateVersionText"));

                window.Hide();
                window.ShowReadyUpdate();
                window.Dispatcher.Invoke(static () => { }, DispatcherPriority.SystemIdle);

                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(updateButton.IsKeyboardFocused);
            }
            finally
            {
                window.AllowClose();
                window.Close();
                updates.DisposeAsync().AsTask().GetAwaiter().GetResult();
                DisposeWebHost(webHost);
            }
        });
    }
}
