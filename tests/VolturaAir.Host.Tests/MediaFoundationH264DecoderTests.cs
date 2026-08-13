using Xunit;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class MediaFoundationH264DecoderTests
{
    [Fact]
    public void InitializesWithoutPredeclaringCompressedFrameDimensions()
    {
        using var decoder = new MediaFoundationH264Decoder();
    }
}
