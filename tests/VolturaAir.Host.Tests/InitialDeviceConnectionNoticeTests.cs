using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class InitialDeviceConnectionNoticeTests : IsolatedHostSettingsTest
{
    [Fact]
    public void FirstAuthenticatedConnectionPublishesMandatoryNoticeWhenOptionalNoticesAreDisabled()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        AppNotificationSettings.SetShowConnectionStatusNotifications(false);
        var manager = Pair(managerStore: store, key, "client-a");
        var notices = new List<InitialDeviceConnectionNotice>();
        manager.InitialDeviceConnected += (_, args) => notices.Add(args.Notice);

        using var connection = manager.TrackConnection("client-a");
        Assert.True(manager.TryTakeInitialDeviceConnectionNotice(out _));

        var notice = Assert.Single(notices);
        Assert.Equal("client-a", notice.ClientId);
        Assert.Equal("Phone", notice.DeviceName);
        Assert.Equal(DeviceAccessProfile.MyDevice, notice.AccessProfile);
        Assert.Null(Assert.Single(store.Store.Load()).InitialAccessNoticePending);
    }

    [Fact]
    public void FailedAuthenticationAndMigratedDevicesDoNotPublishMandatoryNotice()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        store.Store.Save([new PairingRecord("legacy", key.PublicKey, "Legacy")]);
        var manager = new PairingManager(store.Store);
        var notices = 0;
        manager.InitialDeviceConnected += (_, _) => notices++;

        Assert.False(manager.TryTrackConnection(
            "missing",
            static () => { },
            out var missing,
            out _));
        Assert.Null(missing);
        using var legacyConnection = manager.TrackConnection("legacy");
        Assert.False(manager.TryTakeInitialDeviceConnectionNotice(out _));

        Assert.Equal(0, notices);
    }

    [Fact]
    public void ReconnectConcurrencyAndRestartCannotDuplicateMandatoryNotice()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = Pair(store, key, "client-a");
        var notices = 0;
        manager.InitialDeviceConnected += (_, _) => Interlocked.Increment(ref notices);

        IDisposable? first = null;
        IDisposable? second = null;
        Parallel.Invoke(
            () => first = manager.TrackConnection("client-a"),
            () => second = manager.TrackConnection("client-a"));
        Assert.True(manager.TryTakeInitialDeviceConnectionNotice(out _));
        Assert.False(manager.TryTakeInitialDeviceConnectionNotice(out _));
        first!.Dispose();
        second!.Dispose();

        var restarted = new PairingManager(store.Store);
        restarted.InitialDeviceConnected += (_, _) => Interlocked.Increment(ref notices);
        using var afterRestart = restarted.TrackConnection("client-a");
        Assert.False(restarted.TryTakeInitialDeviceConnectionNotice(out _));

        Assert.Equal(1, notices);
    }

    [Fact]
    public void NoticeWaitingForTheTrayRemainsDurableAcrossRestart()
    {
        using var store = new TempPairingStore();
        using var firstKey = new PairingTestKey();
        using var secondKey = new PairingTestKey();
        var manager = Pair(store, firstKey, "first", "First");
        Assert.True(manager.AcceptPairing(
            "second",
            "Second",
            manager.CreatePairingToken(),
            reconnectPublicKey: secondKey.PublicKey).Accepted);
        var firstConnection = manager.TrackConnection("first");
        var secondConnection = manager.TrackConnection("second");

        Assert.True(manager.TryTakeInitialDeviceConnectionNotice(out var firstNotice));
        Assert.Equal("first", firstNotice!.ClientId);
        firstConnection.Dispose();
        secondConnection.Dispose();
        var persisted = store.Store.Load().ToDictionary(record => record.ClientId, StringComparer.Ordinal);
        Assert.Null(persisted["first"].InitialAccessNoticePending);
        Assert.True(persisted["second"].InitialAccessNoticePending);

        var restarted = new PairingManager(store.Store);
        using var reconnected = restarted.TrackConnection("second");
        Assert.True(restarted.TryTakeInitialDeviceConnectionNotice(out var secondNotice));
        Assert.Equal("second", secondNotice!.ClientId);
        Assert.Null(store.Store.Load().Single(record => record.ClientId == "second").InitialAccessNoticePending);
    }

    [Fact]
    public void FailedMarkerClearShowsNothingAndRetriesOnNextAuthenticatedConnection()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = Pair(store, key, "client-a");
        var notices = 0;
        manager.InitialDeviceConnected += (_, _) => notices++;
        store.Store.BeforeReplaceForTests = () => throw new IOException("injected marker failure");

        var first = manager.TrackConnection("client-a");
        Assert.False(manager.TryTakeInitialDeviceConnectionNotice(out _));
        Assert.Equal(0, notices);
        Assert.True(Assert.Single(store.Store.Load()).InitialAccessNoticePending);
        Assert.True(manager.HasActivePendingInitialDeviceConnectionNotice);
        Assert.False(TrayConnectionFeedbackController.ShouldShowOptionalConnectedNotification(
            becameActive: true,
            cancelledTransientDisconnect: false,
            showedMandatoryNotice: manager.HasActivePendingInitialDeviceConnectionNotice));

        store.Store.BeforeReplaceForTests = null;
        first.Dispose();
        using var retry = manager.TrackConnection("client-a");
        Assert.True(manager.TryTakeInitialDeviceConnectionNotice(out _));

        Assert.Equal(1, notices);
        Assert.Null(Assert.Single(store.Store.Load()).InitialAccessNoticePending);
    }

    [Fact]
    public void DeviceNotificationTextAndOptionalMultiDeviceSummaryStayExact()
    {
        var mandatory = TrayConnectionFeedbackController.CreateDeviceNotification(
            "client-a",
            "Phone",
            DeviceAccessProfile.RemoteControls);

        Assert.Equal("Device connected", mandatory.Title);
        Assert.Equal("Phone uses Remote controls access. Click to change.", mandatory.Message);
        Assert.Equal("client-a", mandatory.ClientId);

        using var store = new TempPairingStore();
        using var firstKey = new PairingTestKey();
        using var secondKey = new PairingTestKey();
        var manager = Pair(store, firstKey, "first", "First");
        manager.AcceptPairing(
            "second",
            "Second",
            manager.CreatePairingToken(),
            reconnectPublicKey: secondKey.PublicKey);
        using var first = manager.TrackConnection("first");
        var single = TrayConnectionFeedbackController.CreateOptionalConnectedNotification(
            manager.GetDevices().Where(device => device.IsActive).ToArray(),
            manager.ActiveDeviceSummary);
        Assert.Equal("first", single.ClientId);
        Assert.Contains("uses My device access", single.Message, StringComparison.Ordinal);

        using var second = manager.TrackConnection("second");
        var multiple = TrayConnectionFeedbackController.CreateOptionalConnectedNotification(
            manager.GetDevices().Where(device => device.IsActive).ToArray(),
            manager.ActiveDeviceSummary);
        Assert.Equal("Voltura Air paired", multiple.Title);
        Assert.Equal("First and Second connected.", multiple.Message);
        Assert.Null(multiple.ClientId);
    }

    [Fact]
    public void MandatoryNoticeSuppressesOrdinaryNoticeForTheSameTransition()
    {
        Assert.False(TrayConnectionFeedbackController.ShouldShowOptionalConnectedNotification(
            becameActive: true,
            cancelledTransientDisconnect: false,
            showedMandatoryNotice: true));
        Assert.True(TrayConnectionFeedbackController.ShouldShowOptionalConnectedNotification(
            becameActive: true,
            cancelledTransientDisconnect: false,
            showedMandatoryNotice: false));
    }

    private static PairingManager Pair(
        TempPairingStore managerStore,
        PairingTestKey key,
        string clientId,
        string deviceName = "Phone")
    {
        var manager = new PairingManager(managerStore.Store);
        Assert.True(manager.AcceptPairing(
            clientId,
            deviceName,
            manager.CreatePairingToken(),
            reconnectPublicKey: key.PublicKey).Accepted);
        return manager;
    }
}
