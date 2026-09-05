namespace VolturaAir.Host.Tests;

public sealed class ScreenViewH264ColorMetadataTests
{
    // SPS from the isolated Intel GPU synthetic-color baseline, with no color description.
    private static readonly byte[] Sequence = Convert.FromHexString("000000012742401f95b014016ec044000003000400000300f3a1000d900000d90aef7be0ed0e1970");

    [Fact]
    public void KnownBaselineSpsPreservesTimingAndHrdWhileDeclaring709SrgbLimited()
    {
        // Independently inspected VUI: format=5, full_range=0, primaries=1,
        // transfer=13, matrix=1; the original timing/HRD fields are preserved.
        byte[] expected = Convert.FromHexString("000000012742401f95b014016ec05a808680a000000300200000079d08006c800006c8577bdf076870cb80");
        Assert.Equal(expected, ScreenViewH264ColorMetadata.Apply(Sequence));
    }

    [Fact]
    public void ColorDescriptionIsAddedOnceWithoutChangingPictureOrPpsPayloads()
    {
        byte[] other = [0, 0, 1, 0x68, 0xce, 0, 0, 0, 1, 0x65, 0x01, 0x02];
        byte[] result = ScreenViewH264ColorMetadata.Apply([.. Sequence, .. other]);
        Assert.Equal(other, result[^other.Length..]);
        Assert.Equal(result, ScreenViewH264ColorMetadata.Apply(result));
        Assert.True(ScreenViewHardwareH264Encoder.ContainsNalType(result, 7));
        Assert.True(ScreenViewHardwareH264Encoder.ContainsNalType(result, 5));
        Assert.True(result.Length > Sequence.Length + other.Length);
    }

    [Fact]
    public void AccessUnitsWithoutSequenceHeadersAreUnchanged()
    {
        byte[] frame = [0, 0, 0, 1, 0x41, 5, 6, 0, 0, 3, 0];
        Assert.Equal(frame, ScreenViewH264ColorMetadata.Apply(frame));
    }

    [Theory]
    [InlineData("0000000167")]
    [InlineData("0000000167640000")]
    [InlineData("000000016742000000000000000000")]
    public void InvalidOrUnnegotiatedSequenceHeadersFailAtTheEncoderBoundary(string hex) =>
        Assert.Throws<ScreenViewCaptureException>(() => ScreenViewH264ColorMetadata.Apply(Convert.FromHexString(hex)));
}
