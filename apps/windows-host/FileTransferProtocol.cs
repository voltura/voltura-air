using System.Buffers.Binary;

namespace VolturaAir.Host;

internal enum FileTransferRecordKind : byte
{
    Data = 1,
    Acknowledgement = 2
}

internal readonly record struct FileTransferRecord(FileTransferRecordKind Kind, long Offset, ReadOnlyMemory<byte> Payload);

internal static class FileTransferProtocol
{
    internal const long MaximumSafeFileSize = 9_007_199_254_740_991;
    internal const int MaximumPayloadBytes = 64 * 1024;
    internal const int MaximumUnacknowledgedBytes = 1024 * 1024;
    internal const int HeaderBytes = 9;
    internal const int MaximumRecordBytes = HeaderBytes + MaximumPayloadBytes;
    internal const byte Version = 1;
    internal const string DataChannelLabel = "voltura-file-transfer";
    internal static readonly TimeSpan SignalingLifetime = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    internal static byte[] CreateData(long offset, ReadOnlySpan<byte> payload)
    {
        if (offset < 0 || payload.Length is < 1 or > MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        var result = new byte[HeaderBytes + payload.Length];
        result[0] = EncodeKind(FileTransferRecordKind.Data);
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(1, 8), checked((ulong)offset));
        payload.CopyTo(result.AsSpan(HeaderBytes));
        return result;
    }

    internal static byte[] CreateAcknowledgement(long committedOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(committedOffset);
        var result = new byte[HeaderBytes];
        result[0] = EncodeKind(FileTransferRecordKind.Acknowledgement);
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(1, 8), checked((ulong)committedOffset));
        return result;
    }

    internal static bool TryParse(ReadOnlyMemory<byte> bytes, out FileTransferRecord record)
    {
        record = default;
        if (bytes.Length < HeaderBytes || bytes.Length > MaximumRecordBytes) return false;
        var span = bytes.Span;
        if ((span[0] >> 4) != Version || !Enum.IsDefined((FileTransferRecordKind)(span[0] & 0x0f))) return false;
        ulong unsignedOffset = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(1, 8));
        if (unsignedOffset > long.MaxValue) return false;
        var kind = (FileTransferRecordKind)(span[0] & 0x0f);
        var payload = bytes[HeaderBytes..];
        if (kind == FileTransferRecordKind.Data && payload.Length == 0 ||
            kind == FileTransferRecordKind.Acknowledgement && payload.Length != 0) return false;
        record = new(kind, (long)unsignedOffset, payload);
        return true;
    }

    private static byte EncodeKind(FileTransferRecordKind kind) => (byte)((Version << 4) | (byte)kind);
}
