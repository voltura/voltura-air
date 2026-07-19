using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPairingTests : WebHostServiceTestBase
{
    [Fact]
    public async Task WebSocketRejectsMalformedPairHelloAsInvalidMessage()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var token = fixture.Manager.CreatePairingToken();

        var rejected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            deviceName = "Phone",
            pairToken = token
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("invalid-message", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketRejectsPairHelloWithoutTokenOrSecretAsMissingToken()
    {
        await using var fixture = await WebHostFixture.StartAsync();

        var rejected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone"
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("missing-token", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RejectedPairingMessagesDoNotExtendTheHandshakeDeadline()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);

        var rejected = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = "wrong-token"
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await ReceiveCloseStatusAsync(socket, timeout.Token));
    }

    [Fact]
    public async Task WebSocketRateLimitsRepeatedFailedPairingAttempts()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);

        JsonElement response = default;
        for (var attempt = 0; attempt < PairingAttemptRateLimiter.DefaultMaxFailures + 1; attempt++)
        {
            response = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = "wrong-token"
            });
        }

        Assert.Equal("pair.rejected", response.GetProperty("type").GetString());
        Assert.Equal("rate-limited", response.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketAllowsValidReconnectWhileFailedPairingAttemptsAreRateLimited()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        var paired = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        var secret = paired.GetProperty("secret").GetString();

        using var failedSocket = await ConnectAsync(fixture.WebHost);
        for (var attempt = 0; attempt < PairingAttemptRateLimiter.DefaultMaxFailures; attempt++)
        {
            await SendAndReceiveAsync(failedSocket, new
            {
                type = "pair.hello",
                clientId = $"failed-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = "wrong-token"
            });
        }

        var reconnected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            secret
        });

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("pair.accepted", reconnected.GetProperty("type").GetString());
    }
}
