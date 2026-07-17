namespace VolturaAir.Host.Tests;

public sealed class RemoteInputBlockedTrayNotificationTests
{
    [Fact]
    public void ShowsActionableWarningWhileAControllerIsConnectedAndInputIsBlocked()
    {
        Assert.True(RemoteInputBlockedTrayNotification.ShouldShow(isBlocked: true, hasActiveController: true));
        Assert.Equal("PC input paused", RemoteInputBlockedTrayNotification.Title);
        Assert.Equal(
            "An administrator app is active. Switch to another window or choose Show desktop on your phone.",
            RemoteInputBlockedTrayNotification.Message);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void DoesNotShowWithoutBothAControllerAndBlockedInput(bool isBlocked, bool hasActiveController)
    {
        Assert.False(RemoteInputBlockedTrayNotification.ShouldShow(isBlocked, hasActiveController));
    }
}
