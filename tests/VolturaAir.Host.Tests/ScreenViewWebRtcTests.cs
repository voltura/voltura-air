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
    public async Task BundledPeerGeneratesTheExactTwoTrackOffer()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var peer = new ScreenViewWebRtcPeer();
        string offer = await peer.CreateOfferAsync(TestContext.Current.CancellationToken);

        Assert.True(ScreenViewWebRtcPeer.HasExpectedMedia(offer, "sendonly"));
    }

    [Fact]
    public void AdvertisesABaselineLevelThatSupportsTheMaximum4k60Stream()
    {
        Assert.Contains("profile-level-id=42e034", ScreenViewWebRtcPeer.H264FormatParameters, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresExactlyH264VideoAndStereoOpusAudio()
    {
        const string offer = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\n" +
            "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=sendonly\r\n" +
            "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n";

        Assert.True(ScreenViewWebRtcPeer.HasExpectedMedia(offer, "sendonly"));
        Assert.False(ScreenViewWebRtcPeer.HasExpectedMedia(offer.Replace("opus/48000/2", "PCMU/8000", StringComparison.Ordinal), "sendonly"));
        Assert.False(ScreenViewWebRtcPeer.HasExpectedMedia(offer.Replace("a=sendonly", "a=recvonly", StringComparison.Ordinal), "sendonly"));
        Assert.False(ScreenViewWebRtcPeer.HasExpectedMedia(offer.Replace("m=application 9", "m=application 0", StringComparison.Ordinal), "sendonly"));
        Assert.False(ScreenViewWebRtcPeer.HasExpectedMedia(offer.Replace("UDP/DTLS/SCTP", "UDP/TLS/RTP/SAVPF", StringComparison.Ordinal), "sendonly"));
        Assert.False(ScreenViewWebRtcPeer.HasExpectedMedia(offer.Replace("webrtc-datachannel", "5000", StringComparison.Ordinal), "sendonly"));
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
