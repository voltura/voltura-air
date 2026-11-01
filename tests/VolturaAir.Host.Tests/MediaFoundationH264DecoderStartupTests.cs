using Xunit;
using VolturaAir.Host.Features.PhoneWebcam;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace VolturaAir.Host.Tests;

public sealed class MediaFoundationH264DecoderStartupTests
{
    [Fact]
    public void NativeOutputBufferIsReusedAcrossFramesFlushAndResolutionChangesThenReleased()
    {
        var buffers = new List<IMFMediaBuffer>();
        using var decoder = new MediaFoundationH264Decoder(length =>
        {
            IMFMediaBuffer buffer = MFCreateMemoryBuffer(length);
            buffers.Add(buffer);
            return buffer;
        });
        byte[] first = ReadFixture("chromium-bear-320x192-baseline-keyframe.h264.b64");
        byte[] second = ReadFixture("chromium-test-25fps-first-keyframe.h264.b64");
        for (int index = 0; index < 20; ++index) DecodeAndReturn(decoder, first);
        decoder.Flush();
        DecodeAndReturn(decoder, first);
        for (int index = 0; index < 3; ++index)
        {
            byte[]? frame = decoder.Decode(second);
            MediaFoundationH264Decoder.ReturnFrame(frame);
        }

        Assert.Equal((320, 240), decoder.DecodedSize);
        Assert.Single(buffers);
        Assert.Equal(1, decoder.OutputBufferAllocationCount);
        Assert.NotEqual(nint.Zero, buffers[0].NativePointer);
        decoder.Dispose();
        decoder.Dispose();
        Assert.Equal(nint.Zero, buffers[0].NativePointer);
    }

    [Fact]
    public void NativeOutputAllocationFailureAllowsFlushRecoveryAndDoesNotRetainTheReplacement()
    {
        int attempts = 0;
        IMFMediaBuffer? allocated = null;
        using var decoder = new MediaFoundationH264Decoder(length =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Injected output allocation failure.");
            allocated = MFCreateMemoryBuffer(length);
            return allocated;
        });
        byte[] keyFrame = ReadFixture("chromium-bear-320x192-baseline-keyframe.h264.b64");
        Assert.Throws<InvalidOperationException>(() => decoder.Decode(keyFrame));
        decoder.Flush();
        DecodeAndReturn(decoder, keyFrame);
        Assert.Equal(2, attempts);
        Assert.Equal(1, decoder.OutputBufferAllocationCount);
        Assert.NotNull(allocated);
        decoder.Dispose();
        Assert.Equal(nint.Zero, allocated.NativePointer);
    }

    [Fact]
    public void PublishedFrameRemainsIndependentOfLaterDecoderOutputAndShutdown()
    {
        byte[] accessUnit = ReadFixture("chromium-bear-320x192-baseline-keyframe.h264.b64");
        byte[]? retained = null;
        try
        {
            byte[] expected;
            using (var decoder = new MediaFoundationH264Decoder())
            {
                retained = decoder.Decode(accessUnit);
                Assert.NotNull(retained);
                expected = retained.AsSpan(0, MediaFoundationH264Decoder.FrameBytes).ToArray();
                for (int iteration = 0; iteration < 5; ++iteration)
                    DecodeAndReturn(decoder, accessUnit);
            }
            Assert.Equal(expected, retained.AsSpan(0, MediaFoundationH264Decoder.FrameBytes).ToArray());
        }
        finally
        {
            MediaFoundationH264Decoder.ReturnFrame(retained);
        }
    }

    [Fact]
    public void ConsecutiveKeyAndPredictedFramesDecodeWithoutFailure()
    {
        byte[][] accessUnits =
        [
            ReadFixture("chromium-bear-320x192-baseline-keyframe.h264.b64"),
            ReadFixture("chromium-bear-320x192-baseline-frame-1.h264.b64"),
            ReadFixture("chromium-bear-320x192-baseline-frame-2.h264.b64"),
            ReadFixture("chromium-bear-320x192-baseline-frame-3.h264.b64")
        ];

        using var decoder = new MediaFoundationH264Decoder();
        for (int iteration = 0; iteration < 25; ++iteration)
        {
            foreach (byte[] accessUnit in accessUnits)
            {
                byte[]? frame = null;
                try
                {
                    frame = decoder.Decode(accessUnit);
                    Assert.NotNull(frame);
                    Assert.True(frame.Length >= MediaFoundationH264Decoder.FrameBytes);
                }
                finally
                {
                    MediaFoundationH264Decoder.ReturnFrame(frame);
                }
            }
        }
    }

    [Fact]
    public void ResolutionChangeBetweenCompleteKeyFramesDecodesWithoutFailure()
    {
        byte[] first = ReadFixture("chromium-bear-320x192-baseline-keyframe.h264.b64");
        byte[] second = ReadFixture("chromium-test-25fps-first-keyframe.h264.b64");

        using var decoder = new MediaFoundationH264Decoder();
        DecodeAndReturn(decoder, first);
        (int Width, int Height) firstSize = decoder.DecodedSize;
        int frames = 0;
        for (int index = 0; index < 3; ++index)
        {
            byte[]? frame = decoder.Decode(second);
            if (frame is not null) ++frames;
            MediaFoundationH264Decoder.ReturnFrame(frame);
        }
        Assert.True(frames > 0);
        Assert.Equal((320, 192), firstSize);
        Assert.Equal((320, 240), decoder.DecodedSize);
    }

    private static void DecodeAndReturn(MediaFoundationH264Decoder decoder, byte[] accessUnit)
    {
        byte[]? frame = null;
        try
        {
            frame = decoder.Decode(accessUnit);
            Assert.NotNull(frame);
            Assert.True(frame.Length >= MediaFoundationH264Decoder.FrameBytes);
        }
        finally
        {
            MediaFoundationH264Decoder.ReturnFrame(frame);
        }
    }

    private static byte[] ReadFixture(string name) => Convert.FromBase64String(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PhoneWebcamTestData", name)));
}
