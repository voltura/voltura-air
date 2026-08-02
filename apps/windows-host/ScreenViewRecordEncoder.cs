using System.Buffers.Binary;
using System.Text;

namespace VolturaAir.Host;

internal static class ScreenViewRecordEncoder
{
    public static byte[] EncodeVisual(ScreenViewFrame frame)
    {
        if (frame.IsResynchronization && frame.Patches.Count == 1)
        {
            ScreenViewPatch full = frame.Patches[0];
            var payload = new byte[22 + full.ImageBytes.Length];
            payload[0] = 1;
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, 8), frame.Sequence);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(9, 4), frame.Width);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(13, 4), frame.Height);
            payload[17] = EncodeMime(full.MimeType);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(18, 4), full.ImageBytes.Length);
            full.ImageBytes.CopyTo(payload, 22);
            return payload;
        }

        int length = checked(19 + frame.Patches.Sum(patch => 21 + patch.ImageBytes.Length));
        var result = new byte[length];
        result[0] = 3;
        BinaryPrimitives.WriteInt64BigEndian(result.AsSpan(1, 8), frame.Sequence);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(9, 4), frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(13, 4), frame.Height);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(17, 2), checked((ushort)frame.Patches.Count));
        int offset = 19;
        foreach (ScreenViewPatch patch in frame.Patches)
        {
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset, 4), patch.X);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset + 4, 4), patch.Y);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset + 8, 4), patch.Width);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset + 12, 4), patch.Height);
            result[offset + 16] = EncodeMime(patch.MimeType);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset + 17, 4), patch.ImageBytes.Length);
            patch.ImageBytes.CopyTo(result, offset + 21);
            offset += 21 + patch.ImageBytes.Length;
        }
        return result;
    }

    public static byte[] EncodeCursor(long sequence, ScreenViewCursorUpdate cursor)
    {
        byte[] image = cursor.PngBytes ?? [];
        var payload = new byte[39 + image.Length];
        payload[0] = 4;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, 8), sequence);
        payload[9] = cursor.Visible ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(10, 4), cursor.X);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(14, 4), cursor.Y);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(18, 4), cursor.HotSpotX);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(22, 4), cursor.HotSpotY);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(26, 4), cursor.Width);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(30, 4), cursor.Height);
        payload[34] = image.Length > 0 ? (byte)2 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(35, 4), image.Length);
        image.CopyTo(payload, 39);
        return payload;
    }

    public static byte[] EncodeVideo(long sequence, int width, int height, ScreenViewVideoSegment video)
    {
        byte[] mime = Encoding.ASCII.GetBytes(video.MimeType);
        var payload = new byte[23 + mime.Length + video.Bytes.Length];
        payload[0] = 6;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, 8), sequence);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(9, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(13, 4), height);
        payload[17] = video.Reset ? (byte)1 : (byte)0;
        payload[18] = checked((byte)mime.Length);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(19, 4), video.Bytes.Length);
        mime.CopyTo(payload, 23);
        video.Bytes.CopyTo(payload, 23 + mime.Length);
        return payload;
    }

    public static byte[] EncodeStatus(string code, string message)
    {
        byte[] codeBytes = Encoding.UTF8.GetBytes(code);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        var payload = new byte[4 + codeBytes.Length + messageBytes.Length];
        payload[0] = 5;
        payload[1] = checked((byte)codeBytes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)messageBytes.Length));
        codeBytes.CopyTo(payload, 4);
        messageBytes.CopyTo(payload, 4 + codeBytes.Length);
        return payload;
    }

    private static byte EncodeMime(string mimeType) => mimeType == "image/png" ? (byte)2 : (byte)1;
}
