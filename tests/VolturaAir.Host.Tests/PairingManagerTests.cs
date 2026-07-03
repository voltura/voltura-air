using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PairingManagerTests
{
    [Fact]
    public void AcceptsValidTokenAndRejectsExpiredToken()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);

        var expired = manager.Accept("client-a", "Phone", token, null, now.AddMinutes(6));

        Assert.False(expired.Accepted);
        Assert.Equal("invalid-token", expired.Reason);
    }

    [Fact]
    public void StoresCredentialForReconnect()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);

        var accepted = manager.Accept("client-a", "Phone", token, null, now);
        var secret = accepted.Secret;
        var reconnect = manager.Accept("client-a", "Phone", null, secret, now.AddMinutes(10));

        Assert.True(accepted.Accepted);
        Assert.NotNull(secret);
        Assert.True(reconnect.Accepted);
        Assert.Equal(secret, reconnect.Secret);
    }

    [Fact]
    public void ConsumesPairingTokenAfterSuccessfulPairing()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);

        var first = manager.Accept("client-a", "Phone", token, null, now);
        var second = manager.Accept("client-b", "Home Screen App", token, null, now);

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Equal("missing-token", second.Reason);
    }

    [Fact]
    public void PairingSecondDeviceKeepsFirstDevice()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        var first = manager.Accept("client-a", "Joakim iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddSeconds(1));
        var second = manager.Accept("client-b", "Dominika phone", secondToken, null, now.AddSeconds(1));

        var firstReconnect = manager.Accept("client-a", "Joakim iPhone", null, first.Secret, now.AddMinutes(1));
        var secondReconnect = manager.Accept("client-b", "Dominika phone", null, second.Secret, now.AddMinutes(1));

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.True(firstReconnect.Accepted);
        Assert.True(secondReconnect.Accepted);
        Assert.Equal(2, manager.PairedDeviceCount);
        Assert.Contains("Joakim iPhone", manager.PairedDeviceSummary);
    }

    [Fact]
    public void PairingExistingClientWithNewTokenReplacesSecretAndRevokesOldConnection()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var revokedClientIds = new List<string?>();
        manager.PairingRevoked += (_, e) => revokedClientIds.Add(e.ClientId);

        var firstToken = manager.CreatePairingToken(now);
        var first = manager.Accept("client-a", "Browser iPhone", firstToken, null, now);
        using var connection = manager.TrackConnection("client-a", now.AddSeconds(1));
        var secondToken = manager.CreatePairingToken(now.AddMinutes(1));

        var second = manager.Accept("client-a", "Home Screen iPhone", secondToken, null, now.AddMinutes(1));
        connection.Dispose();
        var oldReconnect = manager.Accept("client-a", "Browser iPhone", null, first.Secret, now.AddMinutes(2));
        var newReconnect = manager.Accept("client-a", "Home Screen iPhone", null, second.Secret, now.AddMinutes(2));
        var device = Assert.Single(manager.GetDevices());

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.NotNull(first.Secret);
        Assert.NotNull(second.Secret);
        Assert.NotEqual(first.Secret, second.Secret);
        Assert.Single(revokedClientIds);
        Assert.Equal("client-a", revokedClientIds[0]);
        Assert.False(oldReconnect.Accepted);
        Assert.True(newReconnect.Accepted);
        Assert.Equal(1, manager.PairedDeviceCount);
        Assert.Equal("Home Screen iPhone", device.DeviceName);
        Assert.False(manager.HasActiveController);
    }

    [Fact]
    public void FreshPairingWithSameDeviceNameKeepsBothDevices()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        var first = manager.Accept("client-a", "iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddMinutes(1));
        var second = manager.Accept("client-b", "iPhone", secondToken, null, now.AddMinutes(1));

        var firstReconnect = manager.Accept("client-a", "iPhone", null, first.Secret, now.AddMinutes(2));
        var secondReconnect = manager.Accept("client-b", "iPhone", null, second.Secret, now.AddMinutes(2));

        Assert.True(firstReconnect.Accepted);
        Assert.True(secondReconnect.Accepted);
        Assert.Equal(2, manager.PairedDeviceCount);
    }

    [Fact]
    public void CleanupDuplicatesRemovesOlderDisconnectedSameNameDevice()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        var first = manager.Accept("client-a", "iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddMinutes(1));
        var second = manager.Accept("client-b", "iPhone", secondToken, null, now.AddMinutes(1));
        using var secondConnection = manager.TrackConnection("client-b", now.AddMinutes(2));

        var candidates = manager.GetDuplicateCleanupCandidates();
        var removed = manager.CleanUpDuplicateDevices();
        var oldReconnect = manager.Accept("client-a", "iPhone", null, first.Secret, now.AddMinutes(3));
        var newReconnect = manager.Accept("client-b", "iPhone", null, second.Secret, now.AddMinutes(3));

        var candidate = Assert.Single(candidates);
        Assert.Equal("client-a", candidate.ClientId);
        Assert.Equal(1, removed);
        Assert.False(oldReconnect.Accepted);
        Assert.True(newReconnect.Accepted);
        Assert.Equal(1, manager.PairedDeviceCount);
        Assert.Equal(1, manager.ActiveControllerCount);
    }

    [Fact]
    public void CleanupDuplicatesDoesNotRemoveActiveSameNameDevices()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        manager.Accept("client-a", "iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddMinutes(1));
        manager.Accept("client-b", "iPhone", secondToken, null, now.AddMinutes(1));
        using var firstConnection = manager.TrackConnection("client-a", now.AddMinutes(2));
        using var secondConnection = manager.TrackConnection("client-b", now.AddMinutes(3));

        Assert.Empty(manager.GetDuplicateCleanupCandidates());
        Assert.Equal(0, manager.CleanUpDuplicateDevices());
        Assert.Equal(2, manager.PairedDeviceCount);
        Assert.Equal(2, manager.ActiveControllerCount);
    }

    [Fact]
    public void TracksActiveControllersByDevice()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        manager.Accept("client-a", "Joakim iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddSeconds(1));
        manager.Accept("client-b", "Dominika phone", secondToken, null, now.AddSeconds(1));

        using var firstConnection = manager.TrackConnection("client-a");
        using var secondConnection = manager.TrackConnection("client-b");

        Assert.True(manager.HasActiveController);
        Assert.Equal(2, manager.ActiveControllerCount);
        Assert.Equal("Joakim iPhone and Dominika phone", manager.ActiveDeviceSummary);

        secondConnection.Dispose();

        Assert.Equal(1, manager.ActiveControllerCount);
        Assert.Equal("Joakim iPhone", manager.ActiveDeviceSummary);
    }

    [Fact]
    public void OrdersDevicesByLatestActivity()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        manager.Accept("client-a", "Chrome iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddMinutes(1));
        manager.Accept("client-b", "Home Screen iPhone", secondToken, null, now.AddMinutes(1));

        using var connection = manager.TrackConnection("client-a", now.AddMinutes(2));
        var devices = manager.GetDevices();

        Assert.Equal("client-a", devices[0].ClientId);
        Assert.Equal(now.AddMinutes(2), devices[0].LastConnectedAt);
        Assert.True(devices[0].IsActive);
        Assert.Equal("client-b", devices[1].ClientId);
    }

    [Fact]
    public void RenameDeviceUpdatesNameTimestampAndOrder()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        manager.Accept("client-a", "Chrome iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddMinutes(1));
        manager.Accept("client-b", "Home Screen iPhone", secondToken, null, now.AddMinutes(1));

        var renamed = manager.RenameDevice("client-a", "Joakim iPhone", now.AddMinutes(2));
        var devices = manager.GetDevices();

        Assert.True(renamed);
        Assert.Equal("client-a", devices[0].ClientId);
        Assert.Equal("Joakim iPhone", devices[0].DeviceName);
        Assert.Equal(now.AddMinutes(2), devices[0].LastRenamedAt);
        Assert.Equal("client-b", devices[1].ClientId);
    }

    [Fact]
    public void DisconnectDeviceRemovesOnlySelectedDevice()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var firstToken = manager.CreatePairingToken(now);
        var first = manager.Accept("client-a", "Joakim iPhone", firstToken, null, now);
        var secondToken = manager.CreatePairingToken(now.AddSeconds(1));
        var second = manager.Accept("client-b", "Dominika phone", secondToken, null, now.AddSeconds(1));
        using var firstConnection = manager.TrackConnection("client-a");
        using var secondConnection = manager.TrackConnection("client-b");

        var removed = manager.DisconnectDevice("client-b");
        var firstReconnect = manager.Accept("client-a", "Joakim iPhone", null, first.Secret, now.AddMinutes(1));
        var secondReconnect = manager.Accept("client-b", "Dominika phone", null, second.Secret, now.AddMinutes(1));

        Assert.True(removed);
        Assert.True(firstReconnect.Accepted);
        Assert.False(secondReconnect.Accepted);
        Assert.Equal(1, manager.PairedDeviceCount);
        Assert.Equal(1, manager.ActiveControllerCount);
        Assert.Equal("Joakim iPhone", manager.ActiveDeviceSummary);
    }
}
