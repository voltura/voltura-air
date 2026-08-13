using Xunit;

namespace WebRtcSpike.Host.Tests;

public sealed class MediaFoundationH264DecoderStartupTests
{
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
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name)));
}
