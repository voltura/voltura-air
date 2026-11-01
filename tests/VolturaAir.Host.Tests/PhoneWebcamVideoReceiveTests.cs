using System.Buffers.Binary;
using System.Runtime.InteropServices;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamVideoReceiveTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65537)]
    public void InvalidNativeLengthsAreIgnoredWithoutReadingMemory(int length)
    {
        using var depacketizer = new H264RtpDepacketizer();
        H264DepacketizeResult result = PhoneWebcamWebRtcPeer.DepacketizeVideoRtpPacket(depacketizer, 0, length);
        Assert.Null(result.AccessUnit);
        Assert.False(result.RequestKeyFrame);
    }

    [Fact]
    public void FragmentedVideoRemainsOwnedAfterNativePacketMemoryIsReusedAndFreed()
    {
        using var depacketizer = new H264RtpDepacketizer();
        nint packet = Marshal.AllocHGlobal(64);
        H264DepacketizeResult completed;
        try
        {
            byte[] first = Packet(1, false, [0x7c, 0x85, 1, 2]);
            Marshal.Copy(first, 0, packet, first.Length);
            Assert.Null(PhoneWebcamWebRtcPeer.DepacketizeVideoRtpPacket(depacketizer, packet, first.Length).AccessUnit);
            byte[] last = Packet(2, true, [0x7c, 0x45, 3, 4]);
            Marshal.Copy(last, 0, packet, last.Length);
            completed = PhoneWebcamWebRtcPeer.DepacketizeVideoRtpPacket(depacketizer, packet, last.Length);
            Marshal.Copy(new byte[64], 0, packet, 64);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
        Assert.False(completed.RequestKeyFrame);
        Assert.Equal(10u, completed.RtpTimestamp);
        Assert.Equal([0, 0, 0, 1, 0x65, 1, 2, 3, 4], completed.AccessUnit);
    }

    [Fact]
    public void MalformedNativePacketRequestsRecovery()
    {
        using var depacketizer = new H264RtpDepacketizer();
        nint packet = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(packet, 0x80);
            H264DepacketizeResult result = PhoneWebcamWebRtcPeer.DepacketizeVideoRtpPacket(depacketizer, packet, 1);
            Assert.True(result.RequestKeyFrame);
            Assert.Null(result.AccessUnit);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }

    private static byte[] Packet(ushort sequence, bool marker, byte[] payload)
    {
        byte[] packet = new byte[12 + payload.Length];
        packet[0] = 0x80;
        packet[1] = marker ? (byte)0xe6 : (byte)0x66;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), 10);
        payload.CopyTo(packet, 12);
        return packet;
    }
}
