using Xunit;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class Nv12FrameComposerTests
{
    [Fact]
    public void CentersNv12WithoutScaling()
    {
        byte[] source =
        [
            20, 21, 22, 23,
            30, 31, 32, 33,
            40, 41, 42, 43,
            50, 51, 52, 53,
            60, 61, 62, 63,
            70, 71, 72, 73
        ];

        byte[] result = new byte[96];
        Nv12FrameComposer.FitIntoCanvas(source, 4, 4, 4, 0, 0, 4, result, 8, 8);

        Assert.Equal(96, result.Length);
        Assert.Equal([16, 16, 20, 21, 22, 23, 16, 16], result.AsSpan(16, 8).ToArray());
        Assert.Equal([16, 16, 50, 51, 52, 53, 16, 16], result.AsSpan(40, 8).ToArray());
        Assert.Equal([128, 128, 60, 61, 62, 63, 128, 128], result.AsSpan(72, 8).ToArray());
        Assert.Equal([128, 128, 70, 71, 72, 73, 128, 128], result.AsSpan(80, 8).ToArray());
    }

    [Fact]
    public void FitsPortraitFramesWithoutChangingAspectRatio()
    {
        byte[] source = Enumerable.Range(0, 48).Select(value => (byte)(value + 20)).ToArray();
        byte[] result = new byte[48];

        Nv12FrameComposer.FitIntoCanvas(source, 4, 8, 4, 0, 0, 8, result, 8, 4);

        Assert.Equal([16, 16, 20, 22, 16, 16, 16, 16], result.AsSpan(0, 8).ToArray());
        Assert.Equal([16, 16, 44, 46, 16, 16, 16, 16], result.AsSpan(24, 8).ToArray());
        Assert.Equal([128, 128, 52, 53, 128, 128, 128, 128], result.AsSpan(32, 8).ToArray());
    }

    [Fact]
    public void ExcludesCodecPaddingFromVisibleFrame()
    {
        byte[] source =
        [
            99, 99, 20, 21, 22, 23, 99, 99,
            99, 99, 30, 31, 32, 33, 99, 99,
            99, 99, 40, 41, 42, 43, 99, 99
        ];
        byte[] result = new byte[36];

        Nv12FrameComposer.FitIntoCanvas(source, 4, 2, 8, 2, 0, 2, result, 4, 6);

        Assert.Equal([20, 21, 22, 23], result.AsSpan(8, 4).ToArray());
        Assert.Equal([30, 31, 32, 33], result.AsSpan(12, 4).ToArray());
        Assert.Equal([40, 41, 42, 43], result.AsSpan(28, 4).ToArray());
    }

    [Fact]
    public void ReusesCallerBuffersWithoutAllocating()
    {
        byte[] source = new byte[24];
        byte[] target = new byte[96];
        Nv12FrameComposer.FitIntoCanvas(source, 4, 4, 4, 0, 0, 4, target, 8, 8);
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 100; ++index)
            Nv12FrameComposer.FitIntoCanvas(source, 4, 4, 4, 0, 0, 4, target, 8, 8);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ReusesFullSizePortraitBuffersWithoutAllocating()
    {
        byte[] source = new byte[1088 * 1920 * 3 / 2];
        byte[] target = new byte[1920 * 1080 * 3 / 2];
        Nv12FrameComposer.FitIntoCanvas(source, 1080, 1920, 1088, 0, 0, 1920, target, 1920, 1080);
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 5; ++index)
            Nv12FrameComposer.FitIntoCanvas(source, 1080, 1920, 1088, 0, 0, 1920, target, 1920, 1080);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
