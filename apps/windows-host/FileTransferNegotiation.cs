using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal static class FileTransferNegotiation
{
    internal static async Task<RelayTurnConfiguration?> RunAsync(
        FileTransferSession transfer,
        bool relayMode,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        IFileTransferWebRtcPeerFactory peerFactory,
        PairingManager pairingManager,
        WebSocketTransport transport,
        Func<CancellationToken, Task> publishConnecting,
        CancellationToken cancellationToken,
        TimeSpan? signalingLifetime = null)
    {
        using var signaling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        signaling.CancelAfter(signalingLifetime ?? FileTransferProtocol.SignalingLifetime);
        try
        {
            RelayTurnConfiguration? relay = relayMode ? await getRelayTurnConfiguration(signaling.Token).ConfigureAwait(false) : null;
            if (relayMode && relay is null) throw new FileTransferWebRtcException("Relay credentials were unavailable.");
            IFileTransferWebRtcPeer peer = peerFactory.Create(relay is null ? null : new FileTransferPeerConfiguration(relay.HostIceServerUris, RelayOnly: true));
            bool admitted;
            lock (transfer.Gate)
            {
                admitted = Volatile.Read(ref transfer.DisposeStarted) == 0;
                if (admitted) transfer.Peer = peer;
            }
            if (!admitted)
            {
                await peer.DisposeAsync().ConfigureAwait(false);
                throw new OperationCanceledException(cancellationToken);
            }
            string offerSdp = await peer.CreateOfferAsync(signaling.Token).ConfigureAwait(false);
            string offerHash = HashSdp(offerSdp);
            lock (transfer.Gate) transfer.OfferHash = offerHash;
            string transcript = OfferTranscript(transfer.ClientId, pairingManager.HostIdentity.PublicKey, transfer.OperationId, transfer.Id, transfer.Direction, transfer.FileName, transfer.DeclaredSize, offerHash);
            await transport.SendAsync(transfer.Socket, new
            {
                type = "file.transfer.offer",
                transferId = transfer.Id,
                direction = transfer.Direction,
                fileName = transfer.FileName,
                declaredSize = transfer.DeclaredSize,
                offerSdp,
                hostSignature = pairingManager.HostIdentity.Sign(Encoding.UTF8.GetBytes(transcript)),
                iceServers = relay?.IceServers,
                turnExpiresAt = relay?.ExpiresAt,
                relayUsageBytes = relay?.UsageBytes,
                relayUsageCheckedAt = relay?.CheckedAt
            }, signaling.Token).ConfigureAwait(false);
            await publishConnecting(signaling.Token).ConfigureAwait(false);
            await transfer.AnswerApplied.Task.WaitAsync(signaling.Token).ConfigureAwait(false);
            await peer.Opened.WaitAsync(signaling.Token).ConfigureAwait(false);
            return relay;
        }
        catch (OperationCanceledException) when (signaling.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            lock (transfer.Gate)
            {
                transfer.FailureCode ??= "offer-expired";
                transfer.FailureMessage ??= "The file-transfer offer expired.";
            }
            throw;
        }
    }

    internal static string StartTranscript(string clientId, string hostPublicKey, string operationId, string direction, string sessionId, string panel, string revision, string entryId, string fileName, long? declaredSize) =>
        $"VolturaAir file-transfer:start:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{direction}\n{sessionId}\n{panel}\n{revision}\n{entryId}\n{fileName}\n{declaredSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}";
    internal static string OfferTranscript(string clientId, string hostPublicKey, string operationId, string transferId, string direction, string fileName, long declaredSize, string offerHash) =>
        $"VolturaAir file-transfer:offer:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{transferId}\n{direction}\n{fileName}\n{declaredSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{offerHash}";
    internal static string AnswerTranscript(string clientId, string hostPublicKey, string operationId, string transferId, string direction, string fileName, long declaredSize, string offerHash, string answerHash) =>
        $"VolturaAir file-transfer:answer:v1\n{clientId}\n{hostPublicKey}\n{operationId}\n{transferId}\n{direction}\n{fileName}\n{declaredSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{offerHash}\n{answerHash}";
    internal static string HashSdp(string sdp) => ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));
}
