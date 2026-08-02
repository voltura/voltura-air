using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal static class PairingBootstrapCrypto
{
    private const string HostProofPrefix = "VolturaAir pairing host:v1";
    private const string ClientProofPrefix = "VolturaAir pairing client:v1";

    public static string CreateHostProof(
        string token,
        string clientId,
        string clientNonce,
        string serverNonce,
        string reconnectPublicKey,
        string hostPublicKey,
        string hostFingerprint) =>
        CreateProof(token, HostProofPrefix, clientId, clientNonce, serverNonce, reconnectPublicKey, hostPublicKey, hostFingerprint);

    public static string CreateClientProof(
        string token,
        string clientId,
        string clientNonce,
        string serverNonce,
        string reconnectPublicKey,
        string hostPublicKey,
        string hostFingerprint) =>
        CreateProof(token, ClientProofPrefix, clientId, clientNonce, serverNonce, reconnectPublicKey, hostPublicKey, hostFingerprint);

    public static bool ProofsMatch(string expected, string candidate) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(candidate));

    public static string CreateNonce() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateProof(
        string token,
        string prefix,
        string clientId,
        string clientNonce,
        string serverNonce,
        string reconnectPublicKey,
        string hostPublicKey,
        string hostFingerprint)
    {
        var encodedClientId = Base64Url(Encoding.UTF8.GetBytes(clientId));
        var transcript = string.Join('\n',
            prefix,
            encodedClientId,
            clientNonce,
            serverNonce,
            reconnectPublicKey,
            hostPublicKey,
            hostFingerprint);
        return Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(transcript)));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
