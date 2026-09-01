using System.Threading.Channels;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewSystemAudioTests
{
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
