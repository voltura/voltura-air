namespace VolturaAir.Host.Tests;

public sealed class TrayConnectionIndicatorTests
{
    [Fact]
    public void StartsNeutralWhileAPairedDeviceHasTimeToReconnect()
    {
        var indicator = new TrayConnectionIndicator(isPaired: true, hasActiveController: false, holdInitialDisconnectedState: true);

        Assert.Equal(TrayConnectionState.Starting, indicator.DisplayedState);
        Assert.Equal(TrayConnectionState.Starting, indicator.Update(isPaired: true, hasActiveController: false, holdInitialDisconnectedState: true));
        Assert.Equal(TrayConnectionState.Connected, indicator.Update(isPaired: true, hasActiveController: true));
    }

    [Fact]
    public void ShowsDisconnectedWhenTheStartupGracePeriodExpires()
    {
        var indicator = new TrayConnectionIndicator(isPaired: true, hasActiveController: false, holdInitialDisconnectedState: true);

        Assert.Equal(TrayConnectionState.Disconnected, indicator.Update(isPaired: true, hasActiveController: false));
    }

    [Fact]
    public void HoldsTheConnectedBadgeWhileAnExistingDeviceReconnects()
    {
        var indicator = new TrayConnectionIndicator(isPaired: true, hasActiveController: true);

        Assert.Equal(TrayConnectionState.Connected, indicator.Update(isPaired: true, hasActiveController: false, holdConnectedDuringReconnect: true));
        Assert.Equal(TrayConnectionState.Connected, indicator.Update(isPaired: true, hasActiveController: false, holdConnectedDuringReconnect: true));
        Assert.Equal(TrayConnectionState.Connected, indicator.Update(isPaired: true, hasActiveController: true));
    }

    [Fact]
    public void ShowsDisconnectedAfterTheReconnectGracePeriodExpires()
    {
        var indicator = new TrayConnectionIndicator(isPaired: true, hasActiveController: true);

        _ = indicator.Update(isPaired: true, hasActiveController: false, holdConnectedDuringReconnect: true);

        Assert.Equal(TrayConnectionState.Disconnected, indicator.Update(isPaired: true, hasActiveController: false));
    }

    [Fact]
    public void DoesNotHoldTheConnectedBadgeWhenNoDeviceIsPaired()
    {
        var indicator = new TrayConnectionIndicator(isPaired: true, hasActiveController: true);

        Assert.Equal(TrayConnectionState.NoDevicesRegistered, indicator.Update(isPaired: false, hasActiveController: false, holdConnectedDuringReconnect: true));
    }
}
