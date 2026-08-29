using System.Diagnostics.CodeAnalysis;

namespace VolturaAir.Host.Features.AiAssistant;

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The active lease is detached and its client is deterministically disposed during manager shutdown.")]
internal sealed class AiAssistantSessionManager : IAsyncDisposable
{
    private readonly IAiAssistantClientFactory _clientFactory;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private AiAssistantSessionLease? _active;
    private object? _openingOwner;
    private bool _retiring;
    private TaskCompletionSource? _retirementCompletion;
    private TaskCompletionSource? _openingCompletion;
    private int _disposed;

    internal AiAssistantSessionManager(IAiAssistantClientFactory? clientFactory = null) =>
        _clientFactory = clientFactory ?? CodexAiAssistantClientFactory.Instance;

    internal event EventHandler? StateChanged;
    internal AiAssistantAvailability Availability => _clientFactory.Availability;
    internal bool IsAvailable => Availability == AiAssistantAvailability.Ready;
    internal bool IsActive
    {
        get { lock (_gate) return _active is not null || _openingOwner is not null || _retiring; }
    }

    internal bool IsOwnedBy(object owner)
    {
        lock (_gate)
        {
            return ReferenceEquals(_active?.Owner, owner) || ReferenceEquals(_openingOwner, owner);
        }
    }

    internal bool IsWorking
    {
        get { lock (_gate) return _active?.IsWorking == true; }
    }

    internal async Task<AiAssistantSessionOpenResult> TryOpenAsync(
        object owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return AiAssistantSessionOpenResult.Failed("closed", "The AI Assistant is unavailable.");
        }

        AiAssistantAvailability availability = Availability;
        if (availability != AiAssistantAvailability.Ready)
        {
            return availability == AiAssistantAvailability.KnowledgeMissing
                ? AiAssistantSessionOpenResult.Failed("knowledge-missing", "Repair Voltura Air to restore AI Assistant.")
                : AiAssistantSessionOpenResult.Failed("codex-missing", "Install Codex and sign in to use AI Assistant.");
        }

        TaskCompletionSource opening;
        lock (_gate)
        {
            if (_active is not null || _openingOwner is not null || _retiring)
            {
                return AiAssistantSessionOpenResult.Failed(
                    "busy",
                    "AI Assistant is open on another screen.");
            }
            _openingOwner = owner;
            opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _openingCompletion = opening;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);

        IAiAssistantClient? client = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        CancellationToken token = linked.Token;
        try
        {
            client = await _clientFactory.ConnectAsync(token).ConfigureAwait(false);
            CodexThreadSummary? thread = await client.FindAssistantAsync(token).ConfigureAwait(false);
            CodexThreadDetail snapshot;
            if (thread is null)
            {
                thread = await client.StartAssistantAsync(token).ConfigureAwait(false);
                snapshot = new CodexThreadDetail(thread, []);
            }
            else
            {
                await client.ResumeAssistantAsync(thread.Id, token).ConfigureAwait(false);
                snapshot = await client.ReadThreadAsync(thread.Id, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership transfers to the manager before the lease is published.
            var lease = new AiAssistantSessionLease(this, owner, client, thread.Id);
#pragma warning restore CA2000
            client = null;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(_openingOwner, owner))
                {
                    throw new OperationCanceledException(token);
                }
                _openingOwner = null;
                _active = lease;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new AiAssistantSessionOpenResult(lease, snapshot, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CodexCompatibilityException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return AiAssistantSessionOpenResult.Failed(
                "codex-unavailable",
                AiAssistantProtocol.BoundWithEllipsis(exception.Message, 240));
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            bool changed = false;
            lock (_gate)
            {
                if (ReferenceEquals(_openingOwner, owner))
                {
                    _openingOwner = null;
                    changed = true;
                }
                if (ReferenceEquals(_openingCompletion, opening)) _openingCompletion = null;
            }
            opening.TrySetResult();
            if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal async ValueTask ReleaseAsync(AiAssistantSessionLease lease)
    {
        bool released;
        TaskCompletionSource? retirement = null;
        lock (_gate)
        {
            released = ReferenceEquals(_active, lease);
            if (released)
            {
                _active = null;
                _retiring = true;
                retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _retirementCompletion = retirement;
            }
        }
        if (!released) return;
        try
        {
            await lease.DisposeClientAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _retiring = false;
                if (ReferenceEquals(_retirementCompletion, retirement)) _retirementCompletion = null;
            }
            retirement!.TrySetResult();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void PublishStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        AiAssistantSessionLease? active;
        Task retirement;
        Task opening;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _openingOwner = null;
            active = _active;
            _active = null;
            retirement = _retirementCompletion?.Task ?? Task.CompletedTask;
            opening = _openingCompletion?.Task ?? Task.CompletedTask;
        }
        if (active is not null) await active.DisposeClientAsync().ConfigureAwait(false);
        await opening.ConfigureAwait(false);
        await retirement.ConfigureAwait(false);
        _lifetime.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed record AiAssistantSessionOpenResult(
    AiAssistantSessionLease? Lease,
    CodexThreadDetail? Snapshot,
    string? Code,
    string? Message)
{
    internal bool Succeeded => Lease is not null && Snapshot is not null;
    internal static AiAssistantSessionOpenResult Failed(string code, string message) =>
        new(null, null, code, message);
}

internal sealed class AiAssistantSessionLease : IAsyncDisposable
{
    private readonly AiAssistantSessionManager _manager;
    private readonly Lock _connectionGate = new();
    private IAiAssistantClient? _client;
    private readonly CancellationTokenSource _lifetime = new();
    private Action? _connectionClosed;
    private bool _isConnectionClosed;
    private string _threadId;
    private string? _turnId;
    private int _working;
    private int _disposed;

    internal AiAssistantSessionLease(
        AiAssistantSessionManager manager,
        object owner,
        IAiAssistantClient client,
        string threadId)
    {
        _manager = manager;
        Owner = owner;
        _client = client;
        _threadId = threadId;
        client.AgentMessageCompleted += OnAgentMessageCompleted;
        client.TurnCompleted += OnTurnCompleted;
        client.ConnectionClosed += OnConnectionClosed;
    }

    internal object Owner { get; }
    internal bool IsWorking => Volatile.Read(ref _working) != 0;
    internal event Action<string, string>? MessageCompleted;
    internal event Action<string, string?>? TurnStateChanged;
    internal event Action? ConnectionClosed
    {
        add
        {
            if (value is null) return;
            bool notify;
            lock (_connectionGate)
            {
                notify = _isConnectionClosed;
                if (!notify) _connectionClosed += value;
            }
            if (notify) value();
        }
        remove
        {
            lock (_connectionGate) _connectionClosed -= value;
        }
    }

    internal async Task<CodexTurnHandle> StartTurnAsync(string question, CancellationToken cancellationToken)
    {
        IAiAssistantClient client = GetClient();
        if (IsWorking) throw new InvalidOperationException("Wait for the current answer to finish.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        CodexTurnHandle turn = await client.StartTurnAsync(_threadId, question, linked.Token).ConfigureAwait(false);
        _turnId = turn.TurnId;
        Volatile.Write(ref _working, 1);
        _manager.PublishStateChanged();
        return turn;
    }

    internal void ReleaseTurnNotifications() => GetClient().ReleaseTurnNotifications(_threadId);

    internal async Task<CodexThreadDetail> ReadAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await GetClient().ReadThreadAsync(_threadId, linked.Token).ConfigureAwait(false);
    }

    internal async Task<CodexThreadDetail> ResetAsync(CancellationToken cancellationToken)
    {
        if (IsWorking) throw new InvalidOperationException("Wait for the current answer to finish.");
        IAiAssistantClient client = GetClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        CodexThreadSummary replacement = await client.ReplaceAssistantAsync(_threadId, linked.Token).ConfigureAwait(false);
        _threadId = replacement.Id;
        _turnId = null;
        return new CodexThreadDetail(replacement, []);
    }

    private void OnAgentMessageCompleted(string threadId, string turnId, string itemId, string text)
    {
        if (threadId == _threadId && turnId == _turnId && Volatile.Read(ref _disposed) == 0)
            MessageCompleted?.Invoke(itemId, text);
    }

    private void OnTurnCompleted(string threadId, string turnId, string status)
    {
        if (threadId != _threadId || turnId != _turnId || Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _working, 0);
        _turnId = null;
        TurnStateChanged?.Invoke(
            status == "completed" ? "ready" : "failed",
            status == "completed" ? null : "The Assistant answer did not complete.");
        _manager.PublishStateChanged();
    }

    private void OnConnectionClosed()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Action? handlers;
        lock (_connectionGate)
        {
            if (_isConnectionClosed || Volatile.Read(ref _disposed) != 0) return;
            _isConnectionClosed = true;
            handlers = _connectionClosed;
            _connectionClosed = null;
        }
        handlers?.Invoke();
    }

    private IAiAssistantClient GetClient() => _client ?? throw new ObjectDisposedException(nameof(AiAssistantSessionLease));

    public ValueTask DisposeAsync() => _manager.ReleaseAsync(this);

    internal async ValueTask DisposeClientAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_connectionGate) _connectionClosed = null;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        IAiAssistantClient? client = Interlocked.Exchange(ref _client, null);
        if (client is not null)
        {
            client.AgentMessageCompleted -= OnAgentMessageCompleted;
            client.TurnCompleted -= OnTurnCompleted;
            client.ConnectionClosed -= OnConnectionClosed;
            await client.DisposeAsync().ConfigureAwait(false);
        }
        _lifetime.Dispose();
    }
}
