using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal static class TerminalNegotiation
{
    internal static string StartTranscript(string clientId, string hostPublicKey, string operationId, int columns, int rows) =>
        $"VolturaAir terminal:start:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{columns}\n{rows}";

    internal static string AttachTranscript(string clientId, string hostPublicKey, string operationId, string terminalId, long acknowledgedOffset, int columns, int rows) =>
        $"VolturaAir terminal:attach:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{terminalId}\n{acknowledgedOffset}\n{columns}\n{rows}";

    internal static string OfferTranscript(string clientId, string hostPublicKey, string operationId, string terminalId, int columns, int rows, long acknowledgedOffset, string offerHash) =>
        $"VolturaAir terminal:offer:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{terminalId}\n{columns}\n{rows}\n{acknowledgedOffset}\n{offerHash}";

    internal static string AnswerTranscript(string clientId, string hostPublicKey, string offerOperationId, string answerOperationId, string terminalId, string offerHash, string answerHash) =>
        $"VolturaAir terminal:answer:v1\n{clientId}\n{hostPublicKey}\n{offerOperationId}\n{answerOperationId}\n{terminalId}\n{offerHash}\n{answerHash}";

    internal static string HashSdp(string sdp) =>
        ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));
}
