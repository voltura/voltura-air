using System.Net.WebSockets;
using System.Text;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelayVirtualWebSocketTests
{
    [Fact]
    public async Task PreservesPreEncryptionTextFrames()
    {
        RelayEnvelope? sent = null;
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (envelope, _) =>
        {
            sent = envelope;
            return Task.CompletedTask;
        });
        var bytes = Encoding.UTF8.GetBytes("{\"type\":\"pair.hello\"}");

        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal(RelayEnvelopeKind.Text, Assert.IsType<RelayEnvelope>(sent).Kind);

        Assert.True(socket.TryReceive(bytes, isBinary: false));
        var buffer = new byte[128];
        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Text, received.MessageType);
        Assert.Equal(bytes, buffer[..received.Count]);
    }

    [Fact]
    public void RejectsBinaryBeforeEncryption()
    {
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);

        Assert.False(socket.TryReceive([1, 2, 3], isBinary: true));
        Assert.Equal(WebSocketState.Aborted, socket.State);
    }

    [Fact]
    public void AbortsALaggingDeviceInsteadOfGrowingItsCommandBacklog()
    {
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);
        var payload = Encoding.UTF8.GetBytes("{}");

        for (var index = 0; index < 32; index++)
        {
            Assert.True(socket.TryReceive(payload, isBinary: false));
        }

        Assert.False(socket.TryReceive(payload, isBinary: false));
        Assert.Equal(WebSocketState.Aborted, socket.State);
    }

    [Fact]
    public async Task DoesNotDeliverQueuedFramesAfterAbort()
    {
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);
        Assert.True(socket.TryReceive(Encoding.UTF8.GetBytes("{}"), isBinary: false));

        socket.Abort();
        var received = await socket.ReceiveAsync(new byte[32], CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, received.MessageType);
        Assert.Equal(0, received.Count);
    }

    [Fact]
    public async Task SendsOneDeviceCloseEnvelopeWhenTheHostClosesTheSession()
    {
        var sent = new List<RelayEnvelope>();
        using var socket = new RelayVirtualWebSocket(Guid.NewGuid(), new string('r', 22), (envelope, _) =>
        {
            sent.Add(envelope);
            return Task.CompletedTask;
        });

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Device disconnected", CancellationToken.None);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Device disconnected", CancellationToken.None);

        var envelope = Assert.Single(sent);
        Assert.Equal(RelayEnvelopeKind.CloseDevice, envelope.Kind);
        Assert.Empty(envelope.Payload);
        Assert.Equal(WebSocketState.Closed, socket.State);
    }
}
