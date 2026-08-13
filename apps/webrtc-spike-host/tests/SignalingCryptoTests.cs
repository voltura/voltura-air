using System.Security.Cryptography;
using Xunit;

namespace WebRtcSpike.Host.Tests;

public sealed class SignalingCryptoTests
{
    private const string Room = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void RoundTripsCiphertextWithoutExposingPlaintext()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var value = new Payload("answer", "v=0\r\nsecret");
        EncryptedEnvelope envelope = SignalingCrypto.Encrypt(key, Room, value);

        Assert.DoesNotContain("secret", envelope.Ciphertext, StringComparison.Ordinal);
        Assert.Equal(value, SignalingCrypto.Decrypt<Payload>(key, Room, envelope));
    }

    [Fact]
    public void RejectsWrongKeyAndTampering()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        EncryptedEnvelope envelope = SignalingCrypto.Encrypt(key, Room, new Payload("offer", "v=0"));

        Assert.Throws<InvalidOperationException>(() =>
            SignalingCrypto.Decrypt<Payload>(RandomNumberGenerator.GetBytes(32), Room, envelope));
        string padded = envelope.Ciphertext.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        byte[] changedBytes = Convert.FromBase64String(padded);
        changedBytes[0] ^= 1;
        string changed = SignalingCrypto.Base64Url(changedBytes);
        Assert.Throws<InvalidOperationException>(() =>
            SignalingCrypto.Decrypt<Payload>(key, Room, envelope with { Ciphertext = changed }));
    }

    private sealed record Payload(string Type, string Sdp);
}
