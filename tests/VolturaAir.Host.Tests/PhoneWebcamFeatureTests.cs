using System.Buffers;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamFeatureTests
{
    [Fact]
    public void FrameContractRemainsFixedAtNv12FullHd()
    {
        Assert.Equal(1, PhoneWebcamFrameContract.ProtocolVersion);
        Assert.Equal(1, PhoneWebcamFrameContract.Nv12Format);
        Assert.Equal(1920, PhoneWebcamFrameContract.Width);
        Assert.Equal(1080, PhoneWebcamFrameContract.Height);
        Assert.Equal(3_110_400, PhoneWebcamFrameContract.FrameBytes);
    }

    [Fact]
    public async Task LatestFrameQueueDisposesTheDisplacedFrameInsteadOfBuildingBacklog()
    {
        using var queue = new PhoneWebcamLatestFrameQueue();
        var firstOwner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);
        var secondOwner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);

        queue.Publish(new PhoneWebcamFrame(1, 10, firstOwner));
        queue.Publish(new PhoneWebcamFrame(2, 20, secondOwner));

        Assert.True(firstOwner.IsDisposed);
        Assert.False(secondOwner.IsDisposed);
        using PhoneWebcamFrame latest = await queue.TakeAsync(CancellationToken.None);
        Assert.Equal((ulong)2, latest.Sequence);
        Assert.Equal((ulong)20, latest.SourceTimestamp90Khz);
    }

    [Fact]
    public void LatestFrameQueueDisposesItsPendingFrameOnShutdown()
    {
        var owner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);
        var queue = new PhoneWebcamLatestFrameQueue();
        queue.Publish(new PhoneWebcamFrame(1, 10, owner));

        queue.Dispose();

        Assert.True(owner.IsDisposed);
    }

    [Theory]
    [InlineData("{\"installed\":true,\"cleanupRequired\":true,\"updateRequired\":false}", true, true, false)]
    [InlineData("{\"installed\":true,\"cleanupRequired\":true,\"updateRequired\":true}", true, true, true)]
    [InlineData("{\"installed\":false,\"cleanupRequired\":false,\"updateRequired\":false}", false, false, false)]
    [InlineData("{\"installed\":false,\"cleanupRequired\":true,\"updateRequired\":false}", false, true, false)]
    public void SetupStatusAcceptsOnlyTheBoundedStateBooleans(
        string json,
        bool expectedInstalled,
        bool expectedCleanupRequired,
        bool expectedUpdateRequired)
    {
        Assert.True(PhoneWebcamSetup.TryReadStatus(
            json,
            out bool installed,
            out bool cleanupRequired,
            out bool updateRequired));
        Assert.Equal(expectedInstalled, installed);
        Assert.Equal(expectedCleanupRequired, cleanupRequired);
        Assert.Equal(expectedUpdateRequired, updateRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"installed\":\"true\",\"cleanupRequired\":true,\"updateRequired\":false}")]
    [InlineData("{\"installed\":true}")]
    [InlineData("{\"installed\":true,\"cleanupRequired\":false,\"updateRequired\":false}")]
    [InlineData("{\"installed\":false,\"cleanupRequired\":false,\"updateRequired\":true}")]
    [InlineData("{\"other\":true}")]
    public void SetupStatusRejectsMalformedOrWrongShapeOutput(string output)
    {
        Assert.False(PhoneWebcamSetup.TryReadStatus(output, out _, out _, out _));
    }

    [Fact]
    public void LatestFrameQueueDisposesFramesPublishedAfterShutdown()
    {
        var owner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);
        var queue = new PhoneWebcamLatestFrameQueue();
        queue.Dispose();

        queue.Publish(new PhoneWebcamFrame(1, 10, owner));

        Assert.True(owner.IsDisposed);
    }

    [Fact]
    public void LatestFrameQueueRejectsAndDisposesNonMonotonicFrames()
    {
        var firstOwner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);
        var staleOwner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);
        using var queue = new PhoneWebcamLatestFrameQueue();
        queue.Publish(new PhoneWebcamFrame(2, 20, firstOwner));

        Assert.Throws<InvalidDataException>(() =>
            queue.Publish(new PhoneWebcamFrame(1, 10, staleOwner)));

        Assert.True(staleOwner.IsDisposed);
        Assert.False(firstOwner.IsDisposed);
    }

    [Fact]
    public unsafe void PreviewConversionProducesOpaqueNeutralWhiteBgra()
    {
        byte[] source = new byte[PhoneWebcamFrameContract.FrameBytes];
        source.AsSpan(0, PhoneWebcamFrameContract.Width * PhoneWebcamFrameContract.Height).Fill(235);
        source.AsSpan(PhoneWebcamFrameContract.Width * PhoneWebcamFrameContract.Height).Fill(128);
        byte[] preview = new byte[PhoneWebcamPreviewSession.PreviewStride * PhoneWebcamPreviewSession.PreviewHeight];

        fixed (byte* sourcePointer = source)
        {
            PhoneWebcamPreviewSession.ConvertNv12ToPreview((nint)sourcePointer, preview);
        }

        Assert.Equal([255, 255, 255, 255], preview.AsSpan(0, 4).ToArray());
        Assert.Equal([255, 255, 255, 255], preview.AsSpan(preview.Length - 4, 4).ToArray());
    }

    private sealed class TrackingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private byte[]? _buffer = new byte[length];

        internal bool IsDisposed => _buffer is null;

        public Memory<byte> Memory => _buffer ?? throw new ObjectDisposedException(nameof(TrackingMemoryOwner));

        public void Dispose() => _buffer = null;
    }
}
