using System.Security.Cryptography;
using System.Text;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelaySessionCryptoTests
{
    private static readonly byte[] Secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] Transcript = Encoding.UTF8.GetBytes("voltura-air-relay-session-v1\nroute\nclient\nhost\nhost-key\nclient-key\nnonce");

    [Fact]
    public void HostFramesAreAuthenticatedAndDirectionBound()
    {
        using var channel = RelaySessionCrypto.CreateHostFromSharedSecretForConformance(Secret, Transcript);
        var frame = channel.Encrypt("host message"u8);

        Assert.Equal("0101000000000000000193026D69E19B495057ED0C29C7590A361AAC6F23D2317EE556962BD8", Convert.ToHexString(frame));
        Assert.Throws<CryptographicException>(() => channel.Decrypt(frame));
    }

    [Fact]
    public void DeviceFramesRejectTamperingAndReplay()
    {
        using var channel = RelaySessionCrypto.CreateHostFromSharedSecretForConformance(Secret, Transcript);
        var deviceFrame = Convert.FromHexString("010200000000000000010D50FE8F07881B182FA5FA6FAA44624E03F59260B2824659BFF232AD75E0");
        var plaintext = channel.Decrypt(deviceFrame);

        Assert.Equal("device message", Encoding.UTF8.GetString(plaintext));
        Assert.Throws<CryptographicException>(() => channel.Decrypt(deviceFrame));

        using var fresh = RelaySessionCrypto.CreateHostFromSharedSecretForConformance(Secret, Transcript);
        deviceFrame[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => fresh.Decrypt(deviceFrame));
    }

    [Fact]
    public void MaximumApplicationFrameAddsOnlyTheFixedRelayOverhead()
    {
        using var channel = RelaySessionCrypto.CreateHostFromSharedSecretForConformance(Secret, Transcript);

        var frame = channel.Encrypt(new byte[WebSocketTransport.MaxMessageBytes]);

        Assert.Equal(WebSocketTransport.MaxMessageBytes + RelaySessionCrypto.FrameOverhead, frame.Length);
        Assert.Equal(RelayEnvelope.MaximumPayloadBytes, frame.Length);
    }
}
