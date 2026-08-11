using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class PresentationLaserPointerControllerTests : IsolatedHostSettingsTest
{
    [Fact]
    public void RecoveryRevocationClearsStateWithoutApplyingAnotherCursor()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController((enabled, _) => applied.Add(enabled));
        var changes = 0;
        controller.StateChanged += (_, _) => changes += 1;
        controller.SetEnabled("client-a", enabled: true);

        controller.Revoke();

        Assert.False(controller.IsEnabled);
        Assert.Equal([true], applied);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void DesiredStateIsIdempotentAndOnlyTheOwnerCanDisable()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController((enabled, _) => applied.Add(enabled));

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
    public void TakeoverDisablesTheActiveCursorRegardlessOfOwner()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController(
            (enabled, _) => applied.Add(enabled));
        controller.SetEnabled("device-a", enabled: true, runtimePresentationId: "presentation-a");

        controller.DisableForTakeover();

        Assert.False(controller.IsEnabled);
        Assert.Equal([true, false], applied);
    }

    [Fact]
    public void DisposeRestoresAnActiveLaser()
    {
        var applied = new List<bool>();
        var controller = new PresentationLaserPointerController((enabled, _) => applied.Add(enabled));
        controller.SetEnabled("device-a", enabled: true);

        controller.Dispose();

        Assert.False(controller.IsEnabled);
        Assert.Equal([true, false], applied);
    }

    [Fact]
    public void PermissionReevaluationRestoresLaserOnlyWhenOwnerLosesControl()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController((enabled, _) => applied.Add(enabled));
        controller.SetEnabled("device-a", enabled: true);

        controller.DisableIfOwnerCannotControl(clientId => clientId == "device-a");
        Assert.True(controller.IsEnabled);

        controller.DisableIfOwnerCannotControl(_ => false);

        Assert.False(controller.IsEnabled);
        Assert.Equal([true, false], applied);
    }

    [Fact]
    public void OwnerCanRecolorAndToggleOffByConcreteColor()
    {
        var applied = new List<(bool Enabled, PresentationLaserColor? Color)>();
        using var controller = new PresentationLaserPointerController(
            (enabled, color) => applied.Add((enabled, color)));

        Assert.Equal(
            LaserPointerChangeOutcome.Changed,
            controller.Toggle("device-a", PresentationLaserColor.Red));
        Assert.Equal(PresentationLaserColor.Red, controller.ActiveColor);

        Assert.Equal(
            LaserPointerChangeOutcome.Changed,
            controller.Toggle("device-a", PresentationLaserColor.Green));
        Assert.Equal(PresentationLaserColor.Green, controller.ActiveColor);

        Assert.Equal(
            LaserPointerChangeOutcome.Changed,
            controller.Toggle("device-a", PresentationLaserColor.Green));
        Assert.False(controller.IsEnabled);
        Assert.Null(controller.ActiveColor);
        Assert.Equal(
            [
                (true, (PresentationLaserColor?)PresentationLaserColor.Red),
                (true, (PresentationLaserColor?)PresentationLaserColor.Green),
                (false, null)
            ],
            applied);
    }

    [Fact]
    public void AnotherOwnerCannotEnableRecolorOrDisable()
    {
        var applied = new List<(bool Enabled, PresentationLaserColor? Color)>();
        using var controller = new PresentationLaserPointerController(
            (enabled, color) => applied.Add((enabled, color)));
        controller.SetEnabled("device-a", enabled: true, colorOverride: PresentationLaserColor.Blue);

        Assert.Equal(
            LaserPointerChangeOutcome.OwnerConflict,
            controller.SetEnabled("device-b", enabled: true, colorOverride: PresentationLaserColor.Red));
        Assert.Equal(
            LaserPointerChangeOutcome.OwnerConflict,
            controller.SetEnabled("device-b", enabled: false));
        Assert.Equal(PresentationLaserColor.Blue, controller.ActiveColor);
        Assert.Equal([(true, (PresentationLaserColor?)PresentationLaserColor.Blue)], applied);
    }

    [Fact]
    public void ExplicitOffIsIdempotent()
    {
        var applied = new List<bool>();
        using var controller = new PresentationLaserPointerController(
            (enabled, _) => applied.Add(enabled));

        Assert.Equal(
            LaserPointerChangeOutcome.Unchanged,
            controller.SetEnabled("device-a", enabled: false));
        controller.SetEnabled("device-a", enabled: true);
        Assert.Equal(
            LaserPointerChangeOutcome.Changed,
            controller.SetEnabled("device-a", enabled: false));
        Assert.Equal(
            LaserPointerChangeOutcome.Unchanged,
            controller.SetEnabled("device-a", enabled: false));

        Assert.Equal([true, false], applied);
    }

    [Fact]
    public void DefaultFollowsPreferencesWhileExplicitColorDoesNot()
    {
        AppPointerSettings.SetPresentationLaserPointer(
            new PresentationLaserPointerSettings(6, PresentationLaserColor.Red));
        using var controller = new PresentationLaserPointerController((_, _) => { });
        controller.SetEnabled("device-a", enabled: true);
        Assert.Equal(PresentationLaserColor.Red, controller.ActiveColor);

        AppPointerSettings.SetPresentationLaserPointer(
            new PresentationLaserPointerSettings(9, PresentationLaserColor.Green));
        Assert.Equal(PresentationLaserColor.Green, controller.ActiveColor);

        controller.SetEnabled(
            "device-a",
            enabled: true,
            colorOverride: PresentationLaserColor.Blue);
        AppPointerSettings.SetPresentationLaserPointer(
            new PresentationLaserPointerSettings(4, PresentationLaserColor.Red));
        Assert.Equal(PresentationLaserColor.Blue, controller.ActiveColor);
    }
}
