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
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var previous = manager.CreatePairingCode(now);

        manager.CreatePairingCode(now.AddMinutes(1));
        var accepted = manager.Accept(
            "client-a",
            "Phone",
            previous.Value,
            null,
            now.AddMinutes(1).Add(PairingManager.TokenRotationOverlap).AddMilliseconds(-1));

        Assert.True(accepted.Accepted);
    }

    [Fact]
    public void RotationRejectsPreviousCodeAfterOverlap()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var previous = manager.CreatePairingCode(now);

        manager.CreatePairingCode(now.AddMinutes(1));
        var rejected = manager.Accept(
            "client-a",
            "Phone",
            previous.Value,
            null,
            now.AddMinutes(1).Add(PairingManager.TokenRotationOverlap));

        Assert.False(rejected.Accepted);
        Assert.Equal("expired-token", rejected.Reason);
    }

    [Fact]
    public void RotationRetainsOnlyOnePreviousCode()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var oldest = manager.CreatePairingCode(now);

        manager.CreatePairingCode(now.AddSeconds(1));
        manager.CreatePairingCode(now.AddSeconds(2));
        var rejected = manager.Accept("client-a", "Phone", oldest.Value, null, now.AddSeconds(2));

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-token", rejected.Reason);
    }

    [Fact]
    public void ExplicitPairingTokenTakesPrecedenceOverStoredReconnectSecret()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var initialCode = manager.CreatePairingCode(now);
        var initialPairing = manager.Accept("client-a", "Phone", initialCode.Value, null, now);
        var replacementCode = manager.CreatePairingCode(now.AddMinutes(1));
        var invalidations = 0;
        manager.PairingCodeInvalidated += (_, _) => invalidations += 1;

        var repaired = manager.Accept(
            "client-a",
            "Phone",
            replacementCode.Value,
            initialPairing.Secret,
            now.AddMinutes(1));
        var oldSecret = manager.Accept("client-a", "Phone", null, initialPairing.Secret, now.AddMinutes(2));

        Assert.True(repaired.Accepted);
        Assert.Equal("paired-with-new-secret", repaired.Reason);
        Assert.NotEqual(initialPairing.Secret, repaired.Secret);
        Assert.Equal(1, invalidations);
        Assert.False(oldSecret.Accepted);
        Assert.Equal("secret-revoked", oldSecret.Reason);
    }

    [Fact]
    public void InvalidExplicitPairingTokenDoesNotFallBackToStoredSecret()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var initialCode = manager.CreatePairingCode(now);
        var initialPairing = manager.Accept("client-a", "Phone", initialCode.Value, null, now);
        var currentCode = manager.CreatePairingCode(now.AddMinutes(1));

        var rejected = manager.Accept(
            "client-a",
            "Phone",
            "not-the-current-code",
            initialPairing.Secret,
            now.AddMinutes(1));
        var accepted = manager.Accept(
            "client-a",
            "Phone",
            currentCode.Value,
            initialPairing.Secret,
            now.AddMinutes(1));

        Assert.False(rejected.Accepted);
        Assert.Equal("invalid-token", rejected.Reason);
        Assert.True(accepted.Accepted);
    }
}
