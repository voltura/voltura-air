using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PairingManagerTests
{
    [Fact]
    public void AcceptsFreshPairingTokenWithReconnectPublicKey()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);

        var accepted = manager.AcceptPairing("client-a", "Phone", token, now, reconnectPublicKey: key.PublicKey);

        Assert.True(accepted.Accepted);
        Assert.Equal("paired", accepted.Reason);
        Assert.Equal(1, manager.PairedDeviceCount);
        Assert.Equal(key.PublicKey, Assert.Single(store.Store.Load()).ReconnectPublicKey);
    }

    [Fact]
    public void RejectsFreshPairingWithoutReconnectPublicKey()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var token = manager.CreatePairingToken();

        var rejected = manager.AcceptPairing("client-a", "Phone", token, reconnectPublicKey: null);

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-message", rejected.Reason);
        Assert.Equal(0, manager.PairedDeviceCount);
    }

    [Fact]
    public void RejectsExpiredToken()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);

        var expired = manager.AcceptPairing("client-a", "Phone", token, now.AddMinutes(6), reconnectPublicKey: key.PublicKey);

        Assert.False(expired.Accepted);
        Assert.Equal("expired-token", expired.Reason);
    }

    [Fact]
    public void RejectsWrongTokenWithoutConsumingCurrentToken()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);

        var rejected = manager.AcceptPairing("client-a", "Phone", "wrong-token", now, reconnectPublicKey: key.PublicKey);
        var accepted = manager.AcceptPairing("client-a", "Phone", token, now, reconnectPublicKey: key.PublicKey);

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-token", rejected.Reason);
        Assert.True(accepted.Accepted);
    }

    [Fact]
    public void AcceptsReconnectProofForHostChallenge()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(now), now, reconnectPublicKey: key.PublicKey);

        var challenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));
        var reconnect = manager.AcceptReconnectProof(
            "client-a",
            challenge,
            key.SignReconnectChallenge("client-a", challenge),
            "Phone",
            now: now.AddMinutes(1));

        Assert.False(string.IsNullOrWhiteSpace(challenge));
        Assert.True(reconnect.Accepted);
        Assert.Equal("paired", reconnect.Reason);
    }

    [Fact]
    public void RejectsReconnectProofForDifferentChallenge()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(now), now, reconnectPublicKey: key.PublicKey);

        var originalChallenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));
        var differentChallenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));
        var signature = key.SignReconnectChallenge("client-a", originalChallenge);
        var rejected = manager.AcceptReconnectProof("client-a", differentChallenge, signature, "Phone", now: now.AddMinutes(1));

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-proof", rejected.Reason);
    }

    [Fact]
    public void RejectsReconnectProofSignedByDifferentKey()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var otherKey = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(now), now, reconnectPublicKey: key.PublicKey);
        var challenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));

        var rejected = manager.AcceptReconnectProof(
            "client-a",
            challenge,
            otherKey.SignReconnectChallenge("client-a", challenge),
            "Phone",
            now: now.AddMinutes(1));

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-proof", rejected.Reason);
    }

    [Fact]
    public void RejectsMalformedBase64UrlReconnectProof()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(now), now, reconnectPublicKey: key.PublicKey);
        var challenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));

        var rejected = manager.AcceptReconnectProof("client-a", challenge, "base64url_but_invalid_length", "Phone");

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-proof", rejected.Reason);
    }

    [Fact]
    public void PairingExistingClientWithNewTokenReplacesReconnectKeyAndRevokesOldConnection()
    {
        using var store = new TempPairingStore();
        using var oldKey = new PairingTestKey();
        using var newKey = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var revokedClientIds = new List<string?>();
        manager.PairingRevoked += (_, e) => revokedClientIds.Add(e.ClientId);

        var first = manager.AcceptPairing("client-a", "Browser iPhone", manager.CreatePairingToken(now), now, reconnectPublicKey: oldKey.PublicKey);
        using var connection = manager.TrackConnection("client-a", now.AddSeconds(1));
        var second = manager.AcceptPairing("client-a", "Home Screen iPhone", manager.CreatePairingToken(now.AddMinutes(1)), now.AddMinutes(1), reconnectPublicKey: newKey.PublicKey);
        connection.Dispose();

        var oldReconnect = CompleteReconnect(manager, oldKey, "client-a", "Browser iPhone", now.AddMinutes(2));
        var newReconnect = CompleteReconnect(manager, newKey, "client-a", "Home Screen iPhone", now.AddMinutes(2));
        var device = Assert.Single(manager.GetDevices());

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Single(revokedClientIds);
        Assert.Equal("client-a", revokedClientIds[0]);
        Assert.False(oldReconnect.Accepted);
        Assert.True(newReconnect.Accepted);
        Assert.Equal("Home Screen iPhone", device.DeviceName);
        Assert.False(manager.HasActiveController);
    }

    [Fact]
    public void TracksActiveControllersByDevice()
    {
        using var store = new TempPairingStore();
        using var firstKey = new PairingTestKey();
        using var secondKey = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        manager.AcceptPairing("client-a", "Joakim iPhone", manager.CreatePairingToken(now), now, reconnectPublicKey: firstKey.PublicKey);
        manager.AcceptPairing("client-b", "Dominika phone", manager.CreatePairingToken(now.AddSeconds(1)), now.AddSeconds(1), reconnectPublicKey: secondKey.PublicKey);

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
    public void DevicePointerSpeedOverrideWinsOverGlobalDefault()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(now), now, reconnectPublicKey: key.PublicKey);

        var changed = manager.SetDevicePointerSpeedOverride("client-a", 65);
        var clamped = manager.SetDevicePointerSpeedOverride("client-a", 999);
        var device = Assert.Single(manager.GetDevices());

        Assert.True(changed);
        Assert.True(clamped);
        Assert.Equal(100, manager.GetDevicePointerSpeed("client-a"));
        Assert.Equal(100, device.PointerSpeed);
        Assert.Equal(100, device.PointerSpeedOverride);
        Assert.Equal(100, Assert.Single(store.Store.Load()).PointerSpeedOverride);
    }

    [Fact]
    public void DevicePointerSpeedChangeDoesNotReportAConnectionChange()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        var connectionChanges = 0;
        var profileChanges = 0;
        manager.ConnectionChanged += (_, _) => connectionChanges++;
        manager.DeviceProfileChanged += (_, _) => profileChanges++;

        Assert.True(manager.SetDevicePointerSpeedOverride("client-a", 75));

        Assert.Equal(0, connectionChanges);
        Assert.Equal(1, profileChanges);
    }

    [Fact]
    public void DeviceModeButtonOverrideWinsOverTheGlobalDefaultAndPersists()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);

        Assert.True(manager.GetDeviceShowModeButtons("client-a"));
        Assert.True(manager.SetDeviceShowModeButtonsOverride("client-a", false));

        var device = Assert.Single(manager.GetDevices());
        Assert.False(manager.GetDeviceShowModeButtons("client-a"));
        Assert.False(device.ShowModeButtons);
        Assert.False(device.ShowModeButtonsOverride);
        Assert.False(Assert.Single(store.Store.Load()).ShowModeButtonsOverride);

        Assert.True(manager.SetDeviceShowModeButtonsOverride("client-a", null));
        Assert.True(manager.GetDeviceShowModeButtons("client-a"));
        Assert.Null(Assert.Single(store.Store.Load()).ShowModeButtonsOverride);
    }

    [Fact]
    public void DisconnectDeviceRemovesOnlySelectedDevice()
    {
        using var store = new TempPairingStore();
        using var firstKey = new PairingTestKey();
        using var secondKey = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        manager.AcceptPairing("client-a", "Joakim iPhone", manager.CreatePairingToken(now), now, reconnectPublicKey: firstKey.PublicKey);
        manager.AcceptPairing("client-b", "Dominika phone", manager.CreatePairingToken(now.AddSeconds(1)), now.AddSeconds(1), reconnectPublicKey: secondKey.PublicKey);
        using var firstConnection = manager.TrackConnection("client-a");
        using var secondConnection = manager.TrackConnection("client-b");

        var removed = manager.DisconnectDevice("client-b");
        var firstReconnect = CompleteReconnect(manager, firstKey, "client-a", "Joakim iPhone", now.AddMinutes(1));
        var secondReconnect = CompleteReconnect(manager, secondKey, "client-b", "Dominika phone", now.AddMinutes(1));

        Assert.True(removed);
        Assert.True(firstReconnect.Accepted);
        Assert.False(secondReconnect.Accepted);
        Assert.Equal(1, manager.PairedDeviceCount);
        Assert.Equal(1, manager.ActiveControllerCount);
        Assert.Equal("Joakim iPhone", manager.ActiveDeviceSummary);
    }

    private static PairingResult CompleteReconnect(
        PairingManager manager,
        PairingTestKey key,
        string clientId,
        string deviceName,
        DateTimeOffset now)
    {
        var challenge = manager.CreateReconnectChallenge(clientId);
        if (challenge is null)
        {
            return new PairingResult(false, "device-revoked");
        }

        return manager.AcceptReconnectProof(
            clientId,
            challenge,
            key.SignReconnectChallenge(clientId, challenge),
            deviceName,
            now: now);
    }
}
