using System.Security.Cryptography;
using System.Text;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelayRoutingIdentityTests
{
    [Fact]
    public void HostChallengeUsesTheSharedTypeScriptTranscript()
    {
        using var identity = RelayRoutingIdentity.CreateEphemeral();
        var challenge = ScreenViewHostIdentity.Base64Url(new byte[32]);
        var signature = ScreenViewHostIdentity.DecodeBase64Url(identity.SignChallenge(challenge));
        var publicKey = ScreenViewHostIdentity.DecodeBase64Url(identity.PublicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
        });
        var transcript = Encoding.UTF8.GetBytes($"VolturaAir relay host:v1\n{identity.RouteId}\n{challenge}");

        Assert.True(verifier.VerifyData(
            transcript,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void FileTransferTurnPurposeUsesTheBoundV2Transcript()
    {
        using var identity = RelayRoutingIdentity.CreateEphemeral();
        var signature = ScreenViewHostIdentity.DecodeBase64Url(identity.SignTurnRequest("1800000000000", "n".PadRight(43, 'n'), "file-transfer"));
        var publicKey = ScreenViewHostIdentity.DecodeBase64Url(identity.PublicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
        });

        Assert.True(verifier.VerifyData(
            Encoding.UTF8.GetBytes($"voltura-air-relay-turn-v2\n{identity.RouteId}\n1800000000000\n{"n".PadRight(43, 'n')}\nfile-transfer"),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Throws<ArgumentOutOfRangeException>(() => identity.SignTurnRequest("timestamp", "nonce", "screen"));
    }
}
