using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPairingTests : WebHostServiceTestBase
{
    [Fact]
    public async Task WebSocketAuthenticatesHostIdentityBeforeAcceptingFreshPairing()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        var clientNonce = PairingBootstrapCrypto.CreateNonce();

        var challenge = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairTokenId = PairingTokenAuthority.CreateTokenId(token),
            clientNonce,
            reconnectPublicKey = key.PublicKey
        });

        Assert.Equal("pair.bootstrap.challenge", challenge.GetProperty("type").GetString());
        Assert.Equal(clientNonce, challenge.GetProperty("clientNonce").GetString());
        var serverNonce = challenge.GetProperty("serverNonce").GetString()!;
        var identity = challenge.GetProperty("hostIdentity");
        Assert.Equal(fixture.Manager.HostIdentity.PublicKey, identity.GetProperty("publicKey").GetString());
        Assert.Equal(fixture.Manager.HostIdentity.Fingerprint, identity.GetProperty("fingerprint").GetString());
        Assert.True(PairingBootstrapCrypto.ProofsMatch(
            PairingBootstrapCrypto.CreateHostProof(
                token,
                clientId,
                clientNonce,
                serverNonce,
                key.PublicKey,
                fixture.Manager.HostIdentity.PublicKey,
                fixture.Manager.HostIdentity.Fingerprint),
            challenge.GetProperty("proof").GetString()!));

        var accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.bootstrap.proof",
            clientId,
            proof = PairingBootstrapCrypto.CreateClientProof(
                token,
                clientId,
                clientNonce,
                serverNonce,
                key.PublicKey,
                fixture.Manager.HostIdentity.PublicKey,
                fixture.Manager.HostIdentity.Fingerprint)
        });

        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
        Assert.True(fixture.Manager.HasCurrentHostIdentity(clientId));
    }

    [Fact]
    public async Task WebSocketRejectsTamperedFreshPairingProof()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();

        var challenge = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairTokenId = PairingTokenAuthority.CreateTokenId(token),
            clientNonce = PairingBootstrapCrypto.CreateNonce(),
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });
        Assert.Equal("pair.bootstrap.challenge", challenge.GetProperty("type").GetString());

        var rejected = await SendAndReceiveAsync(socket, new
        {
            type = "pair.bootstrap.proof",
            clientId,
            proof = new string('A', 43)
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("invalid-proof", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketRejectsMalformedPairHelloAsInvalidMessage()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var token = fixture.Manager.CreatePairingToken();

        var rejected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            deviceName = "Phone",
            pairToken = token,
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("invalid-message", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketRejectsUnknownReconnectIdentityAsRevoked()
    {
        await using var fixture = await WebHostFixture.StartAsync();

        var rejected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone"
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("device-revoked", rejected.GetProperty("reason").GetString());
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
            pairToken = "wrong-token",
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
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
                pairToken = "wrong-token",
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
        }

        Assert.Equal("pair.rejected", response.GetProperty("type").GetString());
        Assert.Equal("rate-limited", response.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketAllowsValidReconnectWhileFailedPairingAttemptsAreRateLimited()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        var paired = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token,
            reconnectPublicKey = key.PublicKey
        });

        using var failedSocket = await ConnectAsync(fixture.WebHost);
        for (var attempt = 0; attempt < PairingAttemptRateLimiter.DefaultMaxFailures; attempt++)
        {
            await SendAndReceiveAsync(failedSocket, new
            {
                type = "pair.hello",
                clientId = $"failed-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = "wrong-token",
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
        }

        var reconnected = await SendReconnectAsync(fixture.WebHost, clientId, "Phone", key);

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("pair.accepted", reconnected.GetProperty("type").GetString());
    }
}
