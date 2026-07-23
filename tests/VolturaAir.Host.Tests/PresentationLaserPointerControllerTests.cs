using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PresentationLaserPointerControllerTests
{
    [Fact]
    public void DesiredStateIsIdempotentAndOnlyTheOwnerCanDisable()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController(applied.Add);

        controller.SetEnabled("device-a", enabled: true);
        controller.SetEnabled("device-b", enabled: true);
        controller.DisableForClient("device-b");

        Assert.True(controller.IsEnabled);
        Assert.Equal([true], applied);

        controller.DisableForClient("device-a");

        Assert.False(controller.IsEnabled);
        Assert.Equal([true, false], applied);
    }

    [Fact]
    public void DisposeRestoresAnActiveLaser()
    {
        var applied = new List<bool>();
        var controller = new PresentationLaserPointerController(applied.Add);
        controller.SetEnabled("device-a", enabled: true);

        controller.Dispose();

        Assert.False(controller.IsEnabled);
        Assert.Equal([true, false], applied);
    }

    [Fact]
    public void PermissionReevaluationRestoresLaserOnlyWhenOwnerLosesControl()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController(applied.Add);
        controller.SetEnabled("device-a", enabled: true);

        controller.DisableIfOwnerCannotControl(clientId => clientId == "device-a");
        Assert.True(controller.IsEnabled);

        controller.DisableIfOwnerCannotControl(_ => false);

        Assert.False(controller.IsEnabled);
        Assert.Equal([true, false], applied);
    }
}
