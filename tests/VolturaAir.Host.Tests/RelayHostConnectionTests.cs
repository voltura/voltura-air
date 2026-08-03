using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class RelayHostConnectionTests
{
    public static TheoryData<string> MalformedTurnResponses => new()
    {
        "[]",
        ValidTurnResponse.Replace("\"allowed\": true", "\"allowed\": \"true\"", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"expiresAt\": \"2026-08-04T20:00:00Z\"", "\"expiresAt\": true", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"iceServers\": [{", "\"iceServers\": [true, {", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"username\": \"user\"", "\"username\": 5", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"credential\": \"secret\"", "\"credential\": false", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"urls\": [\"turns:turn.example.com:443?transport=tcp\"]", "\"urls\": [false]", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"forcedQuality\": null", "\"forcedQuality\": {}", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"usageBytes\": 0", "\"usageBytes\": \"0\"", StringComparison.Ordinal),
        ValidTurnResponse.Replace("\"checkedAt\": \"2026-08-03T20:00:00Z\"", "\"checkedAt\": 7", StringComparison.Ordinal)
    };

    [Theory]
    [MemberData(nameof(MalformedTurnResponses))]
    public async Task MalformedTurnValueKindsReturnUnavailableWithoutThrowing(string json)
    {
        await using var connection = CreateConnection();
        using var document = JsonDocument.Parse(json);

        var configuration = connection.ParseTurnConfiguration(document.RootElement, RelayScreenQuality.Standard);

        Assert.Null(configuration);
    }

    [Fact]
    public async Task ValidCustomTurnConfigurationStillParses()
    {
        await using var connection = CreateConnection();
        using var document = JsonDocument.Parse(ValidTurnResponse);

        var configuration = Assert.IsType<RelayTurnConfiguration>(
            connection.ParseTurnConfiguration(document.RootElement, RelayScreenQuality.Standard));

        Assert.Equal(RelayScreenQuality.Standard, configuration.EffectiveQuality);
        Assert.Single(configuration.IceServers);
    }

    [Fact]
    public async Task RejectsTurnResponsesAboveTheBoundBeforeParsing()
    {
        using var content = new ByteArrayContent(new byte[(64 * 1024) + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RelayHostConnection.ReadBoundedJsonAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task ReassemblesAFragmentedBinaryRelayEnvelope()
    {
        var expected = new RelayEnvelope(RelayEnvelopeKind.Binary, Guid.NewGuid(), Enumerable.Range(0, 512).Select(value => (byte)value).ToArray());
        var encoded = expected.Encode();
        using var socket = new FragmentedWebSocket(
            new Frame(encoded[..173], WebSocketMessageType.Binary, EndOfMessage: false),
            new Frame(encoded[173..], WebSocketMessageType.Binary, EndOfMessage: true));

        var actual = await RelayHostConnection.ReceiveRelayEnvelopeAsync(
            socket,
            new byte[RelayEnvelope.MaximumEncodedBytes],
            CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.Payload, actual.Payload);
    }

    [Fact]
    public async Task RejectsFragmentedRelayMessagesAboveTheExistingBound()
    {
        using var socket = new FragmentedWebSocket(
            new Frame(new byte[40_000], WebSocketMessageType.Binary, EndOfMessage: false),
            new Frame(new byte[30_000], WebSocketMessageType.Binary, EndOfMessage: true));

        await Assert.ThrowsAsync<InvalidDataException>(() => RelayHostConnection.ReceiveRelayEnvelopeAsync(
            socket,
            new byte[RelayEnvelope.MaximumEncodedBytes],
            CancellationToken.None));
    }

    [Fact]
    public void RelayEnvelopeAllowsEncryptionOverheadButNothingMore()
    {
        var maximum = new RelayEnvelope(
            RelayEnvelopeKind.Binary,
            Guid.NewGuid(),
            new byte[RelayEnvelope.MaximumPayloadBytes]);

        var encoded = maximum.Encode();

        Assert.Equal(RelayEnvelope.MaximumEncodedBytes, encoded.Length);
        Assert.True(RelayEnvelope.TryDecode(encoded, out var decoded));
        Assert.Equal(RelayEnvelope.MaximumPayloadBytes, decoded!.Payload.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelayEnvelope(
            RelayEnvelopeKind.Binary,
            Guid.NewGuid(),
            new byte[RelayEnvelope.MaximumPayloadBytes + 1]).Encode());
    }

    [Fact]
    public async Task SessionCreationFailureClosesTheRelayDevice()
    {
        await using var connection = CreateConnection();
        var sessionId = Guid.NewGuid();

        connection.ProcessRelayEnvelope(
            new RelayEnvelope(RelayEnvelopeKind.Connected, sessionId, new byte[15]),
            CancellationToken.None);

        Assert.Equal(1, connection.PendingDeviceCloseCount);
    }

    [Fact]
    public async Task UnknownDeviceDeliveryClosesTheRelayDevice()
    {
        await using var connection = CreateConnection();
        var sessionId = Guid.NewGuid();

        connection.ProcessRelayEnvelope(
            new RelayEnvelope(RelayEnvelopeKind.Text, sessionId, [1]),
            CancellationToken.None);

        Assert.Equal(1, connection.PendingDeviceCloseCount);
    }

    private static RelayHostConnection CreateConnection() => new(
        new RelayEndpointDescriptor(
            "custom-v1",
            new Uri("https://relay.example.com"),
            new Uri("wss://relay.example.com"),
            SupportsTurn: true),
        RelayRoutingIdentity.CreateEphemeral(),
        (_, _, _) => Task.CompletedTask,
        NullAppLog.Instance);

    private const string ValidTurnResponse = """
        {
          "allowed": true,
          "forcedQuality": null,
          "usageBytes": 0,
          "checkedAt": "2026-08-03T20:00:00Z",
          "expiresAt": "2026-08-04T20:00:00Z",
          "iceServers": [{
            "urls": ["turns:turn.example.com:443?transport=tcp"],
            "username": "user",
            "credential": "secret"
          }]
        }
        """;

    private sealed record Frame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage);

    private sealed class FragmentedWebSocket(params Frame[] frames) : WebSocket
    {
        private readonly Queue<Frame> _frames = new(frames);
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            var frame = _frames.Dequeue();
            Assert.True(frame.Payload.Length <= buffer.Count);
            frame.Payload.AsSpan().CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(frame.Payload.Length, frame.MessageType, frame.EndOfMessage));
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
