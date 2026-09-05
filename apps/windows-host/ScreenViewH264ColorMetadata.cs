namespace VolturaAir.Host;

// Some hardware MFTs omit color_description despite accepting the media-type attributes.
// Specify the color of the pixels we actually encode, preserving every other SPS field.
internal static class ScreenViewH264ColorMetadata
{
    public static byte[] Apply(byte[] annexB)
    {
        using var output = new MemoryStream(annexB.Length + 16);
        int position = 0;
        while (position < annexB.Length)
        {
            int prefix = PrefixLength(annexB, position);
            if (prefix == 0) throw Invalid();
            int start = position + prefix;
            int end = start;
            while (end < annexB.Length && PrefixLength(annexB, end) == 0) end++;
            output.Write(annexB.AsSpan(position, prefix));
            if (start < end && (annexB[start] & 31) == 7)
            {
                output.WriteByte(annexB[start]);
                output.Write(RewriteSequence(annexB.AsSpan(start + 1, end - start - 1)));
            }
            else output.Write(annexB.AsSpan(start, end - start));
            position = end;
        }
        return output.ToArray();
    }

    private static byte[] RewriteSequence(ReadOnlySpan<byte> escaped)
    {
        if (escaped.Length is 0 or > 65_536) throw Invalid();
        var bits = new List<bool>(escaped.Length * 8);
        int zeros = 0;
        foreach (byte value in escaped)
        {
            if (zeros == 2 && value == 3) { zeros = 0; continue; }
            for (int bit = 7; bit >= 0; bit--) bits.Add((value & (1 << bit)) != 0);
            zeros = value == 0 ? zeros + 1 : 0;
        }
        var reader = new BitReader(bits);
        if (reader.Read(8) != 66) throw Invalid(); // Screen View's negotiated Baseline contract.
        reader.Read(16);
        reader.Unsigned(); // seq_parameter_set_id
        reader.Unsigned(); // log2_max_frame_num_minus4
        uint order = reader.Unsigned();
        if (order == 0) reader.Unsigned();
        else if (order == 1)
        {
            reader.Read(1);
            reader.Unsigned();
            reader.Unsigned();
            uint count = reader.Unsigned();
            if (count > 255) throw Invalid();
            for (int i = 0; i < count; i++) reader.Unsigned();
        }
        else if (order != 2) throw Invalid();
        reader.Unsigned();
        reader.Read(1);
        reader.Unsigned();
        reader.Unsigned();
        if (reader.Read(1) == 0) reader.Read(1);
        reader.Read(1);
        if (reader.Read(1) != 0)
            for (int i = 0; i < 4; i++) reader.Unsigned();
        int vuiPosition = reader.Position;
        bool hasVui = reader.Read(1) != 0;
        int replaceStart;
        int replaceEnd;
        var replacement = new List<bool>();
        if (hasVui)
        {
            if (reader.Read(1) != 0 && reader.Read(8) == 255) reader.Read(32);
            if (reader.Read(1) != 0) reader.Read(1);
            replaceStart = reader.Position;
            if (reader.Read(1) != 0)
            {
                reader.Read(4);
                if (reader.Read(1) != 0) reader.Read(24);
            }
            replaceEnd = reader.Position;
        }
        else
        {
            replaceStart = vuiPosition;
            replaceEnd = reader.Position;
            replacement.AddRange([true, false, false]); // VUI, no aspect ratio/overscan.
        }
        Append(replacement, 1, 1); // video_signal_type_present_flag
        Append(replacement, 5, 3); // unspecified video format
        Append(replacement, 0, 1); // limited range
        Append(replacement, 1, 1); // colour_description_present_flag
        Append(replacement, 1, 8); // BT.709 primaries
        Append(replacement, 13, 8); // IEC 61966-2-1 (sRGB), matching GPU output
        Append(replacement, 1, 8); // BT.709 matrix
        if (!hasVui) Append(replacement, 0, 6); // Remaining optional VUI fields absent.
        bits.RemoveRange(replaceStart, replaceEnd - replaceStart);
        bits.InsertRange(replaceStart, replacement);
        // Keep rbsp_stop_one_bit, recalculate only its byte alignment.
        while (bits.Count > 0 && !bits[^1]) bits.RemoveAt(bits.Count - 1);
        while (bits.Count % 8 != 0) bits.Add(false);
        using var result = new MemoryStream();
        zeros = 0;
        for (int i = 0; i < bits.Count; i += 8)
        {
            byte value = 0;
            for (int j = 0; j < 8; j++) value = (byte)((value << 1) | (bits[i + j] ? 1 : 0));
            if (zeros == 2 && value <= 3) { result.WriteByte(3); zeros = 0; }
            result.WriteByte(value);
            zeros = value == 0 ? zeros + 1 : 0;
        }
        return result.ToArray();
    }

    private static void Append(List<bool> bits, uint value, int length)
    {
        for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    private static int PrefixLength(ReadOnlySpan<byte> data, int i)
    {
        if (i + 2 >= data.Length || data[i] != 0 || data[i + 1] != 0) return 0;
        if (data[i + 2] == 1) return 3;
        return i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1 ? 4 : 0;
    }

    private static ScreenViewCaptureException Invalid() =>
        new("encoder-failed", "The screen encoder returned invalid Baseline H.264 color metadata.");

    private sealed class BitReader(List<bool> bits)
    {
        public int Position { get; private set; }
        public uint Read(int count)
        {
            if (count < 0 || count > 32 || count > bits.Count - Position) throw Invalid();
            uint value = 0;
            for (int i = 0; i < count; i++) value = (value << 1) | (bits[Position++] ? 1u : 0u);
            return value;
        }
        public uint Unsigned()
        {
            int count = 0;
            while (Read(1) == 0) if (++count > 30) throw Invalid();
            return ((1u << count) - 1) + Read(count);
        }
    }
}
