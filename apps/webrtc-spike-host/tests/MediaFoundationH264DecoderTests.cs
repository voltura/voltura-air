using Xunit;

namespace WebRtcSpike.Host.Tests;

public sealed class MediaFoundationH264DecoderTests
{
    [Fact]
    public void InitializesWithoutPredeclaringCompressedFrameDimensions()
    {
        using var decoder = new MediaFoundationH264Decoder();
    }
}
