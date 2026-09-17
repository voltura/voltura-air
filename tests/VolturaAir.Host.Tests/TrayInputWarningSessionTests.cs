using System.Windows.Threading;
using Microsoft.AspNetCore.TestHost;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class TrayInputWarningSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WarnsOnceUntilAllDevicesDisconnect(bool blockedBeforeConnection)
    {
        using var settings = HostSettingsRegistry.BeginIsolatedScope();
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var injector = new FakeInputInjector();
        var manager = new PairingManager(store.Store);
        Assert.True(manager.AcceptPairing("first", "Phone", manager.CreatePairingToken(),
            reconnectPublicKey: key.PublicKey).Accepted);
        Assert.True(manager.AcceptPairing("second", "Tablet", manager.CreatePairingToken(),
            reconnectPublicKey: key.PublicKey).Accepted);
        await using var webHost = new WebHostService(manager, new InputDispatcher(injector),
            isolatedTestMode: true, configureWebHost: builder => builder.UseTestServer());
        using var trayIcon = new Forms.NotifyIcon();
        var warnings = 0;
        using var presenter = new TrayNotificationPresenter(Dispatcher.CurrentDispatcher, trayIcon,
            (title, _, _) => { if (title == RemoteInputBlockedTrayNotification.Title) warnings++; });
        using var controller = new TrayConnectionFeedbackController(
            Dispatcher.CurrentDispatcher, manager, webHost, static _ => { },
            (title, message, icon, action) =>
            {
                if (title == RemoteInputBlockedTrayNotification.Title)
                    presenter.Enqueue(title, message, icon, action);
            },
            static () => true, static (_, _, _, _) => true, static () => { }, static _ => { });
        controller.Start();
        Pump();
        if (blockedBeforeConnection) webHost.SetInputBlockedByElevation(true);
        using var firstConnection = manager.TrackConnection("first");
        Pump();
        webHost.SetInputBlockedByElevation(true);
        Pump();
        Assert.Equal(1, warnings);

        presenter.OnClosed();
        // Shell focus can last longer than the foreground debounce during dismissal.
        // Deliver each settled state separately, after the notification has closed.
        for (var index = 0; index < 3; index++)
        {
            webHost.SetInputBlockedByElevation(false);
            Pump();
            webHost.SetInputBlockedByElevation(true);
            Pump();
        }
        Assert.Equal(1, warnings);
        Assert.True(presenter.IsAvailable);

        using var secondConnection = manager.TrackConnection("second");
        Pump();
        firstConnection.Dispose();
        Pump();
        webHost.SetInputBlockedByElevation(false);
        Pump();
        webHost.SetInputBlockedByElevation(true);
        Pump();
        Assert.Equal(1, warnings);

        secondConnection.Dispose();
        Pump();
        using var reconnected = manager.TrackConnection("first");
        Pump();
        Assert.Equal(2, warnings);
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.SystemIdle);
}
