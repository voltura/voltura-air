using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PairingCodeLifecycleTests
{
    [Fact]
    public void PairingCodeSchedulesRotationBeforeExpiry()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;

        var code = manager.CreatePairingCode(now);

        Assert.Equal(now.Add(PairingManager.TokenLifetime), code.ExpiresAt);
        Assert.Equal(code.ExpiresAt.Subtract(PairingManager.TokenRotationOverlap), code.RefreshAt);
    }

    [Fact]
    public void RotationKeepsImmediatelyPreviousCodeValidDuringBoundedOverlap()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var previous = manager.CreatePairingCode(now);

        manager.CreatePairingCode(now.AddMinutes(1));
        var accepted = manager.AcceptPairing(
            "client-a",
            "Phone",
            previous.Value,
            now.AddMinutes(1).Add(PairingManager.TokenRotationOverlap).AddMilliseconds(-1),
            reconnectPublicKey: key.PublicKey);

        Assert.True(accepted.Accepted);
    }

    [Fact]
    public void RotationRejectsPreviousCodeAfterOverlap()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var previous = manager.CreatePairingCode(now);

        manager.CreatePairingCode(now.AddMinutes(1));
        var rejected = manager.AcceptPairing(
            "client-a",
            "Phone",
            previous.Value,
            now.AddMinutes(1).Add(PairingManager.TokenRotationOverlap),
            reconnectPublicKey: key.PublicKey);

        Assert.False(rejected.Accepted);
        Assert.Equal("expired-token", rejected.Reason);
    }

    [Fact]
    public void RotationRetainsOnlyOnePreviousCode()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var oldest = manager.CreatePairingCode(now);

        manager.CreatePairingCode(now.AddSeconds(1));
        manager.CreatePairingCode(now.AddSeconds(2));
        var rejected = manager.AcceptPairing("client-a", "Phone", oldest.Value, now.AddSeconds(2), reconnectPublicKey: key.PublicKey);

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-token", rejected.Reason);
    }

    [Fact]
    public void PairingAgainReplacesTheStoredReconnectKey()
    {
        using var store = new TempPairingStore();
        using var initialKey = new PairingTestKey();
        using var replacementKey = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var initialCode = manager.CreatePairingCode(now);
        var initialPairing = manager.AcceptPairing("client-a", "Phone", initialCode.Value, now, reconnectPublicKey: initialKey.PublicKey);
        var replacementCode = manager.CreatePairingCode(now.AddMinutes(1));
        var invalidations = 0;
        manager.PairingCodeInvalidated += (_, _) => invalidations += 1;

        var repaired = manager.AcceptPairing(
            "client-a",
            "Phone",
            replacementCode.Value,
            now.AddMinutes(1),
            reconnectPublicKey: replacementKey.PublicKey);
        var oldKeyChallenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));
        var oldKeyReconnect = manager.AcceptReconnectProof(
            "client-a",
            oldKeyChallenge,
            initialKey.SignReconnectChallenge("client-a", oldKeyChallenge),
            "Phone",
            now: now.AddMinutes(2));
        var replacementKeyChallenge = Assert.IsType<string>(manager.CreateReconnectChallenge("client-a"));
        var replacementKeyReconnect = manager.AcceptReconnectProof(
            "client-a",
            replacementKeyChallenge,
            replacementKey.SignReconnectChallenge("client-a", replacementKeyChallenge),
            "Phone",
            now: now.AddMinutes(3));

        Assert.True(repaired.Accepted);
        Assert.True(initialPairing.Accepted);
        Assert.Equal("paired", repaired.Reason);
        Assert.Equal(1, invalidations);
        Assert.False(oldKeyReconnect.Accepted);
        Assert.Equal("invalid-proof", oldKeyReconnect.Reason);
        Assert.True(replacementKeyReconnect.Accepted);
    }

    [Fact]
    public void InvalidPairingTokenDoesNotReplaceStoredReconnectKey()
    {
        using var store = new TempPairingStore();
        using var initialKey = new PairingTestKey();
        using var replacementKey = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var initialCode = manager.CreatePairingCode(now);
        manager.AcceptPairing("client-a", "Phone", initialCode.Value, now, reconnectPublicKey: initialKey.PublicKey);
        var currentCode = manager.CreatePairingCode(now.AddMinutes(1));

        var rejected = manager.AcceptPairing(
            "client-a",
            "Phone",
            "not-the-current-code",
            now.AddMinutes(1),
            reconnectPublicKey: replacementKey.PublicKey);
        var accepted = manager.AcceptPairing(
            "client-a",
            "Phone",
            currentCode.Value,
            now.AddMinutes(1),
            reconnectPublicKey: replacementKey.PublicKey);

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-token", rejected.Reason);
        Assert.True(accepted.Accepted);
    }
}
