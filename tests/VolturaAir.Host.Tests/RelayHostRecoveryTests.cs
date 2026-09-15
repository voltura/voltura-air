using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelayHostRecoveryTests
{
    [Fact]
    public void IdleRelaySocketRequiresAPongWithinABoundedWindow()
    {
        using var socket = RelayHostConnection.CreateClientSocket();
        Assert.Equal(TimeSpan.FromSeconds(20), socket.Options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(20), socket.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task DeviceDisconnectDoesNotCancelAnInFlightSharedRelaySend()
    {
        using var relay = CreateRelayServer();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new RelayHostConnection(
            new RelayEndpointDescriptor("test", new Uri("https://relay.test"), new Uri("wss://relay.test"), true),
            RelayRoutingIdentity.CreateEphemeral(),
            async (socket, _, _, cancellationToken) =>
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"health.pong\"}"),
                    WebSocketMessageType.Text, true, cancellationToken);
            }, NullAppLog.Instance,
            connectSocket: async (uri, cancellationToken) => new BlockingSendSocket(
                await relay.GetTestServer().CreateWebSocketClient().ConnectAsync(uri, cancellationToken),
                sendStarted, releaseSend));
        connection.StateChanged += (_, args) =>
        {
            if (args.State == RelayConnectionState.Connected) connected.TrySetResult();
        };
        connection.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sessionId = Guid.NewGuid();
        try
        {
            connection.ProcessRelayEnvelope(new RelayEnvelope(RelayEnvelopeKind.Connected, sessionId, []), CancellationToken.None);
            var sharedSendToken = await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection.ProcessRelayEnvelope(new RelayEnvelope(RelayEnvelopeKind.Disconnected, sessionId, []), CancellationToken.None);

            Assert.False(sharedSendToken.IsCancellationRequested);
            Assert.Equal(RelayConnectionState.Connected, connection.State);
        }
        finally
        {
            releaseSend.TrySetResult();
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("disposed")]
    [InlineData("io")]
    public async Task SocketFailureAfterAuthenticationLeavesConnectedAndReconnects(string failure)
    {
        using var relay = CreateRelayServer();
        var server = relay.GetTestServer();
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new RelayHostConnection(
            new RelayEndpointDescriptor("test", new Uri("https://relay.test"), new Uri("wss://relay.test"), true),
            RelayRoutingIdentity.CreateEphemeral(), (_, _, _, _) => Task.CompletedTask, NullAppLog.Instance,
            connectSocket: async (uri, cancellationToken) =>
            {
                var socket = await server.CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
                return Interlocked.Increment(ref connections) == 1
                    ? new CancelledReceiveSocket(socket, reading, fault, failure)
                    : socket;
            });
        connection.StateChanged += (_, args) =>
        {
            if (args.State == RelayConnectionState.Failed) failed.TrySetResult();
            if (args.State == RelayConnectionState.Connected && Volatile.Read(ref connections) > 1)
                recovered.TrySetResult();
        };
        connection.Start();
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RelayConnectionState.Connected, connection.State);

        fault.TrySetResult();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(RelayConnectionState.Connected, connection.State);
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RelayConnectionState.Connected, connection.State);
        Assert.Equal(2, Volatile.Read(ref connections));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledConnectOrAuthenticationTimesOutAndRetries(bool stallAuthentication)
    {
        using var relay = CreateRelayServer();
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var connection = new RelayHostConnection(
            new RelayEndpointDescriptor("test", new Uri("https://relay.test"), new Uri("wss://relay.test"), true),
            RelayRoutingIdentity.CreateEphemeral(), (_, _, _, _) => Task.CompletedTask, NullAppLog.Instance,
            connectSocket: async (uri, cancellationToken) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    if (stallAuthentication)
                    {
                        return new CancelledReceiveSocket(
                            await relay.GetTestServer().CreateWebSocketClient().ConnectAsync(uri, cancellationToken),
                            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), "cancel", failRead: 1);
                    }
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                return await relay.GetTestServer().CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
            });
        connection.StateChanged += (_, args) =>
        {
            if (args.State == RelayConnectionState.Connected) recovered.TrySetResult();
        };
        connection.Start();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task LateCloseSendFailureDoesNotAbortTheReplacementConnection()
    {
        using var relay = CreateRelayServer();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failReceive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0;
        await using var connection = new RelayHostConnection(
            new RelayEndpointDescriptor("test", new Uri("https://relay.test"), new Uri("wss://relay.test"), true),
            RelayRoutingIdentity.CreateEphemeral(), (_, _, _, _) => Task.CompletedTask, NullAppLog.Instance,
            connectSocket: async (uri, cancellationToken) =>
            {
                var socket = await relay.GetTestServer().CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
                return Interlocked.Increment(ref connections) == 1
                    ? new BlockingSendSocket(new CancelledReceiveSocket(socket, reading, failReceive),
                        sendStarted, releaseSend, failOnRelease: true)
                    : socket;
            });
        connection.StateChanged += (_, args) =>
        {
            if (args.State == RelayConnectionState.Connected && Volatile.Read(ref connections) > 1)
                recovered.TrySetResult();
        };
        connection.Start();
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            connection.ProcessRelayEnvelope(new RelayEnvelope(RelayEnvelopeKind.Text, Guid.NewGuid(), [1]), CancellationToken.None);
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            failReceive.TrySetResult();
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseSend.TrySetResult();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (connection.PendingDeviceCloseCount != 0)
                await Task.Delay(10, timeout.Token);
            Assert.Equal(2, Volatile.Read(ref connections));
            Assert.Equal(RelayConnectionState.Connected, connection.State);
        }
        finally
        {
            releaseSend.TrySetResult();
            failReceive.TrySetResult();
        }
    }

    [Fact]
    public async Task StalledSharedSendAbortsItsSocketAndReconnects()
    {
        using var relay = CreateRelayServer();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0;
        await using var connection = new RelayHostConnection(
            new RelayEndpointDescriptor("test", new Uri("https://relay.test"), new Uri("wss://relay.test"), true),
            RelayRoutingIdentity.CreateEphemeral(), (_, _, _, _) => Task.CompletedTask, NullAppLog.Instance,
            connectSocket: async (uri, cancellationToken) =>
            {
                var socket = await relay.GetTestServer().CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
                return Interlocked.Increment(ref connections) == 1
                    ? new BlockingSendSocket(socket, sendStarted, releaseSend)
                    : socket;
            });
        connection.StateChanged += (_, args) =>
        {
            if (args.State != RelayConnectionState.Connected) return;
            connected.TrySetResult();
            if (Volatile.Read(ref connections) > 1) recovered.TrySetResult();
        };
        connection.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connection.ProcessRelayEnvelope(new RelayEnvelope(RelayEnvelopeKind.Text, Guid.NewGuid(), [1]), CancellationToken.None);
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RelayConnectionState.Connected, connection.State);
        Assert.Equal(2, Volatile.Read(ref connections));
    }

    private static IHost CreateRelayServer() => new HostBuilder().ConfigureWebHost(web => web.UseTestServer().Configure(app =>
    {
        app.UseWebSockets();
        app.Run(async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[2048];
            try
            {
                var hello = await socket.ReceiveAsync(buffer, context.RequestAborted);
                using var helloJson = JsonDocument.Parse(buffer.AsMemory(0, hello.Count));
                Assert.Equal("relay.host.hello", helloJson.RootElement.GetProperty("type").GetString());
                await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    type = "relay.host.challenge",
                    challenge = new string('a', 43)
                })), WebSocketMessageType.Text, true, context.RequestAborted);
                var proof = await socket.ReceiveAsync(buffer, context.RequestAborted);
                using var proofJson = JsonDocument.Parse(buffer.AsMemory(0, proof.Count));
                Assert.Equal("relay.host.proof", proofJson.RootElement.GetProperty("type").GetString());
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"relay.host.accepted\"}"),
                    WebSocketMessageType.Text, true, context.RequestAborted);
                while (socket.State == WebSocketState.Open)
                {
                    if ((await socket.ReceiveAsync(buffer, context.RequestAborted)).MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
            }
        });
    })).Start();

    private sealed class CancelledReceiveSocket(
        WebSocket socket, TaskCompletionSource reading, TaskCompletionSource fault,
        string failure = "cancel", int failRead = 3) : WebSocket
    {
        private int _readCount;
        public override WebSocketCloseStatus? CloseStatus => socket.CloseStatus;
        public override string? CloseStatusDescription => socket.CloseStatusDescription;
        public override string? SubProtocol => socket.SubProtocol;
        public override WebSocketState State => socket.State;
        public override void Abort() => socket.Abort();
        public override void Dispose() => socket.Dispose();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _readCount) == failRead)
            {
                reading.TrySetResult();
                await fault.Task.WaitAsync(cancellationToken);
                socket.Abort();
                throw failure switch
                {
                    "disposed" => new ObjectDisposedException(nameof(socket)),
                    "io" => new IOException("Simulated receive failure."),
                    _ => new OperationCanceledException("Simulated transport cancellation without application shutdown.")
                };
            }
            return await socket.ReceiveAsync(buffer, cancellationToken);
        }
    }

    private sealed class BlockingSendSocket(WebSocket socket,
        TaskCompletionSource<CancellationToken> sendStarted, TaskCompletionSource releaseSend,
        bool failOnRelease = false) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => socket.CloseStatus;
        public override string? CloseStatusDescription => socket.CloseStatusDescription;
        public override string? SubProtocol => socket.SubProtocol;
        public override WebSocketState State => socket.State;
        public override void Abort() => socket.Abort();
        public override void Dispose() => socket.Dispose();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            socket.ReceiveAsync(buffer, cancellationToken);
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (messageType == WebSocketMessageType.Binary)
            {
                sendStarted.TrySetResult(cancellationToken);
                await releaseSend.Task.WaitAsync(cancellationToken);
                if (failOnRelease) throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            }
            await socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
        }
    }
}
