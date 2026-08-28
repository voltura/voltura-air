using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class AiAssistantJsonRpcTests
{
    [Fact]
    public async Task StdioTransportPreservesRecordsAndRejectsOversizedInput()
    {
        await using (var transport = new StdioJsonLineTransport(new StringReader("first\r\nsecond\n"), new StringWriter()))
        {
            Assert.Equal("first", await transport.ReadLineAsync(TestContext.Current.CancellationToken));
            Assert.Equal("second", await transport.ReadLineAsync(TestContext.Current.CancellationToken));
            Assert.Null(await transport.ReadLineAsync(TestContext.Current.CancellationToken));
        }

        string oversized = new('x', StdioJsonLineTransport.MaximumRecordCharacters + 1);
        await using var bounded = new StdioJsonLineTransport(new StringReader(oversized), new StringWriter());
        CodexCompatibilityException error = await Assert.ThrowsAsync<CodexCompatibilityException>(
            async () => _ = await bounded.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Contains("record limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrelatesConcurrentResponsesAndDeliversNotifications()
    {
        await using var transport = new FakeTransport();
        await using var connection = new JsonRpcConnection(transport);
        string? notification = null;
        connection.NotificationReceived += (method, _) => notification = method;

        Task<JsonElement> first = connection.RequestAsync("first", new { value = 1 }, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Task<JsonElement> second = connection.RequestAsync("second", new { value = 2 }, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        long firstId = await transport.ReadRequestIdAsync();
        long secondId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync("""{"method":"turn/started","params":{"unknown":true}}""");
        await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = secondId, result = new { name = "second", unknown = true } }));
        await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = firstId, result = new { name = "first" } }));

        Assert.Equal("first", (await first).GetProperty("name").GetString());
        Assert.Equal("second", (await second).GetProperty("name").GetString());
        Assert.Equal("turn/started", notification);
    }

    [Fact]
    public async Task RejectsUnexpectedServerRequestsAndContinuesAfterMalformedInput()
    {
        await using var transport = new FakeTransport();
        await using var connection = new JsonRpcConnection(transport);
        await transport.ReceiveAsync("not json");
        await transport.ReceiveAsync("""{"id":91,"method":"approval/request","params":{}}""");

        using JsonDocument unsupported = JsonDocument.Parse(await transport.ReadWriteAsync());
        Assert.Equal(91, unsupported.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(-32601, unsupported.RootElement.GetProperty("error").GetProperty("code").GetInt32());

        Task<JsonElement> request = connection.RequestAsync("still/works", new { }, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        long id = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new { id, result = new { ok = true } }));
        Assert.True((await request).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task DispatchesSupportedServerRequestsAndRejectsOtherMethods()
    {
        await using var transport = new FakeTransport();
        await using var connection = new JsonRpcConnection(transport)
        {
            ServerRequestReceived = (method, parameters, _) => Task.FromResult<object?>(new
            {
                method,
                value = parameters.GetProperty("value").GetString()
            })
        };
        await transport.ReceiveAsync("""{"id":41,"method":"item/tool/call","params":{"value":"safe"}}""");

        using JsonDocument handled = JsonDocument.Parse(await transport.ReadWriteAsync());
        Assert.Equal("item/tool/call", handled.RootElement.GetProperty("result").GetProperty("method").GetString());
        Assert.Equal("safe", handled.RootElement.GetProperty("result").GetProperty("value").GetString());

        connection.ServerRequestReceived = (_, _, _) => throw new NotSupportedException();
        await transport.ReceiveAsync("""{"id":42,"method":"approval/request","params":{}}""");
        using JsonDocument unsupported = JsonDocument.Parse(await transport.ReadWriteAsync());
        Assert.Equal(-32601, unsupported.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task SurfacesTimeoutAndProcessExitAsCompatibilityFailures()
    {
        await using var timeoutTransport = new FakeTransport();
        await using (var timeoutConnection = new JsonRpcConnection(timeoutTransport))
        {
            Task<JsonElement> timedOut = timeoutConnection.RequestAsync("slow", new { }, TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            _ = await timeoutTransport.ReadRequestIdAsync();
            CodexCompatibilityException timeout = await Assert.ThrowsAsync<CodexCompatibilityException>(() => timedOut);
            Assert.Contains("timed out", timeout.Message, StringComparison.Ordinal);
        }

        await using var exitTransport = new FakeTransport();
        await using var exitConnection = new JsonRpcConnection(exitTransport);
        Task<JsonElement> pending = exitConnection.RequestAsync("pending", new { }, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        _ = await exitTransport.ReadRequestIdAsync();
        exitTransport.CompleteReads();
        CodexCompatibilityException closed = await Assert.ThrowsAsync<CodexCompatibilityException>(() => pending);
        Assert.Contains("connection closed", closed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoversAnUntitledPersistentThreadAfterNamingFails()
    {
        await using var transport = new FakeTransport();
        var connection = new JsonRpcConnection(transport);
        await using var client = new CodexAppServerClient(connection, []);
        string root = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot);

        Task<CodexThreadSummary> start = client.StartAssistantAsync(TestContext.Current.CancellationToken);
        long startId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new
        {
            id = startId,
            result = new { thread = new { id = "recoverable-thread", name = (string?)null, cwd = root } }
        }));
        long failedNameId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new
        {
            id = failedNameId,
            error = new { code = -32000, message = "name failed" }
        }));
        _ = await Assert.ThrowsAsync<CodexCompatibilityException>(() => start);

        Task<CodexThreadSummary?> find = client.FindAssistantAsync(TestContext.Current.CancellationToken);
        using (JsonDocument listRequest = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            Assert.Equal("thread/list", listRequest.RootElement.GetProperty("method").GetString());
            Assert.False(listRequest.RootElement.GetProperty("params").TryGetProperty("searchTerm", out _));
            long listId = listRequest.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new
            {
                id = listId,
                result = new
                {
                    data = new[]
                    {
                        new { id = "recoverable-thread", name = (string?)null, cwd = root },
                        new { id = "previous-thread", name = (string?)AiAssistantProfile.ThreadName, cwd = root }
                    }
                }
            }));
        }
        using (JsonDocument cleanupRequest = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            JsonElement parameters = cleanupRequest.RootElement.GetProperty("params");
            Assert.Equal("previous-thread", parameters.GetProperty("threadId").GetString());
            long cleanupId = cleanupRequest.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = cleanupId, result = new { } }));
        }
        using (JsonDocument recoveredNameRequest = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            JsonElement parameters = recoveredNameRequest.RootElement.GetProperty("params");
            Assert.Equal("recoverable-thread", parameters.GetProperty("threadId").GetString());
            Assert.Equal(AiAssistantProfile.ThreadName, parameters.GetProperty("name").GetString());
            long recoveredNameId = recoveredNameRequest.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = recoveredNameId, result = new { } }));
        }

        CodexThreadSummary recovered = Assert.IsType<CodexThreadSummary>(await find);
        Assert.Equal("recoverable-thread", recovered.Id);
        Assert.Equal(AiAssistantProfile.ThreadName, recovered.Title);
    }

    [Fact]
    public async Task RepairsDuplicateAssistantNamesAfterReplacementCleanupFails()
    {
        await using var transport = new FakeTransport();
        var connection = new JsonRpcConnection(transport);
        await using var client = new CodexAppServerClient(connection, []);
        string root = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot);

        Task<CodexThreadSummary> replace = client.ReplaceAssistantAsync("previous-thread", TestContext.Current.CancellationToken);
        long startId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new
        {
            id = startId,
            result = new { thread = new { id = "replacement-thread", name = (string?)null, cwd = root } }
        }));
        long replacementNameId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = replacementNameId, result = new { } }));
        long failedCleanupId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new
        {
            id = failedCleanupId,
            error = new { code = -32000, message = "cleanup failed" }
        }));
        _ = await Assert.ThrowsAsync<CodexCompatibilityException>(() => replace);

        Task<CodexThreadSummary?> find = client.FindAssistantAsync(TestContext.Current.CancellationToken);
        long listId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(JsonSerializer.Serialize(new
        {
            id = listId,
            result = new
            {
                data = new[]
                {
                    new { id = "replacement-thread", name = AiAssistantProfile.ThreadName, cwd = root },
                    new { id = "previous-thread", name = AiAssistantProfile.ThreadName, cwd = root }
                }
            }
        }));
        using (JsonDocument repairRequest = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            Assert.Equal("thread/name/set", repairRequest.RootElement.GetProperty("method").GetString());
            JsonElement parameters = repairRequest.RootElement.GetProperty("params");
            Assert.Equal("previous-thread", parameters.GetProperty("threadId").GetString());
            Assert.StartsWith($"{AiAssistantProfile.ThreadName} — previous ", parameters.GetProperty("name").GetString(), StringComparison.Ordinal);
            long repairId = repairRequest.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = repairId, result = new { } }));
        }

        CodexThreadSummary repaired = Assert.IsType<CodexThreadSummary>(await find);
        Assert.Equal("replacement-thread", repaired.Id);
    }

    [Fact]
    public async Task ReadsOnlyTheNewestBoundedThreadTurns()
    {
        await using var transport = new FakeTransport();
        var connection = new JsonRpcConnection(transport);
        await using var client = new CodexAppServerClient(connection, []);
        string root = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot);

        Task<CodexThreadDetail> read = client.ReadThreadAsync("assistant-thread", TestContext.Current.CancellationToken);
        using (JsonDocument metadataRequest = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            Assert.Equal("thread/read", metadataRequest.RootElement.GetProperty("method").GetString());
            JsonElement parameters = metadataRequest.RootElement.GetProperty("params");
            Assert.False(parameters.GetProperty("includeTurns").GetBoolean());
            long requestId = metadataRequest.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new
            {
                id = requestId,
                result = new { thread = new { id = "assistant-thread", name = AiAssistantProfile.ThreadName, cwd = root } }
            }));
        }
        using (JsonDocument turnsRequest = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            Assert.Equal("thread/turns/list", turnsRequest.RootElement.GetProperty("method").GetString());
            JsonElement parameters = turnsRequest.RootElement.GetProperty("params");
            Assert.Equal(AiAssistantProtocol.MaximumTranscriptTurns, parameters.GetProperty("limit").GetInt32());
            Assert.Equal("desc", parameters.GetProperty("sortDirection").GetString());
            long requestId = turnsRequest.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new
            {
                id = requestId,
                result = new
                {
                    data = new object[]
                    {
                        new
                        {
                            id = "new-turn",
                            items = new object[]
                            {
                                new { id = "new-question", type = "userMessage", content = new[] { new { type = "text", text = "Newest question" } } },
                                new { id = "tool-call", type = "dynamicToolCall", name = "read_voltura_doc" },
                                new { id = "new-answer", type = "agentMessage", text = "Newest answer" }
                            }
                        },
                        new
                        {
                            id = "old-turn",
                            items = new object[]
                            {
                                new { id = "old-question", type = "userMessage", content = new[] { new { type = "text", text = "Older question" } } },
                                new { id = "old-answer", type = "agentMessage", text = "Older answer" }
                            }
                        }
                    },
                    nextCursor = "more-history"
                }
            }));
        }

        CodexThreadDetail detail = await read;
        Assert.Collection(
            detail.Entries,
            entry =>
            {
                Assert.Equal("old-question", entry.Id);
                Assert.Equal("user", entry.Sender);
            },
            entry =>
            {
                Assert.Equal("old-answer", entry.Id);
                Assert.Equal("assistant", entry.Sender);
            },
            entry =>
            {
                Assert.Equal("new-question", entry.Id);
                Assert.Equal("user", entry.Sender);
            },
            entry =>
            {
                Assert.Equal("new-answer", entry.Id);
                Assert.Equal("assistant", entry.Sender);
            });
    }

    [Fact]
    public async Task ResumesWithoutHydratingTheUnboundedThreadHistory()
    {
        await using var transport = new FakeTransport();
        var connection = new JsonRpcConnection(transport);
        await using var client = new CodexAppServerClient(connection, []);

        Task resume = client.ResumeAssistantAsync("assistant-thread", TestContext.Current.CancellationToken);
        using (JsonDocument request = JsonDocument.Parse(await transport.ReadWriteAsync()))
        {
            Assert.Equal("thread/resume", request.RootElement.GetProperty("method").GetString());
            JsonElement parameters = request.RootElement.GetProperty("params");
            Assert.True(parameters.GetProperty("excludeTurns").GetBoolean());
            long requestId = request.RootElement.GetProperty("id").GetInt64();
            await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = requestId, result = new { } }));
        }

        await resume;
    }

    [Fact]
    public async Task ReleaseKeepsCrossingTurnNotificationsInArrivalOrder()
    {
        await using var transport = new FakeTransport();
        var connection = new JsonRpcConnection(transport);
        await using var client = new CodexAppServerClient(connection, []);
        var observed = new ConcurrentQueue<string>();
        var itemDispatchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowItemDispatch = new ManualResetEventSlim();
        client.AgentMessageCompleted += (_, _, _, _) =>
        {
            observed.Enqueue("item");
            itemDispatchStarted.TrySetResult();
            Assert.True(allowItemDispatch.Wait(TimeSpan.FromSeconds(3)));
        };
        client.TurnCompleted += (_, _, _) => observed.Enqueue("turn");

        Task<CodexTurnHandle> start = client.StartTurnAsync(
            "assistant-thread",
            "ordered answer",
            TestContext.Current.CancellationToken);
        long startId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(
            """{"method":"item/completed","params":{"threadId":"assistant-thread","turnId":"turn-1","item":{"id":"answer-1","type":"agentMessage","text":"Answer"}}}""");
        await transport.ReceiveAsync(JsonSerializer.Serialize(new
        {
            id = startId,
            result = new { turn = new { id = "turn-1" } }
        }));
        _ = await start;

        Task release = Task.Run(() => client.ReleaseTurnNotifications("assistant-thread"));
        await itemDispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Task<JsonElement> barrier = connection.RequestAsync(
            "barrier",
            new { },
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        long barrierId = await transport.ReadRequestIdAsync();
        await transport.ReceiveAsync(
            """{"method":"turn/completed","params":{"threadId":"assistant-thread","turn":{"id":"turn-1","status":"completed"}}}""");
        await transport.ReceiveAsync(JsonSerializer.Serialize(new { id = barrierId, result = new { ok = true } }));
        _ = await barrier;

        allowItemDispatch.Set();
        await release;

        Assert.Equal(["item", "turn"], observed);
    }

    private sealed class FakeTransport : IJsonLineTransport
    {
        private readonly Channel<string?> _reads = Channel.CreateUnbounded<string?>();
        private readonly Channel<string> _writes = Channel.CreateUnbounded<string>();

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            _reads.Reader.ReadAsync(cancellationToken);

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
            _writes.Writer.WriteAsync(line, cancellationToken);

        internal ValueTask ReceiveAsync(string line) => _reads.Writer.WriteAsync(line);
        internal void CompleteReads() => _reads.Writer.TryComplete();
        internal ValueTask<string> ReadWriteAsync() => _writes.Reader.ReadAsync();

        internal async Task<long> ReadRequestIdAsync()
        {
            using JsonDocument document = JsonDocument.Parse(await ReadWriteAsync());
            return document.RootElement.GetProperty("id").GetInt64();
        }

        public ValueTask DisposeAsync()
        {
            _reads.Writer.TryComplete();
            _writes.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
