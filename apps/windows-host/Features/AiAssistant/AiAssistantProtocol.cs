using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host.Features.AiAssistant;

internal static class AiAssistantProtocol
{
    internal const int MaximumQuestionCharacters = 16 * 1024;
    internal const int MaximumMessageCharacters = 32 * 1024;
    internal const int MaximumMessageChunkCharacters = 4 * 1024;
    internal const int MaximumTranscriptMessages = 32;
    internal const int MaximumTranscriptTurns = MaximumTranscriptMessages / 2;
    internal const int MaximumOperations = 512;

    internal static string OpenTranscript(string clientId, string hostPublicKey, string operationId) =>
        $"VolturaAir ai-assistant:open:v1\n{clientId}\n{hostPublicKey}\n{operationId}";

    internal static string AskTranscript(string clientId, string hostPublicKey, string operationId, string question) =>
        $"VolturaAir ai-assistant:ask:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{Hash(question)}";

    internal static string ResetTranscript(string clientId, string hostPublicKey, string operationId) =>
        $"VolturaAir ai-assistant:reset:v1\n{clientId}\n{hostPublicKey}\n{operationId}";

    internal static IEnumerable<string> ChunkMessage(string text)
    {
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }
        int offset = 0;
        while (offset < text.Length)
        {
            int length = Math.Min(MaximumMessageChunkCharacters, text.Length - offset);
            if (length < text.Length - offset && char.IsHighSurrogate(text[offset + length - 1]) && char.IsLowSurrogate(text[offset + length]))
                length--;
            yield return text.Substring(offset, length);
            offset += length;
        }
    }

    internal static string BoundWithEllipsis(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters) return value;
        int prefixLength = maximumCharacters - 1;
        if (char.IsHighSurrogate(value[prefixLength - 1])) prefixLength--;
        return value[..prefixLength] + "…";
    }

    private static string Hash(string value) =>
        ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
