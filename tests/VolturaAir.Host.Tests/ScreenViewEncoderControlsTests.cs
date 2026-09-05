using Vortice.DXGI;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewEncoderControlsTests
{
    [Fact]
    public void RejectedKeyframeControlIsNotRetriedOnTheSameEncoder()
    {
        int calls = 0;
        using var controls = new ScreenViewEncoderControls((_, _, _) => { calls++; return false; });
        Assert.False(controls.TryRequestKeyFrame());
        Assert.False(controls.TryRequestKeyFrame());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void KeyframesAndBitrateUpdatesUseExistingControls()
    {
        var calls = new List<(Guid Property, uint Value, bool Boolean)>();
        using var controls = new ScreenViewEncoderControls((property, value, boolean) =>
        {
            calls.Add((property, value, boolean));
            return true;
        });
        controls.Configure(4_000_000);
        calls.Clear();
        Assert.True(controls.TryRequestKeyFrame());
        Assert.True(controls.TrySetBitrate(2_000_000));
        Assert.Equal(new[]
        {
            (ScreenViewEncoderControls.ForceKeyFrame, 1u, false),
            (ScreenViewEncoderControls.MaximumBitrate, 2_000_000u, false),
            (ScreenViewEncoderControls.MeanBitrate, 2_000_000u, false)
        }, calls);
    }

    [Fact]
    public void FailedPeakUpdateRequiresReplacementBeforeAnyMoreFrames()
    {
        bool reject = false;
        int meanCalls = 0;
        using var controls = new ScreenViewEncoderControls((property, _, _) =>
        {
            if (property == ScreenViewEncoderControls.MeanBitrate) meanCalls++;
            return property != ScreenViewEncoderControls.MaximumBitrate || !reject;
        });
        controls.Configure(4_000_000);
        reject = true;
        Assert.False(controls.TrySetBitrate(2_000_000));
        Assert.False(controls.TrySetBitrate(2_000_000));
        Assert.Equal(1, meanCalls);
    }

    [Theory]
    [InlineData(0u, 1f)]
    [InlineData(1000u, 1f)]
    [InlineData(2500u, 0.4f)]
    [InlineData(uint.MaxValue, 1f)]
    public void NormalizesWindowsReferenceWhite(uint level, float expected) =>
        Assert.Equal(expected, ScreenViewDisplayColor.NormalizeWhiteLevel(level));

    [Theory]
    [InlineData(Format.R16G16B16A16_Float, true)]
    [InlineData(Format.B8G8R8A8_UNorm, false)]
    [InlineData(Format.R10G10B10A2_UNorm, false)]
    public void OnlyFloatingPointScRgbUsesHdrToneMapping(Format format, bool expected) =>
        Assert.Equal(expected, D3D11DesktopFrameConverter.IsScRgb(format));

    [Theory]
    [InlineData(unchecked((int)0x887A0004), true)]
    [InlineData(unchecked((int)0x80070057), true)]
    [InlineData(unchecked((int)0x80004002), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(unchecked((int)0x887A0022), false)]
    public void CaptureFallbackDoesNotHideAccessOrSessionFailure(int result, bool expected) =>
        Assert.Equal(expected, ScreenViewDisplayColor.CanFallback(result));
}
