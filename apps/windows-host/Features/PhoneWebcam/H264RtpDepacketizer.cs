using System.Buffers.Binary;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal readonly record struct H264DepacketizeResult(byte[]? AccessUnit, bool RequestKeyFrame, uint? RtpTimestamp = null);

internal sealed class H264RtpDepacketizer : IDisposable
{
    internal const int MaximumAccessUnitBytes = 8 * 1024 * 1024;
    private readonly MemoryStream _accessUnit = new();
    private ushort? _expectedSequence;
    private uint? _timestamp;
    private bool _fragmentOpen;
    private bool _discardUntilMarker;

    internal H264DepacketizeResult Push(ReadOnlySpan<byte> packet)
    {
        if (!RtpPacket.TryRead(packet, out ushort sequence, out uint timestamp, out bool marker, out ReadOnlySpan<byte> payload))
        {
            _expectedSequence = null;
            _discardUntilMarker = true;
            return Reject();
        }

        bool loss = false;
        if (_expectedSequence.HasValue && sequence != _expectedSequence.Value)
        {
            ResetAccessUnit();
            _discardUntilMarker = true;
        }
        _expectedSequence = unchecked((ushort)(sequence + 1));
        if (_discardUntilMarker)
        {
            if (marker) _discardUntilMarker = false;
            return new H264DepacketizeResult(null, true);
        }
        if (_timestamp.HasValue && timestamp != _timestamp.Value && _accessUnit.Length != 0)
        {
            ResetAccessUnit();
            loss = true;
        }
        _timestamp = timestamp;

        if (!AppendPayload(payload))
        {
            _discardUntilMarker = !marker;
            return Reject();
        }
        if (!marker) return new H264DepacketizeResult(null, loss);
        if (_fragmentOpen || _accessUnit.Length == 0) return Reject();

        byte[] completed = _accessUnit.ToArray();
        ResetAccessUnit();
        return new H264DepacketizeResult(completed, loss, timestamp);
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

    private H264DepacketizeResult Reject()
    {
        ResetAccessUnit();
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
