using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WebHostLifetimeTests
{
    [Fact]
    public async Task DisposalReleasesRemainingOwnersWhenOneOwnerFails()
    {
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var awakeService = new RecordingAwakeService();
        var webHost = new WebHostService(
            new PairingManager(store.Store),
            new InputDispatcher(inputInjector),
            powerController: new ThrowingPowerController(),
            awakeService: awakeService,
            isolatedTestMode: true,
            configureWebHost: builder => builder.UseTestServer());

        await Assert.ThrowsAsync<InvalidOperationException>(() => webHost.DisposeAsync().AsTask());

        Assert.True(awakeService.Disposed);
        await webHost.DisposeAsync();
    }

    private sealed class ThrowingPowerController : ISystemPowerController, IDisposable
    {
        public SystemPowerExecutionResult TryExecute(string action) => SystemPowerExecutionResult.Success;

        public bool IsActionAvailable(string action) => true;

        public bool DismissBlackoutIfActive() => false;

        public void Dispose() => throw new InvalidOperationException("Expected disposal failure.");
    }

    private sealed class RecordingAwakeService : IAwakeService
    {
        public AwakeState State { get; } = new(AwakeMode.Off, false, 60, null);

        public bool Disposed { get; private set; }

        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public AwakeOperationResult SetOff() => AwakeOperationResult.Success;

        public AwakeOperationResult SetIndefinite() => AwakeOperationResult.Success;

        public AwakeOperationResult SetTimed(TimeSpan duration) => AwakeOperationResult.Success;

        public AwakeOperationResult SetExpiration(DateTimeOffset expiresAt) => AwakeOperationResult.Success;

        public AwakeOperationResult SetKeepScreenOn(bool keepScreenOn) => AwakeOperationResult.Success;

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
