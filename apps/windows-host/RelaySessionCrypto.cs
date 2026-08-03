using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal sealed class RelaySessionCrypto : IDisposable
{
    private const byte Version = 1;
    private const byte HostToDevice = 1;
    private const byte DeviceToHost = 2;
    private const int HeaderLength = 10;
    private const int TagLength = 16;
    internal const int FrameOverhead = HeaderLength + TagLength;
    private readonly byte[] _sendKey;
    private readonly byte[] _receiveKey;
    private readonly byte[] _sendNoncePrefix;
    private readonly byte[] _receiveNoncePrefix;
    private readonly byte[] _transcriptHash;
    private ulong _sendCounter;
    private ulong _receiveCounter;

    private RelaySessionCrypto(byte[] secret, byte[] transcriptHash)
    {
        _transcriptHash = transcriptHash;
        var material = Expand(HMACSHA256.HashData(transcriptHash, secret), "voltura-air-relay-session-keys-v1", 72);
        _sendKey = material[..32];
        _receiveKey = material[32..64];
        _sendNoncePrefix = material[64..68];
        _receiveNoncePrefix = material[68..72];
    }

    public static RelaySessionCrypto CreateHost(ECDiffieHellman hostKey, string clientPublicKey, ReadOnlySpan<byte> transcript)
    {
        using var client = CreatePublicKey(clientPublicKey);
        var secret = hostKey.DeriveRawSecretAgreement(client.PublicKey);
        try { return new RelaySessionCrypto(secret, SHA256.HashData(transcript)); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    internal static RelaySessionCrypto CreateHostFromSharedSecretForConformance(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> transcript) =>
        new(secret.ToArray(), SHA256.HashData(transcript));

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var counter = checked(++_sendCounter);
        var result = new byte[HeaderLength + plaintext.Length + TagLength];
        result[0] = Version;
        result[1] = HostToDevice;
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(2, 8), counter);
        var nonce = CreateNonce(_sendNoncePrefix, counter);
        using var aes = new AesGcm(_sendKey, TagLength);
        aes.Encrypt(nonce, plaintext, result.AsSpan(HeaderLength, plaintext.Length), result.AsSpan(HeaderLength + plaintext.Length, TagLength), CreateAad(result.AsSpan(0, HeaderLength)));
        return result;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderLength + TagLength || frame[0] != Version || frame[1] != DeviceToHost)
            throw new CryptographicException("Invalid encrypted relay frame.");
        var counter = BinaryPrimitives.ReadUInt64BigEndian(frame.Slice(2, 8));
        if (counter != checked(_receiveCounter + 1)) throw new CryptographicException("Relay frame counter was replayed or skipped.");
        var plaintext = new byte[frame.Length - HeaderLength - TagLength];
        var nonce = CreateNonce(_receiveNoncePrefix, counter);
        using var aes = new AesGcm(_receiveKey, TagLength);
        aes.Decrypt(nonce, frame.Slice(HeaderLength, plaintext.Length), frame[^TagLength..], plaintext, CreateAad(frame[..HeaderLength]));
        _receiveCounter = counter;
        return plaintext;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_sendKey);
        CryptographicOperations.ZeroMemory(_receiveKey);
    }

    internal static string ExportPublicKey(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(false);
        Span<byte> encoded = stackalloc byte[65];
        encoded[0] = 4;
        parameters.Q.X!.CopyTo(encoded[1..33]);
        parameters.Q.Y!.CopyTo(encoded[33..]);
        return ScreenViewHostIdentity.Base64Url(encoded);
    }

    internal static byte[] CreateTranscript(
        string routeId,
        string clientId,
        string hostIdentity,
        string hostEphemeral,
        string clientEphemeral,
        string nonce) => Encoding.UTF8.GetBytes(string.Join('\n',
            "voltura-air-relay-session-v1", routeId, clientId, hostIdentity, hostEphemeral, clientEphemeral, nonce));

    private static ECDiffieHellman CreatePublicKey(string encoded)
    {
        var bytes = ScreenViewHostIdentity.DecodeBase64Url(encoded);
        if (bytes.Length != 65 || bytes[0] != 4) throw new CryptographicException("Invalid relay session key.");
        var key = ECDiffieHellman.Create();
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = bytes[1..33], Y = bytes[33..65] }
        });
        return key;
    }

    private byte[] CreateAad(ReadOnlySpan<byte> header)
    {
        var aad = new byte[_transcriptHash.Length + header.Length];
        _transcriptHash.CopyTo(aad, 0);
        header.CopyTo(aad.AsSpan(_transcriptHash.Length));
        return aad;
    }

    private static byte[] CreateNonce(byte[] prefix, ulong counter)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    private static byte[] Expand(byte[] pseudoRandomKey, string info, int length)
    {
        var infoBytes = Encoding.UTF8.GetBytes(info);
        var output = new byte[length];
        var previous = Array.Empty<byte>();
        var offset = 0;
        for (byte block = 1; offset < length; block++)
        {
            var input = new byte[previous.Length + infoBytes.Length + 1];
            previous.CopyTo(input, 0);
            infoBytes.CopyTo(input, previous.Length);
            input[^1] = block;
            previous = HMACSHA256.HashData(pseudoRandomKey, input);
            var count = Math.Min(previous.Length, length - offset);
            previous.AsSpan(0, count).CopyTo(output.AsSpan(offset));
            offset += count;
        }
        return output;
    }
}
