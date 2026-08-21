namespace VolturaAir.Host.Tests;

public sealed class ScreenViewQualityControllerTests
{
    private static readonly ScreenViewReceiverQuality Healthy = new(3840, 2160, 30, 60, 0, 0, 0);
    private static readonly ScreenViewReceiverQuality Unhealthy = new(3840, 2160, 10, 10, 10, 1, 25);

    [Fact]
    public void AutomaticStartsAtNativeResolutionAnd30FramesPerSecond()
    {
        var controller = new ScreenViewQualityController(Source(3840, 2160), DirectScreenQualityMode.Automatic);

        Assert.Equal(new ScreenViewCaptureProfile(3840, 2160, true, 30), controller.Current.CaptureProfile);
        Assert.True(controller.Current.TargetBitrate > 8_000_000);
    }

    [Fact]
    public void ReadabilityFloorUsesWindowsEffectiveDpiForAnyDisplaySize()
    {
        Assert.Equal((2560, 1440), ScreenViewQualityController.ReadabilityFloor(Source(3840, 2160, 144)));
        Assert.Equal((1280, 800), ScreenViewQualityController.ReadabilityFloor(Source(1920, 1200, 144)));
        Assert.Equal((1366, 768), ScreenViewQualityController.ReadabilityFloor(Source(1366, 768, 96)));
    }

    [Fact]
    public void AutomaticProfilesContainIntermediateResolutionAndNeverCrossReadabilityFloor()
    {
        List<ScreenViewQualityProfile> profiles = ScreenViewQualityController.CreateProfiles(
            Source(3840, 2160, 144),
            DirectScreenQualityMode.Automatic,
            100_000_000);

        Assert.Contains(profiles, profile => profile.Width == 2560 && profile.Height == 1440 && profile.FramesPerSecond == 30);
        Assert.All(profiles, profile =>
        {
            Assert.True(profile.Width >= 2560);
            Assert.True(profile.Height >= 1440);
        });
        Assert.All(profiles.Zip(profiles.Skip(1)), pair =>
            Assert.True(pair.Second.RequiredBitrate < pair.First.RequiredBitrate, $"{pair.First} -> {pair.Second}"));
        Assert.Equal(new ScreenViewCaptureProfile(3840, 2160, true, 60), profiles[0].CaptureProfile);
        Assert.Equal(new ScreenViewCaptureProfile(3840, 2160, true, 30), profiles[1].CaptureProfile);
    }

    [Fact]
    public void CodecTechnicalLimitBoundsEightKWithoutUsingAnswerLevelAsAProductCeiling()
    {
        List<ScreenViewQualityProfile> profiles = ScreenViewQualityController.CreateProfiles(
            Source(7680, 4320),
            DirectScreenQualityMode.Automatic,
            100_000_000);

        ScreenViewQualityProfile largest = profiles[0];
        long macroblocks = (long)((largest.Width + 15) / 16) * ((largest.Height + 15) / 16);
        Assert.True(macroblocks <= ScreenViewH264Limits.Level52.MaximumMacroblocksPerFrame);
        Assert.True(macroblocks * largest.FramesPerSecond <= ScreenViewH264Limits.Level52.MaximumMacroblocksPerSecond);
    }

    [Fact]
    public void QualityKeepsTheReadableResolutionWhileAdaptingFrameRate()
    {
        List<ScreenViewQualityProfile> profiles = ScreenViewQualityController.CreateProfiles(
            Source(3840, 2160, 192),
            DirectScreenQualityMode.Quality,
            100_000_000);

        Assert.All(profiles, profile =>
        {
            Assert.Equal(3840, profile.Width);
            Assert.Equal(2160, profile.Height);
        });
    }

    [Fact]
    public void RelayUsesSameProfilesInsideItsBitrateCeiling()
    {
        var controller = new ScreenViewQualityController(
            Source(3840, 2160),
            DirectScreenQualityMode.Automatic,
            maximumBitrate: 8_000_000);

        Assert.Equal(3840, controller.Current.Width);
        Assert.True(controller.Current.RequiredBitrate <= 8_000_000);
        Assert.True(controller.Current.TargetBitrate <= 8_000_000);
    }

    [Fact]
    public void DataSaverMayGoBelowReadabilityFloorButRemainsBounded()
    {
        var controller = new ScreenViewQualityController(Source(3840, 2160), DirectScreenQualityMode.DataSaver);

        Assert.True(controller.Current.Width <= 1920);
        Assert.True(controller.Current.Height <= 1080);
        Assert.True(controller.Current.TargetBitrate <= 4_000_000);
    }

    [Fact]
    public void PortraitDataSaverPreservesSourceAspectRatio()
    {
        List<ScreenViewQualityProfile> profiles = ScreenViewQualityController.CreateProfiles(
            Source(1080, 1920), DirectScreenQualityMode.DataSaver, 4_000_000);

        Assert.All(profiles, profile => Assert.Equal(9d / 16d, (double)profile.Width / profile.Height, 2));
    }

    [Fact]
    public void BackpressureRequiresThreeFailuresAndMovesOnlyOneProfile()
    {
        var now = DateTimeOffset.UtcNow;
        var controller = new ScreenViewQualityController(Source(1920, 1080, 144), DirectScreenQualityMode.Automatic);
        ScreenViewQualityProfile initial = controller.Current;

        Assert.False(controller.ReportBackpressure(now));
        Assert.False(controller.ReportBackpressure(now.AddSeconds(1)));
        Assert.True(controller.ReportBackpressure(now.AddSeconds(2)));
        ScreenViewQualityProfile reduced = controller.Current;
        Assert.NotEqual(initial, reduced);
        Assert.False(controller.ReportBackpressure(now.AddSeconds(3)));
        Assert.False(controller.ReportBackpressure(now.AddSeconds(4)));
        Assert.False(controller.ReportBackpressure(now.AddSeconds(5)));
        Assert.Equal(reduced, controller.Current);
        Assert.False(controller.ReportBackpressure(now.AddSeconds(7)));
        Assert.False(controller.ReportBackpressure(now.AddSeconds(8)));
        Assert.True(controller.ReportBackpressure(now.AddSeconds(9)));
        Assert.NotEqual(reduced, controller.Current);
    }

    [Fact]
    public void ReceiverFailureReducesOnceAndSustainedHealthRegainsQuality()
    {
        var now = DateTimeOffset.UtcNow;
        var controller = new ScreenViewQualityController(Source(1920, 1080, 144), DirectScreenQualityMode.Automatic);
        ScreenViewQualityProfile initial = controller.Current;

        Assert.False(controller.ReportReceiverQuality(Unhealthy, now));
        Assert.True(controller.ReportReceiverQuality(Unhealthy, now.AddSeconds(2)));
        ScreenViewQualityProfile reduced = controller.Current;
        Assert.NotEqual(initial, reduced);
        Assert.False(controller.ReportReceiverQuality(Healthy, now.AddSeconds(3)));
        Assert.True(controller.ReportReceiverQuality(Healthy, now.AddSeconds(18)));
        Assert.Equal(initial, controller.Current);
    }

    [Fact]
    public void StaticDesktopWithNoNewDecodedFramesDoesNotReduceQuality()
    {
        var now = DateTimeOffset.UtcNow;
        var controller = new ScreenViewQualityController(Source(3840, 2160, 144), DirectScreenQualityMode.Automatic);
        ScreenViewQualityProfile initial = controller.Current;
        var idle = new ScreenViewReceiverQuality(3840, 2160, 0, 0, 0, 0, 0);

        Assert.False(controller.ReportReceiverQuality(idle, now));
        Assert.False(controller.ReportReceiverQuality(idle, now.AddSeconds(30)));
        Assert.Equal(initial, controller.Current);
    }

    [Fact]
    public void RelaySourceSwitchRemainsInsideItsCeiling()
    {
        var controller = new ScreenViewQualityController(Source(1920, 1080), DirectScreenQualityMode.Automatic, 4_000_000);

        controller.SetSource(Source(3840, 2160));

        Assert.True(controller.Current.TargetBitrate <= 4_000_000);
    }

    [Fact]
    public void FailedUpgradeRollsBackAndIsTemporarilyUnavailable()
    {
        var now = DateTimeOffset.UtcNow;
        var controller = new ScreenViewQualityController(Source(1920, 1080, 144), DirectScreenQualityMode.Automatic);
        Assert.False(controller.ReportReceiverQuality(Unhealthy, now));
        Assert.True(controller.ReportReceiverQuality(Unhealthy, now.AddSeconds(1)));
        ScreenViewQualityProfile reduced = controller.Current;
        Assert.False(controller.ReportReceiverQuality(Healthy, now.AddSeconds(2)));
        Assert.True(controller.ReportReceiverQuality(Healthy, now.AddSeconds(17)));

        Assert.False(controller.ReportReceiverQuality(Unhealthy, now.AddSeconds(18)));
        Assert.True(controller.ReportReceiverQuality(Unhealthy, now.AddSeconds(19)));
        Assert.Equal(reduced, controller.Current);
        Assert.False(controller.ReportReceiverQuality(Healthy, now.AddSeconds(20)));
        Assert.False(controller.ReportReceiverQuality(Healthy, now.AddSeconds(36)));
        Assert.Equal(reduced, controller.Current);
    }

    [Fact]
    public void UnsupportedProfileFallsBackWithinReadableCandidates()
    {
        var controller = new ScreenViewQualityController(Source(3840, 2160, 144), DirectScreenQualityMode.Automatic);
        var now = DateTimeOffset.UtcNow;

        while (controller.ReportProfileUnsupported(now)) { }

        Assert.True(controller.Current.Width >= 2560);
        Assert.True(controller.Current.Height >= 1440);
        Assert.Equal(5, controller.Current.FramesPerSecond);
    }

    private static ScreenViewSource Source(int width, int height, int dpi = 96) =>
        new("display-1", "Display 1", width, height, true, EffectiveDpiX: dpi, EffectiveDpiY: dpi);
}
