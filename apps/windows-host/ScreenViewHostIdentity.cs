using System.Security.Cryptography;

namespace VolturaAir.Host;

internal sealed class ScreenViewHostIdentity : IDisposable
{
    private const string PersistedKeyName = "VolturaAir.ScreenView.HostIdentity.v1";
    private readonly ECDsa _signingKey;

    private ScreenViewHostIdentity(ECDsa signingKey)
    {
        _signingKey = signingKey;
        PublicKey = EncodePublicKey(signingKey.ExportParameters(includePrivateParameters: false));
        Fingerprint = Base64Url(SHA256.HashData(DecodeBase64Url(PublicKey)).AsSpan(0, 16));
    }

    public string PublicKey { get; }
    public string Fingerprint { get; }

    public static ScreenViewHostIdentity OpenCurrentUser()
    {
        var existing = CngKey.Exists(PersistedKeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey);
#pragma warning disable CA2000 // ECDsaCng owns the CngKey passed to its constructor.
        var key = existing
            ? CngKey.Open(PersistedKeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey)
            : CngKey.Create(
                CngAlgorithm.ECDsaP256,
                PersistedKeyName,
                new CngKeyCreationParameters
                {
                    ExportPolicy = CngExportPolicies.None,
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
                });
        return new ScreenViewHostIdentity(new ECDsaCng(key));
#pragma warning restore CA2000
    }

    internal static ScreenViewHostIdentity CreateEphemeral() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public string Sign(ReadOnlySpan<byte> data) => Base64Url(_signingKey.SignData(
        data,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public void Dispose() => _signingKey.Dispose();

    internal static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    internal static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string EncodePublicKey(ECParameters parameters)
    {
        if (parameters.Q.X is not { Length: 32 } x || parameters.Q.Y is not { Length: 32 } y)
        {
            throw new CryptographicException("The screen-view host identity is not P-256.");
        }

        Span<byte> encoded = stackalloc byte[65];
        encoded[0] = 0x04;
        x.CopyTo(encoded[1..33]);
        y.CopyTo(encoded[33..]);
        return Base64Url(encoded);
    }
}
