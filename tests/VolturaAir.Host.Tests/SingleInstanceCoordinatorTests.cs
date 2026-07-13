using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondInstanceSignalsFirstInstanceAndDoesNotAcquireOwnership()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\VolturaAir.Host.Tests.Instance.{uniqueId}";
        var activationEventName = $@"Local\VolturaAir.Host.Tests.Activate.{uniqueId}";
        using var activationReceived = new ManualResetEventSlim();
        using var first = SingleInstanceCoordinator.TryAcquire(mutexName, activationEventName, activationReceived.Set);

        SingleInstanceCoordinator? second = null;
        var secondInstanceThread = new Thread(() => second = SingleInstanceCoordinator.TryAcquire(mutexName, activationEventName, () => { }));
        secondInstanceThread.Start();
        Assert.True(secondInstanceThread.Join(TimeSpan.FromSeconds(2)));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(activationReceived.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void OwnershipCanBeAcquiredAfterFirstInstanceExits()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\VolturaAir.Host.Tests.Instance.{uniqueId}";
        var activationEventName = $@"Local\VolturaAir.Host.Tests.Activate.{uniqueId}";
        var first = SingleInstanceCoordinator.TryAcquire(mutexName, activationEventName, () => { });

        Assert.NotNull(first);
        first.Dispose();

        using var replacement = SingleInstanceCoordinator.TryAcquire(mutexName, activationEventName, () => { });
        Assert.NotNull(replacement);
    }
}
