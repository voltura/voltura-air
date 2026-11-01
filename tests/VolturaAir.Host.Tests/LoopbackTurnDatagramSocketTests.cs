using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VolturaAir.Host.Tests;

// Adapter checks own only ephemeral loopback UDP sockets; protocol framing stays in fake-boundary tests.
public sealed class LoopbackTurnDatagramSocketTests
{
    [Fact]
    public async Task ReceiveUsesCallerMemoryAndRemainsUsableAfterCancellation()
    {
        using var socket = new LoopbackTurnDatagramSocket();
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var endpoint = new IPEndPoint(IPAddress.Loopback, socket.LocalPort);
        byte[] buffer = Enumerable.Repeat((byte)0xee, TurnStreamFraming.MaximumFrameBytes).ToArray();
        byte[] payload = [1, 2, 3, 4, 5];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = await sender.SendAsync(payload, endpoint, timeout.Token);
        TurnDatagram first = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(payload, first.Payload.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(first.Payload, out ArraySegment<byte> segment));
        Assert.Same(buffer, segment.Array);
        Assert.Equal(0xee, buffer[payload.Length]);

        using var canceled = new CancellationTokenSource();
        ValueTask<TurnDatagram> pending = socket.ReceiveAsync(buffer, canceled.Token);
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);

        byte[] next = [6, 7];
        _ = await sender.SendAsync(next, endpoint, timeout.Token);
        TurnDatagram second = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(next, second.Payload.ToArray());
        Assert.Equal(first.RemoteEndpoint, second.RemoteEndpoint);
    }

    [Fact]
    public async Task DisposalReleasesPendingReceiveAndRejectsUndersizedCallerBuffers()
    {
        using var socket = new LoopbackTurnDatagramSocket();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await socket.ReceiveAsync(new byte[4], CancellationToken.None));
        ValueTask<TurnDatagram> pending = socket.ReceiveAsync(new byte[TurnStreamFraming.MaximumFrameBytes], CancellationToken.None);
        socket.Dispose();
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(failure is SocketException or ObjectDisposedException);
    }
}
