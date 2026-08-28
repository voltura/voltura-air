using System.Net.WebSockets;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostAiAssistantTests : WebHostServiceTestBase
{
    [Fact]
    public async Task AuthenticatedOwnerCanOpenAskReceiveAnswerResetAndClose()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new { type = "status.get" }, "status");

        JsonElement opened = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-1",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-1"))
        }, "ai.assistant.open.result");
        Assert.True(opened.GetProperty("succeeded").GetBoolean());
        JsonElement snapshot = await ReceiveUntilTypeAsync(socket, "ai.assistant.message");
        Assert.Equal("Previous answer", snapshot.GetProperty("text").GetString());
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        const string question = "How does Relay work?";
        JsonElement asked = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-ask-1",
            question,
            clientSignature = key.SignPayload(AiAssistantProtocol.AskTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-ask-1", question))
        }, "ai.assistant.ask.result");
        Assert.True(asked.GetProperty("succeeded").GetBoolean());
        Assert.Equal(question, factory.Client.LastQuestion);
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        factory.Client.CompleteAnswer("A bounded Relay answer.");
        JsonElement answer = await ReceiveUntilTypeAsync(socket, "ai.assistant.message");
        Assert.Equal("assistant", answer.GetProperty("sender").GetString());
        Assert.Equal("A bounded Relay answer.", answer.GetProperty("text").GetString());
        JsonElement ready = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");
        Assert.Equal("ready", ready.GetProperty("state").GetString());

        JsonElement reset = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.reset",
            operationId = "assistant-reset-1",
            clientSignature = key.SignPayload(AiAssistantProtocol.ResetTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-reset-1"))
        }, "ai.assistant.reset.result");
        Assert.True(reset.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, factory.Client.StartCount);

        JsonElement closed = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.close",
            operationId = "assistant-close-1"
        }, "ai.assistant.close.result");
        Assert.True(closed.GetProperty("succeeded").GetBoolean());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task InvalidProofAndWrongProfileNeverConnectToCodex()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        JsonElement invalid = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-invalid",
            clientSignature = key.SignPayload("wrong transcript")
        }, "ai.assistant.open.result");
        Assert.False(invalid.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, factory.ConnectCount);

        Assert.True(fixture.Manager.SetDeviceAccessProfile("client-assistant", DeviceAccessProfile.RemoteControls));
        _ = await ReceiveUntilTypeAsync(socket, "status");
        JsonElement denied = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-denied",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-denied"))
        }, "ai.assistant.open.result");
        Assert.False(denied.GetProperty("succeeded").GetBoolean());
        Assert.Equal("unavailable", denied.GetProperty("code").GetString());
        Assert.Equal(0, factory.ConnectCount);
    }

    [Fact]
    public async Task MissingCodexInstallationIsNotAdvertisedOrOpened()
    {
        var factory = new FakeAssistantClientFactory
        {
            Available = false
        };
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        JsonElement paired = await PairAsync(socket, fixture.Manager, key);
        JsonElement capability = paired.GetProperty("capabilities").GetProperty("aiAssistant");
        Assert.False(capability.GetProperty("available").GetBoolean());
        Assert.False(capability.GetProperty("canUse").GetBoolean());

        JsonElement rejected = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-codex-missing",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-codex-missing"))
        }, "ai.assistant.open.result");

        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("unavailable", rejected.GetProperty("code").GetString());
        Assert.Contains("Install Codex", rejected.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.ConnectCount);
    }

    [Fact]
    public async Task CapabilityReadsCurrentCodexInstallationState()
    {
        var factory = new FakeAssistantClientFactory
        {
            Available = false
        };
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        factory.Available = true;
        await SendAsync(socket, new { type = "status.get" });
        await ReceiveUntilAssistantAvailabilityAsync(socket, expected: true);

        factory.Available = false;
        JsonElement rejected = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-after-uninstall",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-after-uninstall"))
        }, "ai.assistant.open.result");
        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("unavailable", rejected.GetProperty("code").GetString());
        Assert.Equal(0, factory.ConnectCount);

        await SendAsync(socket, new { type = "status.get" });
        await ReceiveUntilAssistantAvailabilityAsync(socket, expected: false);
    }

    [Fact]
    public async Task ProfileRevocationTerminatesAndDisposesAStalledOpenSession()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.BlockReadUntilCancelled = true;
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        await SendAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-session-revoked",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-session-revoked"))
        });
        await factory.Client.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(fixture.Manager.SetDeviceAccessProfile("client-assistant", DeviceAccessProfile.Custom));
        JsonElement rejected = await ReceiveUntilOperationAsync(
            socket, "ai.assistant.open.result", "assistant-open-session-revoked");

        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("permission-denied", rejected.GetProperty("code").GetString());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task DuplicateCloseKeepsClosingGuardWhenControlSlotIsFull()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.BlockReadUntilReleased = true;
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        await SendAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-stalled",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-stalled"))
        });
        await factory.Client.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        for (int index = 0; index < 32; index++)
        {
            await SendAsync(socket, new
            {
                type = "ai.assistant.ask",
                operationId = $"assistant-queued-{index}",
                question = "queued",
                clientSignature = "invalid"
            });
        }

        await SendAsync(socket, new
        {
            type = "ai.assistant.close",
            operationId = "assistant-close-stalled"
        });
        await SendAsync(socket, new
        {
            type = "ai.assistant.close",
            operationId = "assistant-close-stalled-duplicate"
        });

        JsonElement duplicate = await ReceiveUntilOperationAsync(
            socket, "ai.assistant.close.result", "assistant-close-stalled-duplicate");
        Assert.False(duplicate.GetProperty("succeeded").GetBoolean());
        Assert.Equal("busy", duplicate.GetProperty("code").GetString());

        factory.Client.ReleaseRead.TrySetResult();
        JsonElement closed = await ReceiveUntilOperationAsync(
            socket, "ai.assistant.close.result", "assistant-close-stalled");
        Assert.True(closed.GetProperty("succeeded").GetBoolean());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task CloseCancelsAStalledOpenBeforeItsRpcTimeout()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.BlockReadUntilCancelled = true;
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        await SendAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-cancelled",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-cancelled"))
        });
        await factory.Client.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        JsonElement closed = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.close",
            operationId = "assistant-close-cancelled"
        }, "ai.assistant.close.result");

        Assert.True(closed.GetProperty("succeeded").GetBoolean());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task DisconnectedQueuedOpenCannotCreateAnOrphanSession()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.BlockReadUntilCancelled = true;
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var ownerKey = new PairingTestKey();
        using var disconnectedKey = new PairingTestKey();
        using WebSocket owner = await ConnectAsync(fixture.WebHost);
        using WebSocket disconnected = await ConnectAsync(fixture.WebHost);
        await PairAsync(owner, fixture.Manager, ownerKey, "client-owner");
        await PairAsync(disconnected, fixture.Manager, disconnectedKey, "client-disconnected");

        await SendAsync(owner, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-owner-stalled",
            clientSignature = ownerKey.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-owner", fixture.Manager.HostIdentity.PublicKey, "assistant-open-owner-stalled"))
        });
        await factory.Client.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        factory.Client.BlockReadUntilCancelled = false;

        await SendAsync(disconnected, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-disconnected-queued",
            clientSignature = disconnectedKey.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-disconnected", fixture.Manager.HostIdentity.PublicKey, "assistant-open-disconnected-queued"))
        });
        await disconnected.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await owner.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        using var replacementKey = new PairingTestKey();
        using WebSocket replacement = await ConnectAsync(fixture.WebHost);
        await PairAsync(replacement, fixture.Manager, replacementKey, "client-replacement");
        JsonElement opened = await SendUntilTypeAsync(replacement, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-replacement",
            clientSignature = replacementKey.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-replacement", fixture.Manager.HostIdentity.PublicKey, "assistant-open-replacement"))
        }, "ai.assistant.open.result");

        Assert.True(opened.GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, factory.ConnectCount);
    }

    [Fact]
    public async Task ReconnectingOwnerWaitsForPreviousSessionCleanup()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket original = await ConnectAsync(fixture.WebHost);
        await PairAsync(original, fixture.Manager, key);
        JsonElement firstOpen = await SendUntilTypeAsync(original, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-before-reconnect",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-before-reconnect"))
        }, "ai.assistant.open.result");
        Assert.True(firstOpen.GetProperty("succeeded").GetBoolean());

        factory.Client.BlockDisposeUntilReleased = true;
        await original.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
        await factory.Client.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using WebSocket replacement = await ConnectAsync(fixture.WebHost);
        JsonElement challenge = await SendAndReceiveAsync(replacement, new
        {
            type = "pair.hello",
            clientId = "client-assistant",
            deviceName = "Assistant test phone"
        });
        Assert.Equal("pair.challenge", challenge.GetProperty("type").GetString());
        JsonElement accepted = await SendAndReceiveAsync(replacement, new
        {
            type = "pair.proof",
            clientId = "client-assistant",
            signature = key.SignReconnectChallenge(
                "client-assistant", challenge.GetProperty("challenge").GetString()!)
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());

        await SendAsync(replacement, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-after-reconnect",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-after-reconnect"))
        });
        factory.Client.ReleaseDispose.TrySetResult();

        JsonElement reopened = await ReceiveUntilTypeAsync(replacement, "ai.assistant.open.result");
        Assert.True(reopened.GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, factory.ConnectCount);
    }

    [Fact]
    public async Task LiveProfileRevocationClosesAndDisposesTheAssistant()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-revoke",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-revoke"))
        }, "ai.assistant.open.result");

        Assert.True(fixture.Manager.SetDeviceAccessProfile("client-assistant", DeviceAccessProfile.Custom));
        JsonElement closed = await ReceiveUntilTypeAsync(socket, "ai.assistant.closed");
        Assert.Equal("closed", closed.GetProperty("reason").GetString());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task BufferedCompletionIsDeliveredAfterTheUserMessageAndWorkingState()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.CompleteOnReleaseText = "Fast answer";
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-fast",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-fast"))
        }, "ai.assistant.open.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        const string question = "Give me a fast answer";
        await SendAsync(socket, new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-ask-fast",
            question,
            clientSignature = key.SignPayload(AiAssistantProtocol.AskTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-ask-fast", question))
        });

        var observed = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (observed.Count < 5)
        {
            using JsonDocument document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            string? type = document.RootElement.GetProperty("type").GetString();
            if (type?.StartsWith("ai.assistant.", StringComparison.Ordinal) == true) observed.Add(type);
        }
        Assert.Equal(
            ["ai.assistant.message", "ai.assistant.ask.result", "ai.assistant.state", "ai.assistant.message", "ai.assistant.state"],
            observed);
    }

    [Fact]
    public async Task UncertainTurnStartClosesTheSessionBeforeAnotherQuestionCanRun()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.FailTurnStart = true;
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-uncertain",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-uncertain"))
        }, "ai.assistant.open.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        const string question = "Could this have started?";
        JsonElement failed = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-ask-uncertain",
            question,
            clientSignature = key.SignPayload(AiAssistantProtocol.AskTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-ask-uncertain", question))
        }, "ai.assistant.ask.result");
        Assert.Equal("turn-uncertain", failed.GetProperty("code").GetString());
        JsonElement closed = await ReceiveUntilTypeAsync(socket, "ai.assistant.closed");
        Assert.Equal("turn-uncertain", closed.GetProperty("reason").GetString());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task ConnectionCloseAtTurnStartCannotEmitSuccessOrWorkingState()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-before-connection-close",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-before-connection-close"))
        }, "ai.assistant.open.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");
        factory.Client.CloseOnStartTurn = true;

        await SendAsync(socket, new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-ask-connection-close",
            question = "Do not acknowledge this turn",
            clientSignature = key.SignPayload(AiAssistantProtocol.AskTranscript(
                "client-assistant",
                fixture.Manager.HostIdentity.PublicKey,
                "assistant-ask-connection-close",
                "Do not acknowledge this turn"))
        });

        JsonElement closed = await ReceiveUntilTypeAsync(socket, "ai.assistant.closed");
        Assert.Equal("codex-closed", closed.GetProperty("reason").GetString());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task AnotherPairedDeviceCannotTakeOverTheActiveAssistant()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var ownerKey = new PairingTestKey();
        using var otherKey = new PairingTestKey();
        using WebSocket owner = await ConnectAsync(fixture.WebHost);
        using WebSocket other = await ConnectAsync(fixture.WebHost);
        await PairAsync(owner, fixture.Manager, ownerKey, "client-assistant");
        await PairAsync(other, fixture.Manager, otherKey, "client-other");

        JsonElement opened = await SendUntilTypeAsync(owner, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-owner",
            clientSignature = ownerKey.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-owner"))
        }, "ai.assistant.open.result");
        Assert.True(opened.GetProperty("succeeded").GetBoolean());

        JsonElement rejected = await SendUntilTypeAsync(other, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-other",
            clientSignature = otherKey.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-other", fixture.Manager.HostIdentity.PublicKey, "assistant-open-other"))
        }, "ai.assistant.open.result");
        Assert.False(rejected.GetProperty("succeeded").GetBoolean());
        Assert.Equal("busy", rejected.GetProperty("code").GetString());
        Assert.Equal(1, factory.ConnectCount);
    }

    [Fact]
    public async Task OwnerCannotReopenAcrossAnActiveTurnAndTheLiveAnswerStillArrives()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-owner-working",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-owner-working"))
        }, "ai.assistant.open.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        const string question = "Keep this answer pending";
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-ask-owner-working",
            question,
            clientSignature = key.SignPayload(AiAssistantProtocol.AskTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-ask-owner-working", question))
        }, "ai.assistant.ask.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        JsonElement reopened = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-reopen-owner-working",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-reopen-owner-working"))
        }, "ai.assistant.open.result");
        Assert.False(reopened.GetProperty("succeeded").GetBoolean());
        Assert.Equal("busy", reopened.GetProperty("code").GetString());

        factory.Client.CompleteAnswer("The original answer is intact.");
        JsonElement answer = await ReceiveUntilTypeAsync(socket, "ai.assistant.message");
        Assert.Equal("The original answer is intact.", answer.GetProperty("text").GetString());
        JsonElement ready = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");
        Assert.Equal("ready", ready.GetProperty("state").GetString());
    }

    [Fact]
    public async Task CodexItemIdsAreMappedToBoundedWireIdsForSnapshotsAndLiveAnswers()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.SnapshotMessageId = "snapshot_id_that_is_not_a_wire_id";
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-normalized-id",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-normalized-id"))
        }, "ai.assistant.open.result");

        JsonElement snapshot = await ReceiveUntilTypeAsync(socket, "ai.assistant.message");
        Assert.Matches("^[A-F0-9]{64}$", snapshot.GetProperty("messageId").GetString());
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        const string question = "Normalize the next answer too";
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-ask-normalized-id",
            question,
            clientSignature = key.SignPayload(AiAssistantProtocol.AskTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-ask-normalized-id", question))
        }, "ai.assistant.ask.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");

        factory.Client.CompleteAnswer("Mapped answer", new string('x', 80) + "_invalid");
        JsonElement answer = await ReceiveUntilTypeAsync(socket, "ai.assistant.message");
        Assert.Matches("^[A-F0-9]{64}$", answer.GetProperty("messageId").GetString());
        Assert.Equal("Mapped answer", answer.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SnapshotFailureReturnsOnlyAFailedOpenAndClosesTheSession()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.FailRead = true;
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        JsonElement opened = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-read-failure",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-read-failure"))
        }, "ai.assistant.open.result");

        Assert.False(opened.GetProperty("succeeded").GetBoolean());
        Assert.Equal("codex-unavailable", opened.GetProperty("code").GetString());
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task LongSnapshotFailureStaysWithinTheMobileResultMessageLimit()
    {
        var factory = new FakeAssistantClientFactory();
        factory.Client.FailRead = true;
        factory.Client.ReadFailureMessage = new string('x', 300);
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);

        JsonElement failed = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-long-error",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-long-error"))
        }, "ai.assistant.open.result");

        string message = Assert.IsType<string>(failed.GetProperty("message").GetString());
        Assert.Equal(240, message.Length);
        Assert.EndsWith("…", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedReplacementLeavesTheExistingConversationNamedAndClosesTheSession()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-reset-failure",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-reset-failure"))
        }, "ai.assistant.open.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");
        factory.Client.FailStartAssistant = true;

        JsonElement reset = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.reset",
            operationId = "assistant-reset-failure",
            clientSignature = key.SignPayload(AiAssistantProtocol.ResetTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-reset-failure"))
        }, "ai.assistant.reset.result");

        Assert.False(reset.GetProperty("succeeded").GetBoolean());
        Assert.Equal("reset-uncertain", reset.GetProperty("code").GetString());
        Assert.Equal(0, factory.Client.RenameCount);
        await factory.Client.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(factory.Client.Disposed);
    }

    [Fact]
    public async Task OldConversationRenameFailureReturnsUncertainAndClosesTheSession()
    {
        var factory = new FakeAssistantClientFactory();
        await using var fixture = await WebHostFixture.StartAsync(aiAssistantClientFactory: factory);
        using var key = new PairingTestKey();
        using WebSocket socket = await ConnectAsync(fixture.WebHost);
        await PairAsync(socket, fixture.Manager, key);
        _ = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.open",
            operationId = "assistant-open-rename-failure",
            clientSignature = key.SignPayload(AiAssistantProtocol.OpenTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-open-rename-failure"))
        }, "ai.assistant.open.result");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.snapshot.complete");
        _ = await ReceiveUntilTypeAsync(socket, "ai.assistant.state");
        factory.Client.FailRename = true;

        JsonElement reset = await SendUntilTypeAsync(socket, new
        {
            type = "ai.assistant.reset",
            operationId = "assistant-reset-rename-failure",
            clientSignature = key.SignPayload(AiAssistantProtocol.ResetTranscript(
                "client-assistant", fixture.Manager.HostIdentity.PublicKey, "assistant-reset-rename-failure"))
        }, "ai.assistant.reset.result");

        Assert.False(reset.GetProperty("succeeded").GetBoolean());
        Assert.Equal("reset-uncertain", reset.GetProperty("code").GetString());
        Assert.Equal(1, factory.Client.StartCount);
        Assert.Equal(1, factory.Client.RenameCount);
        await factory.Client.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(factory.Client.Disposed);
    }

    private static async Task<JsonElement> PairAsync(WebSocket socket, PairingManager manager, PairingTestKey key, string clientId = "client-assistant")
    {
        JsonElement accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Assistant test phone",
            pairToken = manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
        return accepted;
    }

    private static async Task<JsonElement> SendUntilTypeAsync(WebSocket socket, object payload, string expectedType)
    {
        await SendAsync(socket, payload);
        return await ReceiveUntilTypeAsync(socket, expectedType);
    }

    private static async Task<JsonElement> ReceiveUntilTypeAsync(WebSocket socket, string expectedType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        for (int attempt = 0; attempt < 64; attempt++)
        {
            using JsonDocument document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            if (document.RootElement.GetProperty("type").GetString() == expectedType) return document.RootElement.Clone();
        }
        throw new InvalidOperationException($"The host did not send {expectedType}.");
    }

    private static async Task<JsonElement> ReceiveUntilOperationAsync(WebSocket socket, string expectedType, string operationId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        for (int attempt = 0; attempt < 96; attempt++)
        {
            using JsonDocument document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            JsonElement root = document.RootElement;
            if (root.GetProperty("type").GetString() == expectedType &&
                root.GetProperty("operationId").GetString() == operationId)
                return root.Clone();
        }
        throw new InvalidOperationException($"The host did not send {expectedType} for {operationId}.");
    }

    private static async Task ReceiveUntilAssistantAvailabilityAsync(WebSocket socket, bool expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        for (int attempt = 0; attempt < 16; attempt++)
        {
            using JsonDocument document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            JsonElement root = document.RootElement;
            if (root.GetProperty("type").GetString() == "status" &&
                root.GetProperty("capabilities").GetProperty("aiAssistant").GetProperty("available").GetBoolean() == expected)
                return;
        }
        throw new InvalidOperationException($"The host did not advertise AI Assistant availability {expected}.");
    }

    private sealed class FakeAssistantClientFactory : IAiAssistantClientFactory
    {
        internal FakeAssistantClient Client { get; } = new();
        internal int ConnectCount { get; private set; }
        internal bool Available { get; set; } = true;
        public bool IsAvailable => Available;
        public Task<IAiAssistantClient> ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            return Task.FromResult<IAiAssistantClient>(Client);
        }
    }

    private sealed class FakeAssistantClient : IAiAssistantClient
    {
        private string _threadId = "assistant-thread-1";
        private string? _turnId;
        internal string? LastQuestion { get; private set; }
        internal int StartCount { get; private set; }
        internal bool Disposed { get; private set; }
        internal bool FailTurnStart { get; set; }
        internal bool CloseOnStartTurn { get; set; }
        internal bool FailRead { get; set; }
        internal string ReadFailureMessage { get; set; } = "The transcript could not be read.";
        internal bool FailRename { get; set; }
        internal bool FailStartAssistant { get; set; }
        internal bool BlockReadUntilCancelled { get; set; }
        internal bool BlockReadUntilReleased { get; set; }
        internal bool BlockDisposeUntilReleased { get; set; }
        internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int RenameCount { get; private set; }
        internal string SnapshotMessageId { get; set; } = "previous-answer";
        internal string? CompleteOnReleaseText { get; set; }
        public event Action<string, string, string, string>? AgentMessageCompleted;
        public event Action<string, string, string>? TurnCompleted;
        public event Action? ConnectionClosed;

        public Task<CodexThreadSummary?> FindAssistantAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CodexThreadSummary?>(new(_threadId, AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot));
        public Task<CodexThreadSummary> StartAssistantAsync(CancellationToken cancellationToken)
        {
            if (FailStartAssistant) throw new CodexCompatibilityException("The replacement result was uncertain.");
            _threadId = $"assistant-thread-{++StartCount + 1}";
            return Task.FromResult(new CodexThreadSummary(_threadId, AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot));
        }
        public async Task<CodexThreadSummary> ReplaceAssistantAsync(string previousThreadId, CancellationToken cancellationToken)
        {
            CodexThreadSummary replacement = await StartAssistantAsync(cancellationToken);
            RenameCount++;
            if (FailRename) throw new CodexCompatibilityException("The old title could not be changed.");
            return replacement;
        }
        public Task ResumeAssistantAsync(string threadId, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task<CodexThreadDetail> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            if (FailRead) throw new CodexCompatibilityException(ReadFailureMessage);
            if (BlockReadUntilCancelled)
            {
                ReadStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (BlockReadUntilReleased)
            {
                ReadStarted.TrySetResult();
                await ReleaseRead.Task.ConfigureAwait(false);
            }
            return new CodexThreadDetail(
                new(threadId, AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot),
                [new(SnapshotMessageId, "assistant", "Previous answer")]);
        }
        public Task<CodexTurnHandle> StartTurnAsync(string threadId, string question, CancellationToken cancellationToken)
        {
            LastQuestion = question;
            if (FailTurnStart) throw new CodexCompatibilityException("The turn result was uncertain.");
            _turnId = "assistant-turn-1";
            if (CloseOnStartTurn) ConnectionClosed?.Invoke();
            return Task.FromResult(new CodexTurnHandle(threadId, _turnId));
        }
        public void ReleaseTurnNotifications(string threadId)
        {
            if (CompleteOnReleaseText is not { } text) return;
            CompleteOnReleaseText = null;
            CompleteAnswer(text);
        }
        internal void CompleteAnswer(string text, string itemId = "assistant-answer-1")
        {
            AgentMessageCompleted?.Invoke(_threadId, _turnId!, itemId, text);
            TurnCompleted?.Invoke(_threadId, _turnId!, "completed");
        }
        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            DisposeStarted.TrySetResult();
            if (BlockDisposeUntilReleased) await ReleaseDispose.Task.ConfigureAwait(false);
        }
    }
}
