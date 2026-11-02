using Microsoft.Win32;
using VolturaAir.Host.Features.Connect;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class IntroductionControllerTests : IsolatedHostSettingsTest
{
    [Fact]
    public void NewHostWaitsForAuthenticationThenRemainsPendingAcrossRestartUntilDismissed()
    {
        var controller = new IntroductionController(hasRememberedDevices: false);
        Assert.False(controller.IsVisible);
        controller.DeviceConnected();
        Assert.True(controller.IsVisible);
        var restarted = new IntroductionController(hasRememberedDevices: true);
        Assert.True(restarted.IsVisible);
        restarted.Dismiss();
        restarted.DeviceConnected();
        Assert.False(restarted.IsVisible);
        Assert.False(new IntroductionController(hasRememberedDevices: false).IsVisible);
        restarted.Replay();
        Assert.True(restarted.IsVisible);
        restarted.Dismiss();
        Assert.False(restarted.IsVisible);
    }

    [Fact]
    public void ExistingHostStaysSuppressedAfterDevicesAreForgotten()
    {
        var controller = new IntroductionController(hasRememberedDevices: true);
        controller.DeviceConnected();
        Assert.False(controller.IsVisible);
        var restarted = new IntroductionController(hasRememberedDevices: false);
        restarted.DeviceConnected();
        Assert.False(restarted.IsVisible);
        restarted.Replay();
        Assert.True(restarted.IsVisible);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("{\"version\":2,\"state\":\"eligible\"}")]
    [InlineData("{\"version\":1,\"state\":\"unknown\"}")]
    [InlineData("{\"version\":1}")]
    public void MalformedRecordsSuppressAutomaticCardButAllowReplay(string raw)
    {
        using var key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue(IntroductionController.ValueName, raw, RegistryValueKind.String);
        var controller = new IntroductionController(hasRememberedDevices: false);
        controller.DeviceConnected();
        Assert.False(controller.IsVisible);
        controller.Replay();
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public void PersistenceFailureKeepsSessionStateAndDoesNotRetryOnReconnectOrDismiss()
    {
        int writes = 0;
        HostSettingsJsonValue.BeforeWriteForTests = (_, _) => { writes++; throw new IOException("injected failure"); };
        try
        {
            var controller = new IntroductionController(hasRememberedDevices: false);
            controller.DeviceConnected();
            Assert.True(controller.IsVisible);
            controller.Dismiss();
            controller.DeviceConnected();
            Assert.False(controller.IsVisible);
            Assert.Equal(1, writes);
        }
        finally { HostSettingsJsonValue.BeforeWriteForTests = null; }
    }
}
