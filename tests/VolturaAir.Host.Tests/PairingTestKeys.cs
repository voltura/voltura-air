using System.Security.Cryptography;
using System.Text;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PairingTestKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string PublicKeyForFreshPairing { get; } = CreatePublicKeyForFreshPairing();

    public string PublicKey
    {
        get
        {
            var parameters = _key.ExportParameters(false);
            return Base64Url([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
        }
    }

    public string SignReconnectChallenge(string clientId, string challenge) =>
        Base64Url(_key.SignData(
            Encoding.UTF8.GetBytes(PairingManager.GetReconnectSigningPayload(clientId, challenge)),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public void Dispose() => _key.Dispose();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CreatePublicKeyForFreshPairing()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        return Base64Url([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
    }
}
