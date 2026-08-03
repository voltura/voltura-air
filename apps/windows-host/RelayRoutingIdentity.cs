using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal sealed class RelayRoutingIdentity : IDisposable
{
    private const string PersistedKeyName = "VolturaAir.Relay.RoutingIdentity.v1";
    private const string TranscriptPrefix = "VolturaAir relay host:v1";
    private readonly ECDsa _key;

    private RelayRoutingIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = EncodePublicKey(key.ExportParameters(false));
        RouteId = ScreenViewHostIdentity.Base64Url(
            SHA256.HashData(ScreenViewHostIdentity.DecodeBase64Url(PublicKey)).AsSpan(0, 16));
    }

    public string PublicKey { get; }
    public string RouteId { get; }

    public static RelayRoutingIdentity OpenCurrentUser()
    {
        var exists = CngKey.Exists(PersistedKeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey);
#pragma warning disable CA2000
        var key = exists
            ? CngKey.Open(PersistedKeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey)
            : CngKey.Create(CngAlgorithm.ECDsaP256, PersistedKeyName, new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
            });
        return new RelayRoutingIdentity(new ECDsaCng(key));
#pragma warning restore CA2000
    }

    internal static RelayRoutingIdentity CreateEphemeral() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public string SignChallenge(string challenge)
    {
        var transcript = Encoding.UTF8.GetBytes($"{TranscriptPrefix}\n{RouteId}\n{challenge}");
        return ScreenViewHostIdentity.Base64Url(_key.SignData(
            transcript,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public string SignTurnRequest(string timestamp, string nonce)
    {
        var transcript = Encoding.UTF8.GetBytes($"voltura-air-relay-turn-v1\n{RouteId}\n{timestamp}\n{nonce}");
        return ScreenViewHostIdentity.Base64Url(_key.SignData(
            transcript,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public void Dispose() => _key.Dispose();

    private static string EncodePublicKey(ECParameters parameters)
    {
        if (parameters.Q.X is not { Length: 32 } x || parameters.Q.Y is not { Length: 32 } y)
        {
            throw new CryptographicException("The relay routing identity is not P-256.");
        }

        Span<byte> encoded = stackalloc byte[65];
        encoded[0] = 4;
        x.CopyTo(encoded[1..33]);
        y.CopyTo(encoded[33..]);
        return ScreenViewHostIdentity.Base64Url(encoded);
    }
}
