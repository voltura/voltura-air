using System.Buffers.Binary;
using Xunit;

namespace WebRtcSpike.Host.Tests;

public sealed class H264RtpDepacketizerTests
{
    [Fact]
    public void AssemblesSingleNalAndStapA()
    {
        using var depacketizer = new H264RtpDepacketizer();
        H264DepacketizeResult single = depacketizer.Push(Packet(1, 10, true, [0x65, 1, 2]));
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x65, 1, 2 }, single.AccessUnit);
        Assert.Equal(10u, single.RtpTimestamp);

        byte[] stap = [0x78, 0, 2, 0x67, 3, 0, 3, 0x68, 4, 5];
        H264DepacketizeResult aggregated = depacketizer.Push(Packet(2, 11, true, stap));
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x67, 3, 0, 0, 0, 1, 0x68, 4, 5 }, aggregated.AccessUnit);
    }

    [Fact]
    public void AssemblesFuA()
    {
        using var depacketizer = new H264RtpDepacketizer();
        Assert.Null(depacketizer.Push(Packet(10, 22, false, [0x7c, 0x85, 1, 2])).AccessUnit);
        Assert.Null(depacketizer.Push(Packet(11, 22, false, [0x7c, 0x05, 3])).AccessUnit);
        H264DepacketizeResult result = depacketizer.Push(Packet(12, 22, true, [0x7c, 0x45, 4]));
        Assert.False(result.RequestKeyFrame);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x65, 1, 2, 3, 4 }, result.AccessUnit);
    }

    [Fact]
    public void DropsAccessUnitOnSequenceGapOrTimestampChange()
    {
        using var depacketizer = new H264RtpDepacketizer();
        Assert.Null(depacketizer.Push(Packet(30, 40, false, [0x61, 1])).AccessUnit);
        H264DepacketizeResult gap = depacketizer.Push(Packet(32, 40, true, [0x61, 2]));
        Assert.True(gap.RequestKeyFrame);
        Assert.Null(gap.AccessUnit);

        Assert.Null(depacketizer.Push(Packet(33, 41, false, [0x61, 3])).AccessUnit);
        H264DepacketizeResult changed = depacketizer.Push(Packet(34, 42, true, [0x61, 4]));
        Assert.True(changed.RequestKeyFrame);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x61, 4 }, changed.AccessUnit);
    }

    [Fact]
    public void DiscardsCurrentAccessUnitWhenGapStartsAtANewTimestamp()
    {
        using var depacketizer = new H264RtpDepacketizer();
        Assert.NotNull(depacketizer.Push(Packet(10, 20, true, [0x61, 1])).AccessUnit);

        H264DepacketizeResult gap = depacketizer.Push(Packet(12, 21, true, [0x65, 2]));
        Assert.True(gap.RequestKeyFrame);
        Assert.Null(gap.AccessUnit);
        Assert.NotNull(depacketizer.Push(Packet(13, 22, true, [0x65, 3])).AccessUnit);
    }

    [Fact]
    public void DiscardsRemainderAfterMalformedPayload()
    {
        using var depacketizer = new H264RtpDepacketizer();
        H264DepacketizeResult malformed = depacketizer.Push(Packet(1, 10, false, [0x78, 0, 5, 0x67]));
        Assert.True(malformed.RequestKeyFrame);
        Assert.Null(malformed.AccessUnit);

        H264DepacketizeResult remainder = depacketizer.Push(Packet(2, 10, true, [0x61, 2]));
        Assert.True(remainder.RequestKeyFrame);
        Assert.Null(remainder.AccessUnit);
        Assert.NotNull(depacketizer.Push(Packet(3, 11, true, [0x65, 3])).AccessUnit);
    }

    [Theory]
    [InlineData(new byte[] { 0x78, 0, 2, 0x78, 1 })]
    [InlineData(new byte[] { 0x7c, 0x80, 1 })]
    [InlineData(new byte[] { 0x7c, 0x98, 1 })]
    public void RejectsInvalidAggregatedOrFragmentedNalTypes(byte[] payload)
    {
        using var depacketizer = new H264RtpDepacketizer();
        H264DepacketizeResult result = depacketizer.Push(Packet(1, 10, true, payload));
        Assert.True(result.RequestKeyFrame);
        Assert.Null(result.AccessUnit);
    }

    [Theory]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x80, 0x60, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 })]
    [InlineData(new byte[] { 0x80, 0x60, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0x7c, 0xc5, 1 })]
    public void RejectsMalformedPackets(byte[] packet)
    {
        using var depacketizer = new H264RtpDepacketizer();
        H264DepacketizeResult result = depacketizer.Push(packet);
        Assert.Null(result.AccessUnit);
        Assert.True(result.RequestKeyFrame);
    }

    [Fact]
    public void IdentifiesIdrAccessUnitsForDecoderRecovery()
    {
        Assert.True(VideoPipeline.ContainsIdr([0, 0, 0, 1, 0x67, 1, 0, 0, 0, 1, 0x65, 2]));
        Assert.False(VideoPipeline.ContainsIdr([0, 0, 0, 1, 0x67, 1, 0, 0, 0, 1, 0x61, 2]));
    }

    private static byte[] Packet(ushort sequence, uint timestamp, bool marker, byte[] payload)
    {
        byte[] packet = new byte[12 + payload.Length];
        packet[0] = 0x80;
        packet[1] = (byte)(102 | (marker ? 0x80 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), 1234);
        payload.CopyTo(packet, 12);
        return packet;
    }
}
