using System.Buffers.Binary;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal static class RtpPacket
{
    internal static bool TryRead(
        ReadOnlySpan<byte> packet,
        out ushort sequence,
        out uint timestamp,
        out bool marker,
        out ReadOnlySpan<byte> payload)
    {
        sequence = 0;
        timestamp = 0;
        marker = false;
        payload = default;
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
        if ((packet[0] & 0x20) != 0)
        {
            int padding = packet[^1];
            if (padding == 0 || padding > end - offset) return false;
            end -= padding;
        }
        if (offset >= end) return false;
        sequence = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        timestamp = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4));
        marker = (packet[1] & 0x80) != 0;
        payload = packet[offset..end];
        return true;
    }
}
