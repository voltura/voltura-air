using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class AiAssistantSessionManagerTests
{
    [Fact]
    public async Task EnforcesExclusiveOwnershipAndReleasesBeforeNextOpen()
    {
        var factory = new ManagerClientFactory();
        await using var manager = new AiAssistantSessionManager(factory);
        var firstOwner = new object();
        var secondOwner = new object();

        AiAssistantSessionOpenResult first = await manager.TryOpenAsync(firstOwner, TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        Assert.True(manager.IsOwnedBy(firstOwner));

        AiAssistantSessionOpenResult busy = await manager.TryOpenAsync(secondOwner, TestContext.Current.CancellationToken);
        Assert.False(busy.Succeeded);
        Assert.Equal("busy", busy.Code);
        Assert.Equal("AI Assistant is open on another screen.", busy.Message);

        await first.Lease!.DisposeAsync();
        Assert.False(manager.IsActive);
        Assert.True(factory.Clients[0].Disposed);

        AiAssistantSessionOpenResult second = await manager.TryOpenAsync(secondOwner, TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded);
        await second.Lease!.DisposeAsync();
        Assert.Equal(2, factory.Clients.Count);
    }

    [Fact]
    public async Task ReportsMissingKnowledgeSeparatelyFromMissingCodex()
    {
        var factory = new ManagerClientFactory { ReportedAvailability = AiAssistantAvailability.KnowledgeMissing };
        await using var manager = new AiAssistantSessionManager(factory);

        AiAssistantSessionOpenResult result = await manager.TryOpenAsync(new object(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("knowledge-missing", result.Code);
        Assert.Equal("Repair Voltura Air to restore AI Assistant.", result.Message);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task PublishesWorkingAndCompletionWithoutPolling()
    {
        var factory = new ManagerClientFactory();
        await using var manager = new AiAssistantSessionManager(factory);
        var owner = new object();
        int managerStateChanges = 0;
        manager.StateChanged += (_, _) => managerStateChanges++;
        AiAssistantSessionOpenResult opened = await manager.TryOpenAsync(owner, TestContext.Current.CancellationToken);
        AiAssistantSessionLease lease = opened.Lease!;
        string? state = null;
        lease.TurnStateChanged += (value, _) => state = value;

        CodexTurnHandle turn = await lease.StartTurnAsync("Question", TestContext.Current.CancellationToken);
        Assert.True(lease.IsWorking);
        factory.Clients[0].CompleteTurn(turn.TurnId);

        Assert.False(lease.IsWorking);
        Assert.Equal("ready", state);
        Assert.True(managerStateChanges >= 3);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task ReplaysConnectionCloseThatPrecedesOwnerSubscription()
    {
        var factory = new ManagerClientFactory { CloseWhenConnectionHandlerIsAdded = true };
        await using var manager = new AiAssistantSessionManager(factory);

        AiAssistantSessionOpenResult opened = await manager.TryOpenAsync(
            new object(),
            TestContext.Current.CancellationToken);
        int closeNotifications = 0;
        opened.Lease!.ConnectionClosed += () => closeNotifications++;

        Assert.Equal(1, closeNotifications);
        await opened.Lease.DisposeAsync();
    }

    private sealed class ManagerClientFactory : IAiAssistantClientFactory
    {
        internal List<ManagerClient> Clients { get; } = [];
        internal AiAssistantAvailability ReportedAvailability { get; set; } = AiAssistantAvailability.Ready;
        internal bool CloseWhenConnectionHandlerIsAdded { get; init; }
        public bool IsAvailable => ReportedAvailability == AiAssistantAvailability.Ready;
        public AiAssistantAvailability Availability => ReportedAvailability;

        public Task<IAiAssistantClient> ConnectAsync(CancellationToken cancellationToken)
        {
            var client = new ManagerClient { CloseWhenConnectionHandlerIsAdded = CloseWhenConnectionHandlerIsAdded };
            Clients.Add(client);
            return Task.FromResult<IAiAssistantClient>(client);
        }
    }

    private sealed class ManagerClient : IAiAssistantClient
    {
        private string? _turnId;
        private Action? _connectionClosed;
        internal bool Disposed { get; private set; }
        internal bool CloseWhenConnectionHandlerIsAdded { get; init; }
        public event Action<string, string, string, string>? AgentMessageCompleted;
        public event Action<string, string, string>? TurnCompleted;
        public event Action? ConnectionClosed
        {
            add
            {
                _connectionClosed += value;
                if (CloseWhenConnectionHandlerIsAdded) value?.Invoke();
            }
            remove => _connectionClosed -= value;
        }

        public Task<CodexThreadSummary?> FindAssistantAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CodexThreadSummary?>(new("thread", AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot));

        public Task<CodexThreadSummary> StartAssistantAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test thread already exists.");

        public Task<CodexThreadSummary> ReplaceAssistantAsync(string previousThreadId, CancellationToken cancellationToken) =>
            Task.FromResult(new CodexThreadSummary("replacement", AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot));

        public Task ResumeAssistantAsync(string threadId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CodexThreadDetail> ReadThreadAsync(string threadId, CancellationToken cancellationToken) =>
            Task.FromResult(new CodexThreadDetail(
                new(threadId, AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot),
                []));

        public Task<CodexTurnHandle> StartTurnAsync(string threadId, string question, CancellationToken cancellationToken)
        {
            _turnId = Guid.NewGuid().ToString("N");
            return Task.FromResult(new CodexTurnHandle(threadId, _turnId));
        }

        public void ReleaseTurnNotifications(string threadId) { }

        internal void CompleteTurn(string turnId) => TurnCompleted?.Invoke("thread", turnId, "completed");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        internal void KeepEventsReferenced()
        {
            AgentMessageCompleted?.Invoke("", "", "", "");
            _connectionClosed?.Invoke();
        }
    }
}
