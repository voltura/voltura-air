using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenNavigationRingTests
{
    [Fact]
    public void PublishesResponsiveLayoutAndRemoteInputAvailability()
    {
        var service = new CustomScreenService(
            new InMemoryCustomScreenStore(),
            new FakeAppLaunchService());
        var draft = CustomScreenService.CreateNavigationRing(
            CustomScreenService.CreateDraft());
        var navigationRing = draft.Sections[^1] with
        {
            WidthColumns = 8,
            HeightMode = "fill",
            FillWeight = 2
        };
        draft = draft with
        {
            AssignedClientIds = ["phone-a"],
            Sections = [navigationRing]
        };

        Assert.True(service.TrySave(draft, out var saved, out var error), error);
        var mobile = service.GetMobileDefinition(
            "phone-a",
            saved.Id,
            canUseRemoteInput: false,
            canLaunchApps: false);

        var section = Assert.Single(mobile!.Sections);
        Assert.Equal("navigationRing", section.Kind);
        Assert.Equal(8, section.WidthColumns);
        Assert.Equal("fill", section.HeightMode);
        Assert.Equal(2, section.FillWeight);
        Assert.Empty(section.Buttons);
        Assert.False(section.TrackpadEnabled);
        Assert.Contains("Remote input", section.TrackpadUnavailableReason);

        var invalid = draft with
        {
            Sections = [navigationRing with { WidthColumns = 4 }]
        };
        Assert.False(service.TrySave(invalid, out _, out _));

        var invalidOverride = draft with
        {
            OrientationLayoutsEnabled = true,
            Sections =
            [
                navigationRing with
                {
                    Portrait = new CustomScreenLayoutOverride(0, true, 4)
                }
            ]
        };
        Assert.False(service.TrySave(invalidOverride, out _, out _));
    }
}
