using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PairingAttemptRateLimiterTests
{
    [Fact]
    public void PrunesExpiredAttemptsWhenAnotherAddressFails()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var limiter = new PairingAttemptRateLimiter(window: TimeSpan.FromSeconds(10), now: () => now);
        limiter.RecordFailure("192.168.1.10");

        now = now.AddSeconds(11);
        limiter.RecordFailure("192.168.1.11");

        Assert.Equal(1, limiter.Count);
    }

    [Fact]
    public void CapsDistinctAttemptAddresses()
    {
        var limiter = new PairingAttemptRateLimiter(maxEntries: 2);

        limiter.RecordFailure("192.168.1.10");
        limiter.RecordFailure("192.168.1.11");
        limiter.RecordFailure("192.168.1.12");

        Assert.Equal(2, limiter.Count);
    }
}
