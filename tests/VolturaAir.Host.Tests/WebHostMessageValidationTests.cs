using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostMessageValidationTests : WebHostServiceTestBase
{
    [Fact]
    public async Task WebSocketClosesOversizedFragmentedMessageBeforeParsing()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);
        var fragment = new byte[WebHostService.MaxWebSocketMessageBytes / 2];
        Array.Fill(fragment, (byte)'a');

        await socket.SendAsync(fragment, WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await socket.SendAsync(fragment, WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await socket.SendAsync(new byte[] { (byte)'a' }, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        var closeStatus = await ReceiveCloseStatusAsync(socket);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, closeStatus);
    }

    [Fact]
    public async Task WebSocketRejectsBinaryMessages()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);

        await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);

        var closeStatus = await ReceiveCloseStatusAsync(socket);

        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, closeStatus);
    }

    [Fact]
    public async Task WebSocketClosesUnknownAuthenticatedMessagesWithoutDispatchingInput()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });

        await SendAsync(socket, new { type = "pointer.teleport", dx = 10, dy = 10 });
        var closeStatus = await ReceiveCloseStatusAsync(socket);

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
        Assert.Empty(fixture.InputInjector.Events);
    }

    [Fact]
    public async Task WebSocketClosesMalformedAuthenticatedMessagesWithoutDispatchingInput()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var closeEvent = new TaskCompletionSource<ControllerSocketClosedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);
        fixture.WebHost.ControllerSocketClosed += (_, args) => closeEvent.TrySetResult(args);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });

        await SendAsync(socket, new { type = "pointer.move", dx = "not-a-number", dy = 10 });
        var closeStatus = await ReceiveCloseStatusAsync(socket);
        var closeNotification = await closeEvent.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
        Assert.Equal(clientId, closeNotification.ClientId);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeNotification.Status);
        Assert.Equal("Invalid message", closeNotification.Reason);
        Assert.Empty(fixture.InputInjector.Events);
    }

    [Theory]
    [InlineData("system.power", null, false)]
    [InlineData("awake.set", null, false)]
    [InlineData("system.power", "bad/id", true)]
    [InlineData("awake.set", "bad/id", true)]
    [InlineData("system.power", "too-long", true)]
    [InlineData("awake.set", "too-long", true)]
    public async Task WebSocketRejectsMissingOrMalformedPowerOperationIds(
        string type,
        string? operationId,
        bool includeOperationId)
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);
        await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken()
        });
        var resolvedOperationId = operationId == "too-long" ? new string('a', 65) : operationId;
        object payload = (type, includeOperationId) switch
        {
            ("system.power", true) => new { type, operationId = resolvedOperationId, action = "lock" },
            ("system.power", false) => new { type, action = "lock" },
            ("awake.set", true) => new { type, operationId = resolvedOperationId, enabled = true },
            _ => new { type, enabled = true }
        };

        await SendAsync(socket, payload);
        var closeStatus = await ReceiveCloseStatusAsync(socket);

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
        Assert.Empty(fixture.InputInjector.Events);
    }

    [Fact]
    public async Task WebSocketKeepsAuthenticatedSocketOpenAfterRecoverableInputFailure()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        fixture.InputInjector.Failures.Enqueue(new InputDispatchException(
            "Windows did not accept all input events.",
            "keyboard.special",
            requestedCount: 4,
            acceptedCount: 2,
            win32Error: 5,
            cleanupAttempted: true,
            cleanupSucceeded: true));
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        var inputError = await SendAndReceiveAsync(socket, new { type = "keyboard.special", key = "Tab", modifiers = new[] { "Alt" }, seq = 7 });
        var inputAck = await SendAndReceiveAsync(socket, new { type = "pointer.move", dx = 3.7, dy = -2.2, seq = 8 });

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("input.error", inputError.GetProperty("type").GetString());
        Assert.Equal(7, inputError.GetProperty("seq").GetInt64());
        Assert.Equal("VAIR-INPUT-NATIVE-SEND-FAILED", inputError.GetProperty("code").GetString());
        Assert.Equal("input.ack", inputAck.GetProperty("type").GetString());
        Assert.Equal(8, inputAck.GetProperty("seq").GetInt64());
        Assert.Equal(WebSocketState.Open, socket.State);
        Assert.Equal(new[] { "MoveMouse:4:-2" }, fixture.InputInjector.Events);
    }
}
