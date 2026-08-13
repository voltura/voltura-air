using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebRtcSpike.Host;

internal sealed record EncryptedEnvelope(int V, string Iv, string Ciphertext);

internal static class SignalingCrypto
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static EncryptedEnvelope Encrypt<T>(byte[] key, string room, T value)
    {
        ValidateKeyAndRoom(key, room);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(room));
        }
        byte[] authenticatedCiphertext = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(authenticatedCiphertext, 0);
        tag.CopyTo(authenticatedCiphertext, ciphertext.Length);
        return new EncryptedEnvelope(1, Base64Url(nonce), Base64Url(authenticatedCiphertext));
    }

    internal static T Decrypt<T>(byte[] key, string room, EncryptedEnvelope envelope)
    {
        ValidateKeyAndRoom(key, room);
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.V != 1) throw new InvalidOperationException("The signaling envelope version is unsupported.");
        byte[] nonce = FromBase64Url(envelope.Iv);
        byte[] authenticatedCiphertext = FromBase64Url(envelope.Ciphertext);
        if (nonce.Length != 12 || authenticatedCiphertext.Length < 17) throw new InvalidOperationException("The signaling envelope is malformed.");
        int ciphertextLength = authenticatedCiphertext.Length - 16;
        byte[] plaintext = new byte[ciphertextLength];
        using (var aes = new AesGcm(key, 16))
        {
            try
            {
                aes.Decrypt(
                    nonce,
                    authenticatedCiphertext.AsSpan(0, ciphertextLength),
                    authenticatedCiphertext.AsSpan(ciphertextLength, 16),
                    plaintext,
                    Encoding.UTF8.GetBytes(room));
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException("The signaling key is wrong or the answer was altered.", exception);
            }
        }
        return JsonSerializer.Deserialize<T>(plaintext, JsonOptions)
            ?? throw new InvalidOperationException("The decrypted signaling payload was empty.");
    }

    internal static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        if (string.IsNullOrEmpty(value)) return [];
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        try { return Convert.FromBase64String(padded); }
        catch (FormatException exception) { throw new InvalidOperationException("The signaling envelope is malformed.", exception); }
    }

    private static void ValidateKeyAndRoom(byte[] key, string room)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32) throw new ArgumentException("The signaling key must contain 256 bits.", nameof(key));
        if (string.IsNullOrWhiteSpace(room)) throw new ArgumentException("The signaling room is required.", nameof(room));
    }
}
