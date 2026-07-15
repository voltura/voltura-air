using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostTextTransferTests : WebHostServiceTestBase
{
    [Fact]
    public async Task FetchesBoundedClipboardTextOnlyForAnExplicitlyAllowedDevice()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var clipboard = new FakeClipboardTextReader(new ClipboardTextReadResult(true, "Copied from PC", null, "Text fetched from the PC clipboard."));

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowClipboardRead = true });
            await using var fixture = await WebHostFixture.StartAsync(clipboardTextReader: clipboard);
            using var socket = await ConnectAsync(fixture.WebHost);
            var paired = await SendAndReceiveAsync(socket, new { type = "pair.hello", clientId = $"client-{Guid.NewGuid():N}", deviceName = "Phone", pairToken = fixture.Manager.CreatePairingToken() });
            var result = await SendAndReceiveAsync(socket, new { type = "clipboard.get", operationId = "clipboard-read" });

            Assert.True(paired.GetProperty("capabilities").GetProperty("clipboardRead").GetBoolean());
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("Copied from PC", result.GetProperty("text").GetString());
            Assert.Equal(1, clipboard.ReadCount);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task RejectsClipboardReadWhenHostPermissionIsOffWithoutReadingClipboard()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var clipboard = new FakeClipboardTextReader(new ClipboardTextReadResult(true, "Private text", null, "Text fetched from the PC clipboard."));

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowClipboardRead = false });
            await using var fixture = await WebHostFixture.StartAsync(clipboardTextReader: clipboard);
            using var socket = await ConnectAsync(fixture.WebHost);
            var paired = await SendAndReceiveAsync(socket, new { type = "pair.hello", clientId = $"client-{Guid.NewGuid():N}", deviceName = "Phone", pairToken = fixture.Manager.CreatePairingToken() });
            var result = await SendAndReceiveAsync(socket, new { type = "clipboard.get", operationId = "clipboard-blocked" });

            Assert.False(paired.GetProperty("capabilities").GetProperty("clipboardRead").GetBoolean());
            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("VAIR-CLIPBOARD-PERMISSION-DENIED", result.GetProperty("code").GetString());
            Assert.Equal(0, clipboard.ReadCount);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReportsConfiguredClipboardFallbackWithoutExposingHostDetails()
    {
        var service = new FakeTextDestinationService(
            new TextDestinationMetadata("configured", "Windows Notepad", true),
            new TextDeliveryResult(true, "clipboard", null, "Text was copied to the Windows clipboard. Paste it into Windows Notepad manually."));
        await using var fixture = await WebHostFixture.StartAsync(textDestinationService: service);
        using var socket = await ConnectAsync(fixture.WebHost);
        var paired = await SendAndReceiveAsync(socket, new { type = "pair.hello", clientId = $"client-{Guid.NewGuid():N}", deviceName = "Phone", pairToken = fixture.Manager.CreatePairingToken() });
        var result = await SendAndReceiveAsync(socket, new { type = "text.send", operationId = "delivery", text = "Hello", sendEnter = false });

        var target = paired.GetProperty("host").GetProperty("textTransferTarget");
        Assert.Equal("configured", target.GetProperty("mode").GetString());
        Assert.Equal("Windows Notepad", target.GetProperty("displayName").GetString());
        Assert.False(target.TryGetProperty("path", out _));
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("clipboard", result.GetProperty("deliveryKind").GetString());
        Assert.Single(service.Deliveries);
    }

    [Fact]
    public async Task AdvertisesFocusedTargetAndAcknowledgesCompleteTextDelivery()
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
        var operationId = Guid.NewGuid().ToString();
        var result = await SendAndReceiveAsync(socket, new
        {
            type = "text.send",
            operationId,
            text = "Hello from phone",
            sendEnter = true
        });

        Assert.True(paired.GetProperty("capabilities").GetProperty("textTransfer").GetBoolean());
        var target = paired.GetProperty("host").GetProperty("textTransferTarget");
        Assert.Equal("focused", target.GetProperty("mode").GetString());
        Assert.Equal("Currently focused application", target.GetProperty("displayName").GetString());
        Assert.True(target.GetProperty("available").GetBoolean());
        Assert.Equal("text.send.result", result.GetProperty("type").GetString());
        Assert.Equal(operationId, result.GetProperty("operationId").GetString());
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal(["TypeText:Hello from phone", "SpecialKey:Enter:"], fixture.InputInjector.Events);
    }

    [Fact]
    public async Task PreservesMultilineTextBeforeAcknowledgingDelivery()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        var result = await SendAndReceiveAsync(socket, new
        {
            type = "text.send",
            operationId = Guid.NewGuid().ToString(),
            text = "First line\r\nSecond line\nThird line",
            sendEnter = false
        });

        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal(
            [
                "TypeText:First line",
                "SpecialKey:Enter:",
                "TypeText:Second line",
                "SpecialKey:Enter:",
                "TypeText:Third line"
            ],
            fixture.InputInjector.Events);
    }

    [Fact]
    public async Task ReportsPartialNativeFailureWithoutSendingEnterAndKeepsSocketOpen()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        fixture.InputInjector.Failures.Enqueue(new InputDispatchException(
            "Windows rejected input.",
            "keyboard.text",
            requestedCount: 10,
            acceptedCount: 4,
            win32Error: 5));
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        var failed = await SendAndReceiveAsync(socket, new
        {
            type = "text.send",
            operationId = Guid.NewGuid().ToString(),
            text = "Incomplete",
            sendEnter = true
        });
        var retried = await SendAndReceiveAsync(socket, new
        {
            type = "text.send",
            operationId = Guid.NewGuid().ToString(),
            text = "Retry",
            sendEnter = false
        });

        Assert.False(failed.GetProperty("succeeded").GetBoolean());
        Assert.Equal("VAIR-TEXT-NATIVE-SEND-FAILED", failed.GetProperty("code").GetString());
        Assert.True(retried.GetProperty("succeeded").GetBoolean());
        Assert.Equal(["TypeText:Retry"], fixture.InputInjector.Events);
        Assert.Equal(WebSocketState.Open, socket.State);
    }
}
