using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.Devices;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void DevicesPageMapsConnectionStatesToSemanticPills()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var connected = new DeviceListItem("connected", "Connected device", "Connected", true, "Connected now", string.Empty);
            var disconnected = new DeviceListItem("disconnected", "Disconnected device", "Not connected", false, "Disconnected earlier", string.Empty);
            var page = new DevicesPageView([connected, disconnected], static () => { }, static () => { }, static () => { });
            AssertStatusPill(page, connected, PillBadgeTone.Success);
            AssertStatusPill(page, disconnected, PillBadgeTone.Danger);
        });
    }

    private static void AssertStatusPill(
        DevicesPageView page,
        DeviceListItem device,
        PillBadgeTone expectedTone)
    {
        var templateRoot = Assert.IsAssignableFrom<FrameworkElement>(page.Devices.ItemTemplate.LoadContent());
        templateRoot.DataContext = device;
        templateRoot.Measure(new Size(600, 200));
        templateRoot.Arrange(new Rect(0, 0, 600, 200));
        templateRoot.UpdateLayout();
        var pill = Assert.IsType<PillBadge>(templateRoot.FindName("ConnectionStatusPill"));

        Assert.Equal("ConnectionStatusPill", pill.Name);
        Assert.Equal(device.Status, pill.Content);
        Assert.Equal(expectedTone, pill.Tone);
    }
}
