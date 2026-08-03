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
}
