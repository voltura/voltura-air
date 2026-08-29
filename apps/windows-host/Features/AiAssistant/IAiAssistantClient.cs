namespace VolturaAir.Host.Features.AiAssistant;

internal interface IAiAssistantClient : IAsyncDisposable
{
    event Action<string, string, string, string>? AgentMessageCompleted;
    event Action<string, string, string>? TurnCompleted;
    event Action? ConnectionClosed;

    Task<CodexThreadSummary?> FindAssistantAsync(CancellationToken cancellationToken);
    Task<CodexThreadSummary> StartAssistantAsync(CancellationToken cancellationToken);
    Task<CodexThreadSummary> ReplaceAssistantAsync(string previousThreadId, CancellationToken cancellationToken);
    Task ResumeAssistantAsync(string threadId, CancellationToken cancellationToken);
    Task<CodexThreadDetail> ReadThreadAsync(string threadId, CancellationToken cancellationToken);
    Task<CodexTurnHandle> StartTurnAsync(string threadId, string question, CancellationToken cancellationToken);
    void ReleaseTurnNotifications(string threadId);
}

internal interface IAiAssistantClientFactory
{
    bool IsAvailable { get; }
    AiAssistantAvailability Availability => IsAvailable
        ? AiAssistantAvailability.Ready
        : AiAssistantAvailability.CodexMissing;
    Task<IAiAssistantClient> ConnectAsync(CancellationToken cancellationToken);
}

internal sealed class CodexAiAssistantClientFactory : IAiAssistantClientFactory
{
    internal static CodexAiAssistantClientFactory Instance { get; } = new();
    private CodexAiAssistantClientFactory() { }
    public bool IsAvailable => AiAssistantProfile.IsAvailable;
    public AiAssistantAvailability Availability => AiAssistantProfile.Availability;
    public async Task<IAiAssistantClient> ConnectAsync(CancellationToken cancellationToken) =>
        await CodexAppServerClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class UnavailableAiAssistantClientFactory : IAiAssistantClientFactory
{
    internal static UnavailableAiAssistantClientFactory Instance { get; } = new();
    private UnavailableAiAssistantClientFactory() { }
    public bool IsAvailable => false;
    public Task<IAiAssistantClient> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromException<IAiAssistantClient>(new CodexCompatibilityException("Codex is unavailable in isolated test mode."));
}
