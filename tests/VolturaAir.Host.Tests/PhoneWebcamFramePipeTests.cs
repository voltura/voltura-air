using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamFramePipeTests
{
    [Fact]
    public async Task IdleReaderDisconnectReleasesTheConnectionBeforeAnotherFrameArrives()
    {
        string pipeName = $"voltura-air-webcam-test-{Guid.NewGuid():N}";
        await using PhoneWebcamFramePipeServer server = CreateServer(pipeName);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (NamedPipeClientStream reader = await ConnectAsync(pipeName, deadline.Token))
        {
            server.Clear();
            AssertClear(await ReadHeaderAsync(reader, deadline.Token));
        }

        await using NamedPipeClientStream replacement = await ConnectAsync(pipeName, deadline.Token);
        server.Clear();
        AssertClear(await ReadHeaderAsync(replacement, deadline.Token));
    }

    [Fact]
    public async Task StalledFrameWriteReleasesItsBufferAndLetsTheNextReaderReceiveTheClear()
    {
        string pipeName = $"voltura-air-webcam-test-{Guid.NewGuid():N}";
        await using PhoneWebcamFramePipeServer server = CreateServer(pipeName);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using NamedPipeClientStream stalled = await ConnectAsync(pipeName, deadline.Token);
        var owner = new TrackingFrameOwner();
        server.Publish(new PhoneWebcamFrame(1, 90, owner));
        byte[] header = await ReadHeaderAsync(stalled, deadline.Token);
        Assert.True(header.AsSpan(0, 4).SequenceEqual("VAWF"u8));
        Assert.Equal(1UL, BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)));
        server.Clear();

        // Keep the first reader connected without draining its frame payload.
        await owner.Released.Task.WaitAsync(deadline.Token);
        await using NamedPipeClientStream replacement = await ConnectAsync(pipeName, deadline.Token);
        AssertClear(await ReadHeaderAsync(replacement, deadline.Token));
    }

    private static PhoneWebcamFramePipeServer CreateServer(string pipeName) => new(
        () => new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, inBufferSize: 4096, outBufferSize: 4096),
        _ => { }, // In-process test readers use a unique local pipe. Production requires Frame Server's identity.
        TimeSpan.FromMilliseconds(500));

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        var reader = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await reader.ConnectAsync(cancellationToken);
            byte[] handshake = [(byte)'V', (byte)'A', (byte)'W', (byte)'H', 1, 0, 0, 0];
            await reader.WriteAsync(handshake, cancellationToken);
            return reader;
        }
        catch
        {
            await reader.DisposeAsync();
            throw;
        }
    }

    private static async Task<byte[]> ReadHeaderAsync(NamedPipeClientStream reader, CancellationToken cancellationToken)
    {
        byte[] header = new byte[40];
        await reader.ReadExactlyAsync(header, cancellationToken);
        return header;
    }

    private static void AssertClear(byte[] header)
    {
        Assert.True(header.AsSpan(0, 4).SequenceEqual("VAWC"u8));
        Assert.Equal(PhoneWebcamFrameContract.ProtocolVersion, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)));
    }

    private sealed class TrackingFrameOwner : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = new byte[PhoneWebcamFrameContract.FrameBytes];
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Released.TrySetResult();
    }
}
