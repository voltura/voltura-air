using System.Threading.Channels;
using NAudio.CoreAudioApi;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewSystemAudioTests
{
    [Fact]
    public void AudioClockRetainsSamplePositionBeyondTheOldOverflowRange()
    {
        long ticks = TimeSpan.FromDays(365).Ticks + 12_345;
        uint expected = unchecked((uint)((Int128)ticks * ScreenViewSystemAudioCapture.SampleRate / TimeSpan.TicksPerSecond));

        Assert.Equal(expected, ScreenViewSystemAudioCapture.TimestampFromPosition(ticks));
        Assert.Equal(0u, ScreenViewSystemAudioCapture.TimestampFromPosition(0));
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
        Assert.Equal(48_002u, block.Timestamp);
        Assert.False(block.Follows(4));
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
        Assert.False(block.Follows(2));
    }

    [Fact]
    public void AudioQueueKeepsNewBlocksAndIdentifiesLossAtTheCorrectBoundary()
    {
        var queue = ScreenViewSystemAudioCapture.CreatePendingAudioQueue();
        for (int index = 0; index < 6; index++)
            Assert.True(queue.Writer.TryWrite(new([0, 0, 0, 0], 0, index, false)));

        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal(1, first.Sequence);
        Assert.False(first.Follows(-1));
        Assert.True(queue.Reader.TryRead(out var second));
        Assert.True(second.Follows(first.Sequence));
        queue.Writer.TryComplete();
        Assert.False(queue.Writer.TryWrite(first));
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
