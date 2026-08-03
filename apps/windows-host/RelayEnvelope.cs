namespace VolturaAir.Host;

internal enum RelayEnvelopeKind : byte
{
    Text = 0,
    Connected = 1,
    Disconnected = 2,
    Binary = 3,
    CloseDevice = 4
}

internal sealed record RelayEnvelope(RelayEnvelopeKind Kind, Guid SessionId, byte[] Payload)
{
    private const int HeaderLength = 18;
    internal const int MaximumPayloadBytes = WebSocketTransport.MaxMessageBytes + RelaySessionCrypto.FrameOverhead;
    internal const int MaximumEncodedBytes = HeaderLength + MaximumPayloadBytes;
    private const byte Version = 1;

    public byte[] Encode()
    {
        if (Payload.Length > MaximumPayloadBytes ||
            Kind == RelayEnvelopeKind.CloseDevice && Payload.Length != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Payload));
        }

        var result = new byte[HeaderLength + Payload.Length];
        result[0] = Version;
        result[1] = (byte)Kind;
        SessionId.TryWriteBytes(result.AsSpan(2, 16), bigEndian: true, out _);
        Payload.CopyTo(result, HeaderLength);
        return result;
    }

    public static bool TryDecode(ReadOnlySpan<byte> value, out RelayEnvelope? envelope)
    {
        envelope = null;
        if (value.Length < HeaderLength || value.Length > MaximumEncodedBytes ||
            value[0] != Version || value[1] > (byte)RelayEnvelopeKind.CloseDevice)
        {
            return false;
        }

        envelope = new RelayEnvelope(
            (RelayEnvelopeKind)value[1],
            new Guid(value.Slice(2, 16), bigEndian: true),
            value[HeaderLength..].ToArray());
        return true;
    }
}
