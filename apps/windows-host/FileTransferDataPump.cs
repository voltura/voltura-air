namespace VolturaAir.Host;

internal static class FileTransferDataPump
{
    internal static async Task SendAsync(
        FileTransferDownloadSource source,
        long declaredSize,
        IFileTransferWebRtcPeer peer,
        Action progress,
        Func<long, bool, CancellationToken, Task> publishProgress,
        CancellationToken cancellationToken)
    {
        long sent = 0;
        long acknowledged = 0;
        var buffer = new byte[FileTransferProtocol.MaximumPayloadBytes];
        while (acknowledged < declaredSize)
        {
            while (sent < declaredSize && sent - acknowledged < FileTransferProtocol.MaximumUnacknowledgedBytes)
            {
                int count = (int)Math.Min(buffer.Length, Math.Min(declaredSize - sent, FileTransferProtocol.MaximumUnacknowledgedBytes - (sent - acknowledged)));
                int read = await source.Stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (read <= 0) throw new IOException("The PC file ended before its declared size.");
                await SendRecordAsync(peer, FileTransferProtocol.CreateData(sent, buffer.AsSpan(0, read)), cancellationToken).ConfigureAwait(false);
                sent += read;
            }
            byte[] bytes = await peer.Messages.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!FileTransferProtocol.TryParse(bytes, out var record) || record.Kind != FileTransferRecordKind.Acknowledgement ||
                record.Offset <= acknowledged || record.Offset > sent) throw new IOException("An invalid download acknowledgement was received.");
            acknowledged = record.Offset;
            progress();
            await publishProgress(acknowledged, acknowledged == declaredSize, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task ReceiveAsync(
        Stream destination,
        long declaredSize,
        IFileTransferWebRtcPeer peer,
        Action<long> committed,
        Action progress,
        Func<long, bool, CancellationToken, Task> publishProgress,
        CancellationToken cancellationToken)
    {
        long offset = 0;
        if (declaredSize == 0) await SendRecordAsync(peer, FileTransferProtocol.CreateAcknowledgement(0), cancellationToken).ConfigureAwait(false);
        while (offset < declaredSize)
        {
            byte[] bytes = await peer.Messages.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!FileTransferProtocol.TryParse(bytes, out var record) || record.Kind != FileTransferRecordKind.Data ||
                record.Offset != offset || record.Payload.Length > declaredSize - offset)
                throw new IOException("An invalid upload record was received.");
            await destination.WriteAsync(record.Payload, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            offset += record.Payload.Length;
            committed(offset);
            progress();
            await SendRecordAsync(peer, FileTransferProtocol.CreateAcknowledgement(offset), cancellationToken).ConfigureAwait(false);
            await publishProgress(offset, offset == declaredSize, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SendRecordAsync(IFileTransferWebRtcPeer peer, byte[] record, CancellationToken cancellationToken)
    {
        while (!peer.TrySend(record)) await Task.Delay(5, cancellationToken).ConfigureAwait(false);
    }
}
