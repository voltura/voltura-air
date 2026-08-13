using Xunit;

namespace WebRtcSpike.Host.Tests;

public sealed class ProductionHostExclusionTests
{
    [Fact]
    public void HoldsTheOneHostMutexUntilDisposed()
    {
        string mutexName = $@"Local\VolturaAir.Host.Spike.Tests.{Guid.NewGuid():N}";
        using ProductionHostExclusion? first = ProductionHostExclusion.TryAcquire(mutexName);
        using ProductionHostExclusion? blocked = ProductionHostExclusion.TryAcquire(mutexName);

        Assert.NotNull(first);
        Assert.Null(blocked);
    }

    [Fact]
    public void ReleasesTheOneHostMutexForTheNextHost()
    {
        string mutexName = $@"Local\VolturaAir.Host.Spike.Tests.{Guid.NewGuid():N}";
        ProductionHostExclusion? first = ProductionHostExclusion.TryAcquire(mutexName);
        Assert.NotNull(first);
        first.Dispose();

        using ProductionHostExclusion? replacement = ProductionHostExclusion.TryAcquire(mutexName);
        Assert.NotNull(replacement);
    }
}
