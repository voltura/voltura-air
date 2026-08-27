using System.Buffers.Binary;

namespace VolturaAir.Host;

internal enum TerminalRecordKind : byte
{
    Input = 1,
    Output = 2,
    Acknowledgement = 3,
    Resize = 4
}

internal readonly record struct TerminalRecord(
    TerminalRecordKind Kind,
    long Offset,
    ushort Columns,
    ushort Rows,
    ReadOnlyMemory<byte> Payload);

internal static class TerminalProtocol
{
    internal const byte Version = 1;
    internal const string DataChannelLabel = "voltura-terminal";
    internal const int MaximumPayloadBytes = 16 * 1024;
    internal const int MaximumQueuedInputBytes = 256 * 1024;
    internal const int MaximumUnacknowledgedOutputBytes = 1024 * 1024;
    internal const int MaximumBufferedAmountBytes = 1024 * 1024;
    internal const int MaximumPasteBytes = 64 * 1024;
    internal const int OffsetHeaderBytes = 9;
    internal const int ResizeRecordBytes = 5;
    internal const int MaximumRecordBytes = OffsetHeaderBytes + MaximumPayloadBytes;
    internal const ushort MinimumColumns = 10;
    internal const ushort MaximumColumns = 500;
    internal const ushort MinimumRows = 5;
    internal const ushort MaximumRows = 300;
    internal static readonly TimeSpan SignalingLifetime = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan OutputEofExitGrace = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ReconnectLifetime = TimeSpan.FromMinutes(15);

    internal static byte[] CreateInput(ReadOnlySpan<byte> payload) => CreatePayload(TerminalRecordKind.Input, 0, payload);

    internal static byte[] CreateOutput(long offset, ReadOnlySpan<byte> payload) =>
        CreatePayload(TerminalRecordKind.Output, offset, payload);

    internal static byte[] CreateAcknowledgement(long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var result = new byte[OffsetHeaderBytes];
        result[0] = Encode(TerminalRecordKind.Acknowledgement);
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(1), checked((ulong)offset));
        return result;
    }

    internal static byte[] CreateResize(ushort columns, ushort rows)
    {
        ValidateDimensions(columns, rows);
        var result = new byte[ResizeRecordBytes];
        result[0] = Encode(TerminalRecordKind.Resize);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), columns);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(3, 2), rows);
        return result;
    }

    internal static bool TryParse(ReadOnlyMemory<byte> bytes, out TerminalRecord record)
    {
        record = default;
        if (bytes.Length < 1 || bytes.Length > MaximumRecordBytes || bytes.Span[0] >> 4 != Version) return false;
        var kind = (TerminalRecordKind)(bytes.Span[0] & 0x0f);
        if (!Enum.IsDefined(kind)) return false;
        if (kind == TerminalRecordKind.Resize)
        {
            if (bytes.Length != ResizeRecordBytes) return false;
            ushort columns = BinaryPrimitives.ReadUInt16BigEndian(bytes.Span.Slice(1, 2));
            ushort rows = BinaryPrimitives.ReadUInt16BigEndian(bytes.Span.Slice(3, 2));
            if (!AreValidDimensions(columns, rows)) return false;
            record = new(kind, 0, columns, rows, ReadOnlyMemory<byte>.Empty);
            return true;
        }

        if (bytes.Length < OffsetHeaderBytes) return false;
        ulong rawOffset = BinaryPrimitives.ReadUInt64BigEndian(bytes.Span.Slice(1, 8));
        if (rawOffset > long.MaxValue) return false;
        ReadOnlyMemory<byte> payload = bytes[OffsetHeaderBytes..];
        if (kind is TerminalRecordKind.Input or TerminalRecordKind.Output)
        {
            if (payload.Length is < 1 or > MaximumPayloadBytes) return false;
            if (kind == TerminalRecordKind.Input && rawOffset != 0) return false;
        }
        else if (kind == TerminalRecordKind.Acknowledgement && payload.Length != 0) return false;
        record = new(kind, (long)rawOffset, 0, 0, payload);
        return true;
    }

    internal static bool AreValidDimensions(int columns, int rows) =>
        columns is >= MinimumColumns and <= MaximumColumns && rows is >= MinimumRows and <= MaximumRows;

    internal static void ValidateDimensions(ushort columns, ushort rows)
    {
        if (!AreValidDimensions(columns, rows)) throw new ArgumentOutOfRangeException(nameof(columns));
    }

    private static byte[] CreatePayload(TerminalRecordKind kind, long offset, ReadOnlySpan<byte> payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (payload.Length is < 1 or > MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        var result = new byte[OffsetHeaderBytes + payload.Length];
        result[0] = Encode(kind);
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(1, 8), checked((ulong)offset));
        payload.CopyTo(result.AsSpan(OffsetHeaderBytes));
        return result;
    }

    private static byte Encode(TerminalRecordKind kind) => (byte)((Version << 4) | (byte)kind);
}
