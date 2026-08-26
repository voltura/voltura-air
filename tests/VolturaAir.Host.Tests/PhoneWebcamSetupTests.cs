using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamSetupTests
{
    [Theory]
    [InlineData("{\"installed\":true,\"cleanupRequired\":true,\"updateRequired\":false,\"revision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}", true, true, false, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("{\"installed\":true,\"cleanupRequired\":true,\"updateRequired\":true,\"revision\":null}", true, true, true, null)]
    [InlineData("{\"installed\":false,\"cleanupRequired\":false,\"updateRequired\":false,\"revision\":null}", false, false, false, null)]
    [InlineData("{\"installed\":false,\"cleanupRequired\":true,\"updateRequired\":false,\"revision\":null}", false, true, false, null)]
    public void StatusAcceptsOnlyTheBoundedStateAndRevision(
        string json,
        bool expectedInstalled,
        bool expectedCleanupRequired,
        bool expectedUpdateRequired,
        string? expectedRevision)
    {
        Assert.True(PhoneWebcamSetup.TryReadStatus(
            json,
            out bool installed,
            out bool cleanupRequired,
            out bool updateRequired,
            out string? revision));
        Assert.Equal(expectedInstalled, installed);
        Assert.Equal(expectedCleanupRequired, cleanupRequired);
        Assert.Equal(expectedUpdateRequired, updateRequired);
        Assert.Equal(expectedRevision, revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"installed\":\"true\",\"cleanupRequired\":true,\"updateRequired\":false}")]
    [InlineData("{\"installed\":true}")]
    [InlineData("{\"installed\":true,\"cleanupRequired\":false,\"updateRequired\":false,\"revision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}")]
    [InlineData("{\"installed\":false,\"cleanupRequired\":false,\"updateRequired\":true}")]
    [InlineData("{\"installed\":true,\"cleanupRequired\":true,\"updateRequired\":false,\"revision\":\"ABCDEF\"}")]
    [InlineData("{\"installed\":true,\"cleanupRequired\":true,\"updateRequired\":false,\"revision\":7}")]
    [InlineData("{\"other\":true}")]
    public void StatusRejectsMalformedOrWrongShapeOutput(string output)
    {
        Assert.False(PhoneWebcamSetup.TryReadStatus(output, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", false)]
    [InlineData("short", false)]
    [InlineData("", false)]
    public void RevisionAcceptsOnlyLowercaseSha256(string output, bool expected)
    {
        Assert.Equal(expected, PhoneWebcamSetup.TryReadRevision(output, out _));
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", false)]
    [InlineData(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public void InstalledAndPackagedComponentRevisionsMustMatch(
        string? installedRevision,
        string packagedRevision,
        bool expected)
    {
        Assert.Equal(expected, PhoneWebcamSetup.IsCurrentRevision(installedRevision, packagedRevision));
    }

    [Fact]
    public async Task MissingOptionalHelperKeepsTheFeatureAvailableForMaintenanceGuidance()
    {
        var setup = new PhoneWebcamSetup(Path.Combine(Path.GetTempPath(), $"missing-webcam-{Guid.NewGuid():N}.exe"));

        PhoneWebcamFeatureStatus status = await setup.GetStatusAsync(CancellationToken.None);
        await using PhoneWebcamFeature feature = await PhoneWebcamFeature.CreateAsync(setup);

        Assert.Equal(PhoneWebcamFeatureState.NotInstalled, status.State);
        Assert.Equal(status, feature.Status);
    }
}
