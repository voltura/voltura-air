using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace VolturaAir.Host.Features.AiAssistant;

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Stop atomically cancels the page lifetime and disposes the replaceable WPF view.")]
internal sealed class AiAssistantPageController : IDisposable
{
    private const int MaximumMessages = 32;
    private readonly Window _owner;
    private readonly AiAssistantSessionManager _sessions;
    private readonly List<AiAssistantConversationMessage> _messages = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pageLifetime;
    private AiAssistantPageView? _view;
    private AiAssistantSessionLease? _lease;
    private int _generation;
    private volatile bool _waitingForOwner;
    private bool _disposed;

    internal AiAssistantPageController(Window owner, AiAssistantSessionManager sessions)
    {
        _owner = owner;
        _sessions = sessions;
        _sessions.StateChanged += OnSessionStateChanged;
    }

    internal AiAssistantPageView CreateView(bool preserveState)
    {
        if (preserveState && _view is not null) return _view;
        Stop();
        _pageLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var view = new AiAssistantPageView();
        _view = view;
        view.RetryRequested += BeginOpen;
        view.SendRequested += BeginSend;
        view.NewConversationRequested += BeginReset;
        BeginOpen();
        return view;
    }

    internal void Stop()
    {
        Interlocked.Increment(ref _generation);
        _waitingForOwner = false;
        CancellationTokenSource? pageLifetime = Interlocked.Exchange(ref _pageLifetime, null);
        if (pageLifetime is not null)
        {
            pageLifetime.Cancel();
            pageLifetime.Dispose();
        }
        AiAssistantPageView? view = Interlocked.Exchange(ref _view, null);
        if (view is not null)
        {
            view.RetryRequested -= BeginOpen;
            view.SendRequested -= BeginSend;
            view.NewConversationRequested -= BeginReset;
            view.Dispose();
        }
        AiAssistantSessionLease? lease = Interlocked.Exchange(ref _lease, null);
        if (lease is not null)
        {
            _ = RetireAsync(lease);
        }
        _messages.Clear();
    }

#if DEBUG
    internal void ShowDemoForScreenshot()
    {
        if (_view is null) return;
        Interlocked.Increment(ref _generation);
        CloseLease();
        _messages.Clear();
        _messages.Add(new("preview-question", "user", "What is the difference between Direct and Relay?"));
        _messages.Add(new(
            "preview-answer",
            "assistant",
            "**Direct** connects your devices straight to this PC when possible. **Relay** provides a secure fallback when a direct connection cannot be established."));
        _view.SetReady();
        _view.ShowConversation(_messages);
    }
#endif

    private void BeginOpen()
    {
        if (_disposed || _view is null || _pageLifetime is null) return;
        _waitingForOwner = false;
        int generation = Interlocked.Increment(ref _generation);
        _view.SetOpening();
        _ = OpenAsync(generation, _view, _pageLifetime.Token);
    }

    private async Task OpenAsync(
        int generation,
        AiAssistantPageView view,
        CancellationToken cancellationToken)
    {
        try
        {
            AiAssistantSessionOpenResult result = await Task.Run(
                () => _sessions.TryOpenAsync(this, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (!IsCurrent(generation, view))
            {
                if (result.Lease is not null) await result.Lease.DisposeAsync().ConfigureAwait(false);
                return;
            }
            await _owner.Dispatcher.InvokeAsync(() => ApplyOpenResult(generation, view, result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void ApplyOpenResult(int generation, AiAssistantPageView view, AiAssistantSessionOpenResult result)
    {
        if (!IsCurrent(generation, view)) return;
        if (!result.Succeeded)
        {
            string title = result.Code switch
            {
                "codex-missing" => "Codex required",
                "knowledge-missing" => "Repair Voltura Air",
                "busy" => "Already in use",
                _ => "Codex unavailable"
            };
            string message = result.Code == "codex-unavailable" && LooksLikeAuthenticationFailure(result.Message)
                ? "Sign in to Codex, then retry."
                : result.Code == "codex-unavailable"
                    ? "Codex could not start. Retry."
                    : result.Message ?? "AI Assistant is unavailable.";
            view.ShowUnavailable(title, message);
            _waitingForOwner = result.Code == "busy";
            if (_waitingForOwner && !_sessions.IsActive)
                _owner.Dispatcher.BeginInvoke(BeginOpen);
            return;
        }

        AiAssistantSessionLease lease = result.Lease!;
        _lease = lease;
        lease.MessageCompleted += (itemId, text) => OnMessageCompleted(generation, view, lease, itemId, text);
        lease.TurnStateChanged += (state, message) => OnTurnStateChanged(generation, view, lease, state, message);
        lease.ConnectionClosed += () => OnConnectionClosed(generation, view, lease);
        ReplaceMessages(result.Snapshot!.Entries);
        view.ShowConversation(_messages);
        if (lease.IsWorking) view.SetWorking(_messages);
        else view.SetReady();
    }

    private void BeginSend()
    {
        if (_view is null || _lease is null || _pageLifetime is null) return;
        string question = _view.Question.Trim();
        if (question.Length == 0 || _lease.IsWorking) return;
        int generation = _generation;
        _view.SetPending("Sending…");
        _ = SendAsync(generation, _view, _lease, question, _pageLifetime.Token);
    }

    private async Task SendAsync(
        int generation,
        AiAssistantPageView view,
        AiAssistantSessionLease lease,
        string question,
        CancellationToken cancellationToken)
    {
        try
        {
            await lease.StartTurnAsync(question, cancellationToken).ConfigureAwait(false);
            if (!IsCurrent(generation, view, lease)) return;
            await _owner.Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrent(generation, view, lease)) return;
                AppendMessage(new(Guid.NewGuid().ToString("N"), "user", question));
                view.ClearQuestion();
                view.SetWorking(_messages);
            });
        }
        catch (Exception exception) when (exception is CodexCompatibilityException or IOException or InvalidOperationException)
        {
            await ShowUncertainAsync(
                generation,
                view,
                lease,
                Bound($"{exception.Message} Retry before asking again.")).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            try { lease.ReleaseTurnNotifications(); }
            catch (ObjectDisposedException) { }
        }
    }

    private void BeginReset()
    {
        if (_view is null || _lease is null || _lease.IsWorking || _pageLifetime is null) return;
        int generation = _generation;
        _view.SetPending("Starting a new conversation…");
        _ = ResetAsync(generation, _view, _lease, _pageLifetime.Token);
    }

    private async Task ResetAsync(
        int generation,
        AiAssistantPageView view,
        AiAssistantSessionLease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            CodexThreadDetail snapshot = await lease.ResetAsync(cancellationToken).ConfigureAwait(false);
            if (!IsCurrent(generation, view, lease)) return;
            await _owner.Dispatcher.InvokeAsync(() =>
            {
                ReplaceMessages(snapshot.Entries);
                view.ShowConversation(_messages);
                view.SetReady("New conversation ready.");
            });
        }
        catch (Exception exception) when (exception is CodexCompatibilityException or IOException or InvalidOperationException)
        {
            await ShowUncertainAsync(
                generation,
                view,
                lease,
                Bound($"{exception.Message} Retry to reload the conversation.")).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void OnMessageCompleted(
        int generation,
        AiAssistantPageView view,
        AiAssistantSessionLease lease,
        string itemId,
        string text)
    {
        if (_owner.Dispatcher.HasShutdownStarted || !IsCurrent(generation, view, lease)) return;
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(generation, view, lease)) return;
            AppendMessage(new(itemId, "assistant", text));
            view.ShowConversation(_messages);
        });
    }

    private void OnTurnStateChanged(
        int generation,
        AiAssistantPageView view,
        AiAssistantSessionLease lease,
        string state,
        string? message)
    {
        if (_owner.Dispatcher.HasShutdownStarted || !IsCurrent(generation, view, lease)) return;
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(generation, view, lease)) return;
            if (state == "ready") view.SetReady();
            else view.SetFailure(message ?? "The answer did not complete.");
            view.ShowConversation(_messages);
        });
    }

    private void OnConnectionClosed(
        int generation,
        AiAssistantPageView view,
        AiAssistantSessionLease lease)
    {
        if (_owner.Dispatcher.HasShutdownStarted || !IsCurrent(generation, view, lease)) return;
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(generation, view, lease)) return;
            view.ShowUnavailable("Codex unavailable", "Reconnect to Codex, then retry.");
            CloseLease();
        });
    }

    private async Task ShowUncertainAsync(
        int generation,
        AiAssistantPageView view,
        AiAssistantSessionLease lease,
        string message)
    {
        if (!IsCurrent(generation, view, lease)) return;
        await _owner.Dispatcher.InvokeAsync(() =>
        {
            if (!IsCurrent(generation, view, lease)) return;
            view.ShowUnavailable("Retry required", message);
            CloseLease();
        });
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (!_waitingForOwner || _sessions.IsActive || _owner.Dispatcher.HasShutdownStarted) return;
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (_waitingForOwner && !_sessions.IsActive) BeginOpen();
        });
    }

    private void ReplaceMessages(IEnumerable<CodexTranscriptEntry> entries)
    {
        _messages.Clear();
        foreach (CodexTranscriptEntry entry in entries.TakeLast(MaximumMessages))
            _messages.Add(new(entry.Id, entry.Sender, entry.Text));
    }

    private void AppendMessage(AiAssistantConversationMessage message)
    {
        _messages.Add(message);
        if (_messages.Count > MaximumMessages) _messages.RemoveRange(0, _messages.Count - MaximumMessages);
    }

    private void CloseLease()
    {
        AiAssistantSessionLease? lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;
        _ = RetireAsync(lease);
    }

    private static async Task RetireAsync(AiAssistantSessionLease lease)
    {
        try { await lease.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private bool IsCurrent(int generation, AiAssistantPageView view) =>
        generation == _generation && ReferenceEquals(_view, view) && !_disposed;

    private bool IsCurrent(int generation, AiAssistantPageView view, AiAssistantSessionLease lease) =>
        IsCurrent(generation, view) && ReferenceEquals(_lease, lease);

    private static bool LooksLikeAuthenticationFailure(string? message) =>
        message?.Contains("auth", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("sign in", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("login", StringComparison.OrdinalIgnoreCase) == true;

    private static string Bound(string message) => AiAssistantProtocol.BoundWithEllipsis(message, 240);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessions.StateChanged -= OnSessionStateChanged;
        _lifetime.Cancel();
        Stop();
        _lifetime.Dispose();
    }
}
