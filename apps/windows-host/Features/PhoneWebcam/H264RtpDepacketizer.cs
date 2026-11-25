using System.Buffers.Binary;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal readonly record struct H264DepacketizeResult(
    byte[]? AccessUnit, bool RequestKeyFrame, uint? RtpTimestamp = null, bool Discontinuity = false);

internal sealed class H264RtpDepacketizer : IDisposable
{
    internal const int MaximumAccessUnitBytes = 8 * 1024 * 1024;
    private readonly MemoryStream _accessUnit = new();
    private ushort? _expectedSequence;
    private uint? _timestamp;
    private bool _fragmentOpen;
    private bool _discardUntilMarker;
    private uint? _discardTimestamp;
    private bool _decoderDiscontinuity;

    internal H264DepacketizeResult Push(ReadOnlySpan<byte> packet)
    {
        if (!RtpPacket.TryRead(packet, out RtpPacketHeader header))
        {
            _expectedSequence = null;
            return Reject();
        }

        int offset = RtpPacket.SequenceOffset(header.Sequence, _expectedSequence);
        if (RtpPacket.IsLate(offset)) return default;
        _expectedSequence = unchecked((ushort)(header.Sequence + 1));
        uint timestamp = header.Timestamp;
        bool marker = header.Marker;
        ReadOnlySpan<byte> payload = packet.Slice(header.PayloadOffset, header.PayloadLength);
        if (payload.IsEmpty) return offset == 0 ? default : Reject();
        if (offset != 0) return Reject(timestamp, marker);

        // After a gap, the first packet we see may belong to an already-started frame.
        // A subsequent timestamp change with continuous sequence numbers is a new boundary.
        if (_discardUntilMarker && (_discardTimestamp is null || _discardTimestamp == timestamp))
        {
            if (marker) _discardUntilMarker = false;
            return new H264DepacketizeResult(null, true);
        }
        _discardUntilMarker = false;
        bool loss = _timestamp.HasValue && timestamp != _timestamp.Value;
        if (loss)
        {
            ResetAccessUnit();
            _decoderDiscontinuity = true;
        }
        _timestamp = timestamp;

        if (!AppendPayload(payload)) return Reject(timestamp, marker);
        if (!marker) return new H264DepacketizeResult(null, loss);
        if (_fragmentOpen || _accessUnit.Length == 0) return Reject(timestamp, marker);

        byte[] completed = _accessUnit.ToArray();
        ResetAccessUnit();
        var result = new H264DepacketizeResult(completed, loss, timestamp, _decoderDiscontinuity);
        _decoderDiscontinuity = false;
        return result;
    }

    private bool AppendPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return false;
        if ((payload[0] & 0x80) != 0) return false;
        int type = payload[0] & 0x1f;
        if (type is >= 1 and <= 23)
        {
            if (_fragmentOpen) return false;
            return AppendNal(payload);
        }
        if (type == 24)
        {
            if (_fragmentOpen) return false;
            int offset = 1;
            while (offset < payload.Length)
            {
                if (offset + 2 > payload.Length) return false;
                int length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2));
                offset += 2;
                ReadOnlySpan<byte> nal = offset + length <= payload.Length ? payload.Slice(offset, length) : [];
                int nestedType = nal.IsEmpty ? 0 : nal[0] & 0x1f;
                if (length == 0 || offset + length > payload.Length || (nal[0] & 0x80) != 0 ||
                    nestedType is < 1 or > 23 || !AppendNal(nal)) return false;
                offset += length;
            }
            return offset == payload.Length;
        }
        if (type != 28 || payload.Length < 3) return false;

        byte fragmentHeader = payload[1];
        bool start = (fragmentHeader & 0x80) != 0;
        bool end = (fragmentHeader & 0x40) != 0;
        int fragmentType = fragmentHeader & 0x1f;
        if ((fragmentHeader & 0x20) != 0 || (start && end) || fragmentType is < 1 or > 23) return false;
        if (start)
        {
            if (_fragmentOpen) return false;
            _fragmentOpen = true;
            byte nalHeader = (byte)((payload[0] & 0xe0) | (fragmentHeader & 0x1f));
            if (!AppendStartCode() || !AppendBytes([nalHeader]) || !AppendBytes(payload[2..])) return false;
        }
        else
        {
            if (!_fragmentOpen || !AppendBytes(payload[2..])) return false;
        }
        if (end) _fragmentOpen = false;
        return true;
    }

    private bool AppendNal(ReadOnlySpan<byte> nal) => AppendStartCode() && AppendBytes(nal);
    private bool AppendStartCode() => AppendBytes([0, 0, 0, 1]);

    private bool AppendBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || _accessUnit.Length + value.Length > MaximumAccessUnitBytes) return false;
        _accessUnit.Write(value);
        return true;
    }

    private H264DepacketizeResult Reject(uint? timestamp = null, bool marker = false)
    {
        ResetAccessUnit();
        _decoderDiscontinuity = true;
        _discardUntilMarker = !marker;
        _discardTimestamp = timestamp;
        return new H264DepacketizeResult(null, true);
    }

    private void ResetAccessUnit()
    {
        _accessUnit.SetLength(0);
        _timestamp = null;
        _fragmentOpen = false;
    }

    public void Dispose() => _accessUnit.Dispose();

}
