using System.Buffers.Binary;

namespace VolturaAir.Host.Tests;

public sealed class TerminalProtocolTests
{
    [Theory]
    [InlineData((int)LibDataChannelNative.PeerState.Disconnected, false)]
    [InlineData((int)LibDataChannelNative.PeerState.Failed, true)]
    [InlineData((int)LibDataChannelNative.PeerState.Closed, true)]
    public void TerminalPeerToleratesTransientDisconnection(int state, bool expected)
    {
        Assert.Equal(
            expected,
            TerminalWebRtcPeer.ShouldStopForPeerState((LibDataChannelNative.PeerState)state));
    }

    [Theory]
    [InlineData(1024 * 1024 - 1, 1, true)]
    [InlineData(1024 * 1024 - 1, 2, false)]
    [InlineData(-1, 1, false)]
    public void TerminalPeerAccountsForTheWholeRecordBeforeBuffering(int bufferedAmount, int recordLength, bool expected)
    {
        Assert.Equal(expected, TerminalWebRtcPeer.CanBufferRecord(bufferedAmount, recordLength));
    }

    [Fact]
    public void OutputRoundTripsWithOffsetAndBoundedBytes()
    {
        byte[] bytes = TerminalProtocol.CreateOutput(42, [0xff, 0x00, 0x61]);

        Assert.True(TerminalProtocol.TryParse(bytes, out var record));
        Assert.Equal(TerminalRecordKind.Output, record.Kind);
        Assert.Equal(42, record.Offset);
        Assert.Equal([0xff, 0x00, 0x61], record.Payload.ToArray());
    }

    [Fact]
    public void ResizeUsesExactShapeAndBounds()
    {
        byte[] bytes = TerminalProtocol.CreateResize(120, 40);

        Assert.Equal(TerminalProtocol.ResizeRecordBytes, bytes.Length);
        Assert.True(TerminalProtocol.TryParse(bytes, out var record));
        Assert.Equal((ushort)120, record.Columns);
        Assert.Equal((ushort)40, record.Rows);
        bytes[1] = 0;
        bytes[2] = 1;
        Assert.False(TerminalProtocol.TryParse(bytes, out _));
    }

    [Fact]
    public void ParserRejectsUnknownKindsAndOversizedPayloads()
    {
        Assert.False(TerminalProtocol.TryParse(new byte[] { (byte)((TerminalProtocol.Version << 4) | 15) }, out _));
        byte[] oversized = new byte[TerminalProtocol.MaximumRecordBytes + 1];
        oversized[0] = (byte)((TerminalProtocol.Version << 4) | (byte)TerminalRecordKind.Input);
        Assert.False(TerminalProtocol.TryParse(oversized, out _));
    }

    [Fact]
    public void ParserRejectsWrongVersionEmptyPayloadsAndMalformedOffsets()
    {
        Assert.False(TerminalProtocol.TryParse(new byte[] { (byte)((2 << 4) | (byte)TerminalRecordKind.Input) }, out _));
        Assert.False(TerminalProtocol.TryParse(new byte[] { (byte)((TerminalProtocol.Version << 4) | (byte)TerminalRecordKind.Input), 0, 0, 0, 0, 0, 0, 0, 0 }, out _));

        byte[] input = TerminalProtocol.CreateInput([0x61]);
        input[8] = 1;
        Assert.False(TerminalProtocol.TryParse(input, out _));

        byte[] acknowledgementWithPayload = [.. TerminalProtocol.CreateAcknowledgement(0), 0x61];
        Assert.False(TerminalProtocol.TryParse(acknowledgementWithPayload, out _));
    }

    [Fact]
    public void AcknowledgementUsesBigEndianOffset()
    {
        byte[] bytes = TerminalProtocol.CreateAcknowledgement(0x01020304050607);

        Assert.Equal((ulong)0x01020304050607, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(1)));
        Assert.True(TerminalProtocol.TryParse(bytes, out var record));
        Assert.Equal(TerminalRecordKind.Acknowledgement, record.Kind);
    }

    [Fact]
    public void NegotiationTranscriptsBindSessionAndDimensions()
    {
        Assert.Equal("VolturaAir terminal:start:v1\nclient\nhost\nop\n80\n24", TerminalNegotiation.StartTranscript("client", "host", "op", 80, 24));
        Assert.Equal("VolturaAir terminal:attach:v1\nclient\nhost\nop\nsession\n17\n80\n24", TerminalNegotiation.AttachTranscript("client", "host", "op", "session", 17, 80, 24));
        Assert.Equal("VolturaAir terminal:answer:v1\nclient\nhost\noffer-op\nanswer-op\nsession\noffer-hash\nanswer-hash", TerminalNegotiation.AnswerTranscript("client", "host", "offer-op", "answer-op", "session", "offer-hash", "answer-hash"));
    }
}
