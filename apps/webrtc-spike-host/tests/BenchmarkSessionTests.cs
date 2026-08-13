using Xunit;
using ZXing;

namespace WebRtcSpike.Host.Tests;

public sealed class BenchmarkSessionTests
{
    [Fact]
    public void PatternRefreshLimitsTimestampStepWhileRemainingCameraReadable()
    {
        Assert.Equal(33, BenchmarkSession.PatternIntervalMilliseconds);
    }

    [Fact]
    public void CountsOnlyNewPatternsAndDerivesDropsFromSequenceGaps()
    {
        long? lastSequence = null;
        int drops = 0;

        Assert.True(BenchmarkSession.TryAccountPattern(10, ref lastSequence, ref drops));
        Assert.False(BenchmarkSession.TryAccountPattern(10, ref lastSequence, ref drops));
        Assert.False(BenchmarkSession.TryAccountPattern(9, ref lastSequence, ref drops));
        Assert.True(BenchmarkSession.TryAccountPattern(13, ref lastSequence, ref drops));

        Assert.Equal(13, lastSequence);
        Assert.Equal(2, drops);
    }

    [Fact]
    public void ReadsOnlyVersionedBenchmarkPatterns()
    {
        Assert.True(BenchmarkSession.TryReadPattern("VA1:123456:78", out long timestamp, out long sequence));
        Assert.Equal(123456, timestamp);
        Assert.Equal(78, sequence);

        Assert.False(BenchmarkSession.TryReadPattern("VA2:123456:78", out _, out _));
        Assert.False(BenchmarkSession.TryReadPattern("VA1:not-a-time:78", out _, out _));
        Assert.False(BenchmarkSession.TryReadPattern("VA1:123456", out _, out _));
    }

    [Fact]
    public void GeneratedPatternRoundTripsThroughQrDecoder()
    {
        using System.Drawing.Bitmap bitmap = BenchmarkSession.CreatePatternBitmap(123456, 78);
        byte[] luma = new byte[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; ++y)
        {
            for (int x = 0; x < bitmap.Width; ++x)
            {
                System.Drawing.Color pixel = bitmap.GetPixel(x, y);
                luma[y * bitmap.Width + x] = pixel.R;
            }
        }

        ZXing.Result? decoded = BenchmarkSession.CreateQrReader().Decode(
            new RGBLuminanceSource(luma, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8));
        Assert.True(BenchmarkSession.TryReadPattern(decoded?.Text, out long timestamp, out long sequence));
        Assert.Equal(123456, timestamp);
        Assert.Equal(78, sequence);
    }

    [Fact]
    public void PipelineFailureInvalidatesBenchmarkEvidence()
    {
        var decoderFailure = new InvalidOperationException("decoder failed");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => BenchmarkSession.EnsurePipelineHealthy(() => decoderFailure));

        Assert.Same(decoderFailure, failure.InnerException);
        Assert.Contains("evidence was not written", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1920, 1080, 28, 300, true)]
    [InlineData(1080, 1920, 30, 250, true)]
    [InlineData(1280, 720, 30, 250, false)]
    [InlineData(1920, 1080, 27.9, 250, false)]
    [InlineData(1920, 1080, 30, 301, false)]
    public void PassRequiresFullHdEffectiveFrameRateAndLatency(
        int width,
        int height,
        double fps,
        double p95,
        bool expected)
    {
        var result = new BenchmarkResult(
            "direct", width, height, true, 300, fps, 200, p95, 0, 300, 5, 100_000, DateTimeOffset.UtcNow);

        Assert.Equal(expected, BenchmarkSession.MeetsPassCriteria(result));
    }

    [Fact]
    public void PassRejectsAnyNonFullHdMeasuredFrame()
    {
        var result = new BenchmarkResult(
            "relay", 1920, 1080, false, 300, 30, 200, 250, 0, 300, 5, 100_000, DateTimeOffset.UtcNow);

        Assert.False(BenchmarkSession.MeetsPassCriteria(result));
    }
}
