using System.Threading.Channels;
using NAudio.CoreAudioApi;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewSystemAudioTests
{
    [Fact]
    public void AudioClockRetainsSamplePositionBeyondTheOldOverflowRange()
    {
        long ticks = TimeSpan.FromDays(365).Ticks + 12_345;
        long expected = (long)((Int128)ticks * ScreenViewSystemAudioCapture.SampleRate / TimeSpan.TicksPerSecond);

        Assert.Equal(expected, ScreenViewSystemAudioCapture.TimestampFromPosition(ticks));
        Assert.Equal(0L, ScreenViewSystemAudioCapture.TimestampFromPosition(0));
    }

    [Fact]
    public void OversizedAudioBlocksKeepTheNewestCompleteSamplesAndTheirTimestamp()
    {
        int size = ScreenViewSystemAudioCapture.FrameSamples * 4 * 5;
        byte[] capture = new byte[size + 8 + 2];
        capture[8] = 41;
        capture[8 + size - 1] = 42;

        var block = ScreenViewSystemAudioCapture.CopyAudioBlock(capture, 0, TimeSpan.TicksPerSecond, 5);

        Assert.NotNull(block);
        Assert.Equal(size, block.Bytes.Length);
        Assert.Equal(41, block.Bytes[0]);
        Assert.Equal(42, block.Bytes[^1]);
        Assert.Equal(48_002L, block.Timestamp);
        Assert.Equal(7, block.SamplePosition);
        Assert.True(block.Discontinuity);
    }

    [Fact]
    public void AudioBlocksRespectWindowsDiscontinuityTimestampAndSilenceFlags()
    {
        byte[] capture = [2, 3, 4, 5];
        var block = ScreenViewSystemAudioCapture.CopyAudioBlock(
            capture,
            AudioClientBufferFlags.Silent | AudioClientBufferFlags.DataDiscontinuity | AudioClientBufferFlags.TimestampError,
            TimeSpan.TicksPerSecond,
            3);

        Assert.NotNull(block);
        Assert.Equal([0, 0, 0, 0], block.Bytes);
        Assert.Null(block.Timestamp);
        Assert.True(block.Discontinuity);
    }

    [Fact]
    public void AudioQueueKeepsNewBlocksAndIdentifiesLossAtTheCorrectBoundary()
    {
        var queue = ScreenViewSystemAudioCapture.CreatePendingAudioQueue();
        for (int index = 0; index < 6; index++)
            Assert.True(queue.Writer.TryWrite(new([0, 0, 0, 0], 0, index, false)));

        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal(1, first.SamplePosition);
        Assert.True(queue.Reader.TryRead(out var second));
        Assert.Equal(2, second.SamplePosition);
        queue.Writer.TryComplete();
        Assert.False(queue.Writer.TryWrite(first));
    }

    [Fact]
    public async Task EncoderUsesUniformSampleTimingThroughCaptureJitterAndRtpWrap()
    {
        const long start = uint.MaxValue - 479L;
        uint[] timestamps = await EncodeTimestampsAsync(
            Block(960, start, 0),
            Block(480, start + 964, 960),
            Block(480, start + 1_436, 1_440),
            Block(960, start + 1_917, 1_920),
            Block(960, null, 2_880),
            Block(960, start + 3_841, 3_840));

        Assert.Equal([(uint)start, 480u, 1_440u, 2_400u, 3_360u], timestamps);
    }

    [Fact]
    public async Task EncoderPreservesRealIdleGapsAndDropsUnfinishedAudioAcrossThem()
    {
        uint[] timestamps = await EncodeTimestampsAsync(
            Block(960, 10_000, 0),
            Block(480, 10_963, 960),
            Block(480, 18_643, 1_440),
            Block(960, 19_124, 1_920),
            Block(480, 20_079, 2_880));

        // The idle gap is derived from adjacent QPC positions. Samples on opposite sides
        // of the gap are never combined into one frame.
        Assert.Equal([10_000u, 18_640u, 19_600u], timestamps);
    }

    [Fact]
    public async Task EncoderCountsShortQueueLossEvenWhenTheNextTimestampIsUnreliable()
    {
        uint[] timestamps = await EncodeTimestampsAsync(
            Block(960, 10_000, 0),
            Block(480, null, 1_440),
            Block(480, 11_920, 1_920),
            Block(960, 12_400, 2_400));

        Assert.Equal([10_000u, 11_440u, 12_400u], timestamps);
    }

    [Fact]
    public async Task EncoderContinuesForwardAcrossCaptureDiscontinuityAndEndpointReplacement()
    {
        var clock = new ScreenViewSystemAudioCapture.AudioFrameClock();
        uint[] before = await EncodeTimestampsAsync(clock,
            Block(960, 10_000, 0),
            Block(480, 10_960, 960),
            Block(960, 11_400, 1_440, discontinuity: true));
        uint[] after = await EncodeTimestampsAsync(clock,
            Block(960, 12_300, 0),
            Block(960, 13_260, 960));

        Assert.Equal([10_000u, 11_440u], before);
        Assert.Equal([12_400u, 13_360u], after);
    }

    [Fact]
    public async Task FullCaptureBlockFitsAlongsideAnUnfinishedFrame()
    {
        uint[] timestamps = await EncodeTimestampsAsync(
            Block(480, 10_000, 0),
            Block(4_800, 10_480, 480),
            Block(480, 15_280, 5_280));

        Assert.Equal([10_000u, 10_960u, 11_920u, 12_880u, 13_840u, 14_800u], timestamps);
    }

    private static ScreenViewSystemAudioCapture.AudioBlock Block(
        int samples,
        long? timestamp,
        long samplePosition,
        bool discontinuity = false) =>
        new(new byte[samples * ScreenViewSystemAudioCapture.Channels * sizeof(short)], timestamp, samplePosition, discontinuity);

    private static Task<uint[]> EncodeTimestampsAsync(params ScreenViewSystemAudioCapture.AudioBlock[] blocks) =>
        EncodeTimestampsAsync(new ScreenViewSystemAudioCapture.AudioFrameClock(), blocks);

    private static async Task<uint[]> EncodeTimestampsAsync(
        ScreenViewSystemAudioCapture.AudioFrameClock clock,
        params ScreenViewSystemAudioCapture.AudioBlock[] blocks)
    {
        var queue = Channel.CreateUnbounded<ScreenViewSystemAudioCapture.AudioBlock>();
        foreach (var block in blocks)
            Assert.True(queue.Writer.TryWrite(block));
        queue.Writer.Complete();

        List<uint> timestamps = [];
        await ScreenViewSystemAudioCapture.EncodeAsync(queue.Reader, clock, frame =>
        {
            Assert.NotEmpty(frame.Bytes);
            timestamps.Add(frame.RtpTimestamp);
            return true;
        }, () => ScreenViewSoundQuality.High, TestContext.Current.CancellationToken);
        return [.. timestamps];
    }

    [Fact]
    public async Task CaptureCompletionLeavesTheNextDeviceChangeAvailableForRecovery()
    {
        Channel<bool> changes = Channel.CreateUnbounded<bool>();

        ScreenViewAudioRunEnd result = await ScreenViewSystemAudioCapture.WaitForCaptureOrDeviceChangeAsync(
            Task.CompletedTask,
            changes.Reader,
            TestContext.Current.CancellationToken);

        Assert.Equal(ScreenViewAudioRunEnd.CaptureStopped, result);
        Assert.True(changes.Writer.TryWrite(true));
        Assert.True(await changes.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CaptureFailureLeavesTheNextDeviceChangeAvailableForRecovery()
    {
        Channel<bool> changes = Channel.CreateUnbounded<bool>();
        Task failed = Task.FromException(new InvalidOperationException("Injected capture failure."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScreenViewSystemAudioCapture.WaitForCaptureOrDeviceChangeAsync(
                failed,
                changes.Reader,
                TestContext.Current.CancellationToken));

        Assert.True(changes.Writer.TryWrite(true));
        Assert.True(await changes.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeviceChangeWinsWithoutWaitingForCaptureCompletion()
    {
        Channel<bool> changes = Channel.CreateUnbounded<bool>();
        Assert.True(changes.Writer.TryWrite(true));

        ScreenViewAudioRunEnd result = await ScreenViewSystemAudioCapture.WaitForCaptureOrDeviceChangeAsync(
            Task.Delay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken),
            changes.Reader,
            TestContext.Current.CancellationToken);

        Assert.Equal(ScreenViewAudioRunEnd.DefaultDeviceChanged, result);
    }

    [Fact]
    public async Task WaitHonorsSessionCancellation()
    {
        Channel<bool> changes = Channel.CreateUnbounded<bool>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScreenViewSystemAudioCapture.WaitForCaptureOrDeviceChangeAsync(
                Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token),
                changes.Reader,
                cancellation.Token));
    }

    [Fact]
    public void AudioAvailabilityRequiresTheDocumentedNonEmptyBounds()
    {
        Assert.Throws<ArgumentException>(() => ScreenViewRecordEncoder.EncodeAudioAvailability(true, "", "ready"));
        Assert.Throws<ArgumentException>(() => ScreenViewRecordEncoder.EncodeAudioAvailability(true, "audio-ready", ""));
        Assert.Equal(5 + 64 + 512, ScreenViewRecordEncoder.EncodeAudioAvailability(true, new string('c', 64), new string('m', 512)).Length);
    }
}
