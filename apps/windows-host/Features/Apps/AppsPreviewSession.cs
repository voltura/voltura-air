using System.Net.WebSockets;

namespace VolturaAir.Host.Features.Apps;

internal sealed class AppsPreviewSession(
    string id,
    string clientId,
    WebSocket socket,
    string offerOperationId,
    CancellationTokenSource cancellation)
{
    internal string Id { get; } = id;
    internal string ClientId { get; } = clientId;
    internal WebSocket Socket { get; } = socket;
    internal string OfferOperationId { get; } = offerOperationId;
    internal CancellationTokenSource Cancellation { get; } = cancellation;
    internal CancellationToken Token => Cancellation.Token;
    internal TaskCompletionSource AnswerApplied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal IFileTransferWebRtcPeer? Peer { get; set; }
    internal RelayTurnConfiguration? Relay { get; set; }
    internal string? OfferHash { get; set; }
    internal Task? RunTask { get; set; }
    internal long RelayPayloadBytes { get; set; }
    internal bool IncludesVolturaAir { get; set; }
}
