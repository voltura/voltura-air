using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamFeatureTests
{
    [Fact]
    public void ProcessTokenAccessRemainsGrantedUntilTheLastOverlappingLeaseEnds()
    {
        var frameServerSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "FrameServer")
            .Translate(typeof(SecurityIdentifier));
        Assert.Equal(0, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);

        using IDisposable first = PhoneWebcamProcessTokenAccess.Grant(frameServerSid);
        Assert.Equal(1, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
        IDisposable second = PhoneWebcamProcessTokenAccess.Grant(frameServerSid);
        Assert.Equal(2, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);

        first.Dispose();
        Assert.Equal(1, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
        second.Dispose();
        Assert.Equal(0, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
    }

    [Fact]
    public void ProcessTokenAccessRetainsAReleaseOwnerAndRetriesAfterEveryAclRestoreFails()
    {
        var frameServerSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "FrameServer")
            .Translate(typeof(SecurityIdentifier));
        Assert.Equal(0, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
        IDisposable lease = PhoneWebcamProcessTokenAccess.Grant(frameServerSid);
        try
        {
            PhoneWebcamProcessTokenAccess.SetRestoreFailureForTests(
                objectName => new IOException($"Injected {objectName} restore failure."));

            AggregateException failure = Assert.Throws<AggregateException>(() => lease.Dispose());

            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.Equal(1, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
            PhoneWebcamProcessTokenAccess.SetRestoreFailureForTests(null);
            lease.Dispose();
            Assert.Equal(0, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
        }
        finally
        {
            PhoneWebcamProcessTokenAccess.SetRestoreFailureForTests(null);
            if (PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests > 0) lease.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentLeaseDisposerRetriesAfterTheFirstAclRestoreFails()
    {
        var frameServerSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "FrameServer")
            .Translate(typeof(SecurityIdentifier));
        Assert.Equal(0, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
        IDisposable lease = PhoneWebcamProcessTokenAccess.Grant(frameServerSid);
        using var firstRestoreEntered = new ManualResetEventSlim();
        using var allowFirstRestore = new ManualResetEventSlim();
        var injected = 0;
        try
        {
            PhoneWebcamProcessTokenAccess.SetRestoreFailureForTests(objectName =>
            {
                if (objectName != "host token" || Interlocked.Exchange(ref injected, 1) != 0) return null;
                firstRestoreEntered.Set();
                allowFirstRestore.Wait(TimeSpan.FromSeconds(5));
                return new IOException("Injected first token restore failure.");
            });

            Task first = Task.Run(lease.Dispose);
            Assert.True(firstRestoreEntered.Wait(TimeSpan.FromSeconds(5)));
            Task second = Task.Run(lease.Dispose);
            allowFirstRestore.Set();

            await Assert.ThrowsAsync<AggregateException>(() => first);
            await second.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests);
        }
        finally
        {
            allowFirstRestore.Set();
            PhoneWebcamProcessTokenAccess.SetRestoreFailureForTests(null);
            if (PhoneWebcamProcessTokenAccess.ActiveLeaseCountForTests > 0) lease.Dispose();
        }
    }

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
        using PhoneWebcamFrame latest = Assert.IsType<PhoneWebcamFrame>(await queue.TakeAsync(CancellationToken.None));
        Assert.Equal((ulong)2, latest.Sequence);
        Assert.Equal((ulong)20, latest.SourceTimestamp90Khz);
    }

    [Fact]
    public async Task ExplicitClearDisplacesTheLastFrameAndProducesAWaitingRecord()
    {
        using var queue = new PhoneWebcamLatestFrameQueue();
        var owner = new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes);
        queue.Publish(new PhoneWebcamFrame(1, 10, owner));

        queue.Clear();

        Assert.True(owner.IsDisposed);
        Assert.Null(await queue.TakeAsync(CancellationToken.None));
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

    [Fact]
    public async Task LatestFrameQueueDisposalReleasesAnActiveWaiterAndIsIdempotent()
    {
        var queue = new PhoneWebcamLatestFrameQueue();
        Task<PhoneWebcamFrame?> waiting = queue.TakeAsync(CancellationToken.None);

        queue.Dispose();
        queue.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await waiting.WaitAsync(TimeSpan.FromSeconds(2)));
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

    [Fact]
    public async Task InstallationStatusChangeIsPublishedToConnectedClientStatusOwners()
    {
        await using var feature = new PhoneWebcamFeature(new FailedInstallSetup());
        var changes = 0;
        feature.StatusChanged += (_, _) => changes++;

        PhoneWebcamFeatureStatus result = await feature.EnableAsync();

        Assert.Equal(PhoneWebcamFeatureState.Failed, result.State);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task MissingRequiredNativePayloadFailsProductionFeatureComposition()
    {
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PhoneWebcamFeature.CreateAsync(new UnavailableSetup()));

        Assert.Equal("Required Phone webcam payload is missing.", exception.Message);
    }

    [Fact]
    public async Task FrameSequenceRemainsMonotonicAcrossProducerPipelines()
    {
        var feature = new RecordingPhoneWebcamFeature();
        var sequence = new PhoneWebcamFrameSequence();
        await using var first = new PhoneWebcamVideoPipeline(feature, sequence);
        await using var second = new PhoneWebcamVideoPipeline(feature, sequence);
        using var queue = new PhoneWebcamLatestFrameQueue();
        queue.Publish(new PhoneWebcamFrame(
            first.AllocateFrameSequence(),
            10,
            new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes)));
        queue.Publish(new PhoneWebcamFrame(
            second.AllocateFrameSequence(),
            20,
            new TrackingMemoryOwner(PhoneWebcamFrameContract.FrameBytes)));

        using PhoneWebcamFrame latest = Assert.IsType<PhoneWebcamFrame>(await queue.TakeAsync(CancellationToken.None));
        Assert.Equal((ulong)2, latest.Sequence);
    }

    [Fact]
    public void TrackErrorAfterOpenStillReportsTerminalStop()
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        opened.SetResult();
        var stopped = false;

        PhoneWebcamWebRtcPeer.ReportTrackError(opened, () => stopped = true, "injected");

        Assert.True(stopped);
        Assert.True(opened.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RuntimePipeFailureStopsTheProducerAndRetiresTheFeature()
    {
        string pipeName = $"voltura-air-webcam-test-{Guid.NewGuid():N}";
        var pipeCreations = 0;
        await using var feature = new PhoneWebcamFeature(
            new SuccessfulInstallSetup(),
            () => new PhoneWebcamFramePipeServer(() =>
            {
                if (Interlocked.Increment(ref pipeCreations) > 1)
                {
                    throw new IOException("Injected replacement pipe failure.");
                }

                return new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }));
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        feature.SetSessionStopper(() => { stopped.TrySetResult(); return Task.CompletedTask; });
        PhoneWebcamFeatureStatus installed = await feature.EnableAsync();
        Assert.True(installed.IsInstalled);

        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(2000);
            byte[] handshake = new byte[8];
            Encoding.ASCII.GetBytes("VAWH").CopyTo(handshake, 0);
            BinaryPrimitives.WriteInt32LittleEndian(handshake.AsSpan(4), PhoneWebcamFrameContract.ProtocolVersion);
            await client.WriteAsync(handshake);
            await client.FlushAsync();
        }

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AssertEventuallyAsync(() => feature.Status.HasError);
        Assert.False(feature.Status.IsInstalled);
        Assert.Equal(2, pipeCreations);
    }

    [Fact]
    public async Task FeatureDisposalWaitsForFailedPipeRetirement()
    {
        string pipeName = $"voltura-air-webcam-test-{Guid.NewGuid():N}";
        var pipeCreations = 0;
        var retirementStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var feature = new PhoneWebcamFeature(
            new SuccessfulInstallSetup(),
            () => new PhoneWebcamFramePipeServer(() =>
            {
                if (Interlocked.Increment(ref pipeCreations) > 1)
                    throw new IOException("Injected replacement pipe failure.");
                return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }));
        feature.SetSessionStopper(async () =>
        {
            retirementStarted.TrySetResult();
            await releaseRetirement.Task;
        });
        _ = await feature.EnableAsync();
        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(2000);
            await client.WriteAsync(new byte[8]);
        }
        await retirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task disposal = feature.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        releaseRetirement.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class FailedInstallSetup : IPhoneWebcamSetup
    {
        private static readonly PhoneWebcamFeatureStatus Failed = new(
            PhoneWebcamFeatureState.Failed,
            "Injected install failure.",
            HasError: true);

        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneWebcamFeatureStatus(PhoneWebcamFeatureState.NotInstalled, "Not installed."));

        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(Failed);

        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken) => Task.FromResult(Failed);
    }

    private sealed class UnavailableSetup : IPhoneWebcamSetup
    {
        private static readonly PhoneWebcamFeatureStatus Unavailable = new(
            PhoneWebcamFeatureState.Unavailable,
            "Required Phone webcam payload is missing.");

        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Unavailable);
        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(Unavailable);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken) => Task.FromResult(Unavailable);
    }

    private sealed class SuccessfulInstallSetup : IPhoneWebcamSetup
    {
        private static readonly PhoneWebcamFeatureStatus NotInstalled = new(PhoneWebcamFeatureState.NotInstalled, "Not installed.");
        private static readonly PhoneWebcamFeatureStatus Installed = new(PhoneWebcamFeatureState.Installed, "Installed.");
        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(NotInstalled);
        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken) => Task.FromResult(NotInstalled);
    }

    private static async Task AssertEventuallyAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingPhoneWebcamFeature : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(PhoneWebcamFeatureState.Installed, "Installed.");
        public PhoneWebcamActivity Activity { get; } = new("idle");
        public event EventHandler? ActivityChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public void Publish(PhoneWebcamFrame frame) => frame.Dispose();
    }

    private sealed class TrackingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private byte[]? _buffer = new byte[length];

        internal bool IsDisposed => _buffer is null;

        public Memory<byte> Memory => _buffer ?? throw new ObjectDisposedException(nameof(TrackingMemoryOwner));

        public void Dispose() => _buffer = null;
    }
}
