namespace VolturaAir.Host.Tests;

public sealed class ScreenViewWebRtcTests
{
    [Fact]
    public void BundledPeerAcceptsTheOfficialRelayConfiguration()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var peer = new ScreenViewWebRtcPeer(new ScreenViewPeerConfiguration(
        [
            "turns:user:credential@turn.cloudflare.com:443?transport=tcp",
            "turn:user:credential@turn.cloudflare.com:3478?transport=udp"
        ], RelayOnly: true), _ => new FakeTurnTlsBridge());
    }

    [Fact]
    public void AdvertisesABaselineLevelThatSupportsTheMaximum4k60Stream()
    {
        Assert.Contains("profile-level-id=42e034", ScreenViewWebRtcPeer.H264FormatParameters, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertsAvcParameterSetsToTheAnnexBFormatUsedByTheRtpPacketizer()
    {
        byte[] configuration =
        [
            1, 0x42, 0xe0, 0x1f, 0xff,
            0xe1, 0, 3, 0x67, 0x42, 0xe0,
            1, 0, 2, 0x68, 0xce
        ];

        byte[] annexB = ScreenViewHardwareH264Encoder.ConvertAvcConfiguration(configuration);

        Assert.Equal([0, 0, 0, 1, 0x67, 0x42, 0xe0, 0, 0, 0, 1, 0x68, 0xce], annexB);
        Assert.True(ScreenViewHardwareH264Encoder.ContainsNalType(annexB, 7));
        Assert.True(ScreenViewHardwareH264Encoder.ContainsNalType(annexB, 8));
    }

    [Fact]
    public void DetectsKeyframesInBothAnnexBAndLengthPrefixedAccessUnits()
    {
        Assert.True(ScreenViewHardwareH264Encoder.ContainsNalType([0, 0, 0, 1, 0x65, 1, 2], 5));
        Assert.True(ScreenViewHardwareH264Encoder.ContainsNalType([0, 0, 0, 3, 0x65, 1, 2], 5));
        Assert.False(ScreenViewHardwareH264Encoder.ContainsNalType([0, 0, 0, 1, 0x61, 1, 2], 5));
    }

    [Fact]
    public void NormalizesLengthPrefixedEncoderOutputBeforeRtpPacketization()
    {
        Assert.Equal(
            [0, 0, 0, 1, 0x65, 1, 2, 0, 0, 0, 1, 0x61],
            ScreenViewHardwareH264Encoder.NormalizeToAnnexB([0, 0, 0, 3, 0x65, 1, 2, 0, 0, 0, 1, 0x61]));
        Assert.Throws<ScreenViewCaptureException>(() =>
            ScreenViewHardwareH264Encoder.NormalizeToAnnexB([0, 0, 0, 8, 0x65]));
    }

    [Fact]
    public void HardwareEncoderSelectionContinuesAfterActivationAndConfigurationFailures()
    {
        var rejected = new FakeEncoderCandidate();
        var selected = new FakeEncoderCandidate();
        int configured = 0;

        FakeEncoderCandidate result = ScreenViewHardwareH264Encoder.SelectFirstCompatible(
            [
                () => throw new InvalidOperationException("activation failed"),
                () => rejected,
                () => selected
            ],
            candidate =>
            {
                configured++;
                if (ReferenceEquals(candidate, rejected))
                    throw new InvalidOperationException("configuration failed");
            },
            lastFailure => new InvalidOperationException("No encoder was compatible.", lastFailure));

        Assert.Same(selected, result);
        Assert.True(rejected.Disposed);
        Assert.False(selected.Disposed);
        Assert.Equal(2, configured);
    }

    private sealed class FakeEncoderCandidate : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeTurnTlsBridge : ITurnTlsBridge
    {
        public string LocalIceServerUri => "turn:user:credential@127.0.0.1:41234?transport=udp";
        public string? FailureCode => null;
        public void Dispose()
        {
        }
    }
}
