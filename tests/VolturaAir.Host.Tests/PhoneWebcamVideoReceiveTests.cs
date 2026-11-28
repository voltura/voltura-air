using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamVideoReceiveTests
{
    [Theory]
    [InlineData("a=candidate:1 1 UDP 1 192.0.2.1 5000 typ relay\r\n", true)]
    [InlineData("a=candidate:1 1 TCP 1 192.0.2.1 5000 typ relay\r\na=candidate:2 1 UDP 1 192.0.2.2 5000 typ relay\r\n", true)]
    [InlineData("", false)]
    [InlineData("a=candidate:1 1 UDP 1 192.0.2.1 5000 typ relay\r\na=candidate:2 1 UDP 1 192.0.2.2 5000 typ host\r\n", false)]
    public void PhoneWebcamRelayRequiresAtLeastOneCandidateAndEveryCandidateToBeRelay(string sdp, bool expected)
    {
        Assert.Equal(expected, PhoneWebcamWebRtcPeer.HasOnlyRelayCandidates(sdp));
    }

    [Fact]
    public void KeyFrameRecoveryRequestsAreBoundedAcrossDecoderAndRtpCallbacks()
    {
        long lastRequest = long.MinValue;
        long now = Stopwatch.GetTimestamp();
        int admitted = 0;
        Parallel.For(0, 64, _ =>
        {
            if (PhoneWebcamWebRtcPeer.TryAdmitKeyFrameRequest(ref lastRequest, now))
                Interlocked.Increment(ref admitted);
        });

        Assert.Equal(1, admitted);
        Assert.False(PhoneWebcamWebRtcPeer.TryAdmitKeyFrameRequest(ref lastRequest, now + Stopwatch.Frequency / 8));
        Assert.True(PhoneWebcamWebRtcPeer.TryAdmitKeyFrameRequest(ref lastRequest, now + Stopwatch.Frequency));
    }

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

    [Fact]
    public void NativeReceiveForwardsTheLossToTheNextAccessUnitOnly()
    {
        using var depacketizer = new H264RtpDepacketizer();
        nint packet = Marshal.AllocHGlobal(64);
        try
        {
            var received = new List<bool>();
            void Push(byte[] data)
            {
                Marshal.Copy(data, 0, packet, data.Length);
                PhoneWebcamWebRtcPeer.DepacketizeVideoRtpPacket(depacketizer, packet, data.Length,
                    (_, _, discontinuity) => received.Add(discontinuity));
            }
            Push(Packet(1, false, [0x7c, 0x85, 1]));
            Push(Packet(3, true, [0x7c, 0x45, 2]));
            Push(Packet(4, true, [0x65, 3]));
            Push(Packet(5, true, [0x61, 4]));
            Assert.Equal(new[] { true, false }, received);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }

    [Fact]
    public async Task WebcamPipelineKeepsAQueuedKeyFrameWhenItReplacesAnOlderAccessUnit()
    {
        var published = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipeline = new PhoneWebcamVideoPipeline(frame =>
        {
            using (frame) published.TrySetResult(frame.SourceTimestamp90Khz);
        }, new PhoneWebcamFrameSequence());
        pipeline.Submit(PhoneWebcamFeatureTests.ReadAccessUnit("chromium-bear-320x192-baseline-frame-1.h264.b64"), 10);
        pipeline.Submit(PhoneWebcamFeatureTests.ReadAccessUnit("chromium-bear-320x192-baseline-keyframe.h264.b64"), 20);
        pipeline.Start();
        Assert.Equal(20UL, await published.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebcamPipelineWaitsForAKeyFrameAfterTransportLossOrQueueOverflow(bool overflow)
    {
        byte[] keyFrame = PhoneWebcamFeatureTests.ReadAccessUnit("chromium-bear-320x192-baseline-keyframe.h264.b64");
        byte[] dependentFrame = PhoneWebcamFeatureTests.ReadAccessUnit("chromium-bear-320x192-baseline-frame-1.h264.b64");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var firstPublication = new ManualResetEventSlim(false);
        var events = Channel.CreateUnbounded<ulong>();
        await using var pipeline = new PhoneWebcamVideoPipeline(frame =>
        {
            using (frame)
            {
                events.Writer.TryWrite(frame.SourceTimestamp90Khz);
                if (frame.SourceTimestamp90Khz == 10) firstPublication.Wait(deadline.Token);
            }
        }, new PhoneWebcamFrameSequence());
        pipeline.KeyFrameRequested += (_, _) => events.Writer.TryWrite(0);
        pipeline.Start();

        try
        {
            pipeline.Submit(keyFrame, 10);
            Assert.Equal(10UL, await events.Reader.ReadAsync(deadline.Token));
            if (overflow) pipeline.Submit(dependentFrame, 20);
            pipeline.Submit(dependentFrame, 30, discontinuity: !overflow);
        }
        finally
        {
            firstPublication.Set();
        }

        Assert.Equal(0UL, await events.Reader.ReadAsync(deadline.Token));
        pipeline.Submit(keyFrame, 40);
        Assert.Equal(40UL, await events.Reader.ReadAsync(deadline.Token));
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
