using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VolturaAir.Host.Features.Connection;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ConnectionChooserOpenAndCancelDoNotCreatePendingChanges()
    {
        RunConnectionControllerTest((controller, page) =>
        {
            page.AdapterChooserButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(page.IsAdapterChooserOpen);
            Assert.False(controller.HasPendingChanges);

            FindWpfDescendants<Button>(page)
                .Single(button => button.Name == "CancelAdapterChooserButton")
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.False(page.IsAdapterChooserOpen);
            Assert.False(controller.HasPendingChanges);
        });
    }

    [Fact]
    public void ConnectionPendingChangesGuardNavigationUntilDiscardConfirmed()
    {
        var allowDiscard = false;
        RunConnectionControllerTest((controller, page) =>
        {
            page.UsesCustomPort = true;
            Assert.True(controller.HasPendingChanges);
            Assert.False(controller.TryLeavePage());
            Assert.True(controller.HasPendingChanges);

            allowDiscard = true;
            Assert.True(controller.TryLeavePage());
        }, confirm: _ => allowDiscard);
    }

    [Fact]
    public void ConnectionSaveFailureKeepsPendingChangesAndDoesNotRestart()
    {
        var restartCount = 0;
        RunConnectionControllerTest((controller, page) =>
        {
            page.UsesCustomPort = true;
            page.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.True(controller.HasPendingChanges);
            Assert.Equal(0, restartCount);
            Assert.Contains("couldn't save", page.StatusText.Text, StringComparison.OrdinalIgnoreCase);
        },
        requestRestart: () => restartCount += 1,
        saveSettings: _ => throw new InvalidOperationException("Simulated failure"));
    }

    [Fact]
    public void ConnectionRestartRequestFailureOffersRestartOnlyRetry()
    {
        RunConnectionControllerTest((controller, page) =>
        {
            page.UsesCustomPort = true;
            page.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.False(controller.HasPendingChanges);
            Assert.Equal("Restart Voltura Air", page.PrimaryActionButton.Content);
            Assert.Contains("settings were saved", page.StatusText.Text, StringComparison.OrdinalIgnoreCase);
        }, requestRestart: static () => throw new InvalidOperationException("Simulated restart failure"));
    }

    [Fact]
    public void ConnectionSaveWithPairedDeviceRequiresRestartConfirmation()
    {
        ConnectionConfirmation? requestedConfirmation = null;
        var restartCount = 0;
        RunConnectionControllerTest((controller, page) =>
        {
            page.UsesCustomPort = true;
            page.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(ConnectionConfirmation.RestartWithPairedDevices, requestedConfirmation);
            Assert.True(controller.HasPendingChanges);
            Assert.Equal(0, restartCount);
        },
        requestRestart: () => restartCount += 1,
        confirm: confirmation =>
        {
            requestedConfirmation = confirmation;
            return false;
        },
        pairDevice: true);
    }

    [Fact]
    public void ConnectionSavesAdapterAndPortTogetherOnce()
    {
        var candidate = LanAddressSelector.CreateCandidate(
            System.Net.IPAddress.Parse("192.168.1.40"),
            "Wi-Fi",
            "Combined settings adapter",
            System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
            hasIpv4Gateway: true,
            adapterId: "combined-adapter-id");
        var saveCount = 0;
        NetworkSettingsSnapshot? saved = null;

        RunConnectionControllerTest((controller, page) =>
        {
            var item = Assert.Single(ConnectionCandidateItem.Create([candidate], selectedCandidate: null));
            controller.SelectAdapter(item);
            page.PortTextBox.Text = "51396";
            page.UsesCustomPort = true;

            Assert.Contains("Automatic →", page.AdapterChangeText, StringComparison.Ordinal);
            Assert.Contains("Automatic → Custom · 51396", page.PortChangeText, StringComparison.Ordinal);
            page.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(1, saveCount);
            Assert.NotNull(saved);
            Assert.Equal(NetworkSelectionMode.Manual, saved.NetworkMode);
            Assert.Equal("combined-adapter-id", saved.ManualAdapterId);
            Assert.Equal(PortSelectionMode.Manual, saved.PortMode);
            Assert.Equal(51396, saved.ManualPort);
        },
        saveSettings: snapshot =>
        {
            saveCount += 1;
            saved = snapshot;
        },
        candidates: [candidate]);
    }

    private static void RunConnectionControllerTest(
        Action<ConnectionPageController, ConnectionPageView> assertion,
        Action? requestRestart = null,
        Action<NetworkSettingsSnapshot>? saveSettings = null,
        Func<ConnectionConfirmation, bool>? confirm = null,
        bool pairDevice = false,
        IReadOnlyList<LanAddressCandidate>? candidates = null)
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
            using var pairingKey = pairDevice ? new PairingTestKey() : null;
            var manager = new PairingManager(store.Store);
            if (pairingKey is not null)
            {
                var now = DateTimeOffset.UtcNow;
                manager.AcceptPairing(
                    "client-a",
                    "Test device",
                    manager.CreatePairingToken(now),
                    now,
                    reconnectPublicKey: pairingKey.PublicKey);
            }

            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var settings = CreateSettings(customAdapter: false, customPort: false);
            var controller = new ConnectionPageController(
                new Window(),
                manager,
                webHost,
                requestRestart ?? (static () => { }),
                NullAppLog.Instance,
                loadSettings: () => settings,
                saveSettings: saveSettings ?? (saved => settings = saved),
                confirm: confirm,
                loadCandidates: candidates is null ? null : () => candidates);
            var page = controller.CreateView();
            try
            {
                assertion(controller, page);
            }
            finally
            {
                DisposeWebHost(webHost);
            }
        });
    }
}
