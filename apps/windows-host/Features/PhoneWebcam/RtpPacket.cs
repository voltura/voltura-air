using System.Buffers.Binary;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal readonly record struct RtpPacketHeader(ushort Sequence, uint Timestamp, bool Marker, int PayloadOffset, int PayloadLength);

internal static class RtpPacket
{
    private const int MaximumLatePackets = 100;

    internal static int SequenceOffset(ushort sequence, ushort? expected) =>
        expected.HasValue ? unchecked((short)(sequence - expected.Value)) : 0;

    internal static bool IsLate(int sequenceOffset) => sequenceOffset is >= -MaximumLatePackets and < 0;

    internal static bool TryRead(ReadOnlySpan<byte> packet, out RtpPacketHeader header)
    {
        header = default;
        if (packet.Length < 12 || (packet[0] >> 6) != 2) return false;
        int offset = 12 + (packet[0] & 0x0f) * 4;
        if (offset > packet.Length) return false;
        if ((packet[0] & 0x10) != 0)
        {
            if (offset + 4 > packet.Length) return false;
            int extensionBytes = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 2, 2)) * 4;
            offset += 4 + extensionBytes;
            if (offset > packet.Length) return false;
        }
        int end = packet.Length;
        bool hasPadding = (packet[0] & 0x20) != 0;
        if (hasPadding)
        {
            int padding = packet[^1];
            if (padding == 0 || padding > end - offset) return false;
            end -= padding;
        }
        if (offset == end && !hasPadding) return false;
        header = new RtpPacketHeader(
            BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4)),
            (packet[1] & 0x80) != 0,
            offset,
            end - offset);
        return true;
    }
}
