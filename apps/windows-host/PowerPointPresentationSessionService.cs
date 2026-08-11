using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed record PowerPointSessionSnapshot(
    string State,
    string? RuntimePresentationId,
    string? PresentationName,
    string? OwnerClientId,
    string? OwnerDeviceName,
    DateTimeOffset? StartedAt,
    double ElapsedSeconds,
    bool BreakActive,
    double BreakElapsedSeconds,
    int? CurrentSlideIndex,
    int SlideCount,
    string SlideShowState)
{
    internal static PowerPointSessionSnapshot Inactive { get; } =
        new("inactive", null, null, null, null, null, 0, false, 0, null, 0, "ready");
}

internal sealed class PowerPointPresentationSessionService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly Lock _gate = new();
    private readonly IPowerPointAutomationService _powerPoint;
    private readonly IPresentationReportStore _reportStore;
    private readonly IPresentationBreakOverlay _breakOverlay;
    private readonly TimeProvider _timeProvider;
    private readonly string? _draftPath;
    private readonly System.Threading.Timer _statusTimer;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "SemaphoreSlim owns no OS handle because AvailableWaitHandle is never used; retaining it lets in-flight start leases release safely during shutdown.")]
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private SessionDraft? _session;
    private string? _pendingCommandOrigin;
    private string? _completionReportId;
    private string? _resumeReportId;
    private bool _disposed;

    internal PowerPointPresentationSessionService(
        IPowerPointAutomationService powerPoint,
        IPresentationReportStore reportStore,
        TimeProvider? timeProvider = null,
        IPresentationBreakOverlay? breakOverlay = null)
    {
        _powerPoint = powerPoint;
        _reportStore = reportStore;
        _breakOverlay = breakOverlay ?? NoOpPresentationBreakOverlay.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _statusTimer = new(
            OnStatusTimer,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _draftPath = string.IsNullOrEmpty(reportStore.ReportDirectory)
            ? null
            : Path.Combine(reportStore.ReportDirectory, "Drafts", "powerpoint-session.draft");
        _session = ReadDraft();
        if (_session is not null)
        {
            var recoverableAfterRestart = _session.State == "tracking"
                ? true
                : _session.RecoverableAfterRestart;
            _session = _session with
            {
                State = "pending-review",
                RunningSinceTimestamp = null,
                BreakSinceTimestamp = null,
                RecoverableAfterRestart = recoverableAfterRestart
            };
        }

        _powerPoint.SnapshotChanged += OnPowerPointSnapshotChanged;
        lock (_gate)
        {
            var snapshot = _powerPoint.Snapshot;
            if (!TryRecoverInterruptedSessionLocked(snapshot) &&
                _session?.State == "pending-review")
            {
                _ = ReconcilePausedSessionLocked(snapshot);
            }
        }
    }

    internal event EventHandler? StateChanged;

    internal PowerPointSessionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshot(_session);
            }
        }
    }

    internal void PrepareCommand(string action, string? runtimePresentationId)
    {
        if (action is not ("next" or "previous" or "first" or "last" or "goto"))
        {
            return;
        }

        lock (_gate)
        {
            if (_session?.State == "tracking" &&
                (runtimePresentationId is null ||
                 string.Equals(
                     _session.RuntimePresentationId,
                     runtimePresentationId,
                     StringComparison.Ordinal)))
            {
                _pendingCommandOrigin = "voltura-air";
            }
        }
    }

    internal void CompleteCommand(PowerPointPresentationSnapshot? presentation)
    {
        var changed = false;
        lock (_gate)
        {
            if (_completionReportId is null &&
                _session?.State == "tracking" &&
                presentation is not null &&
                string.Equals(
                    _session.RuntimePresentationId,
                    presentation.RuntimePresentationId,
                    StringComparison.Ordinal))
            {
                var previousSession = _session;
                changed = ApplyPresentationSnapshotLocked(presentation);
                if (changed && !PersistLocked())
                {
                    _session = previousSession;
                    changed = false;
                }
            }

            _pendingCommandOrigin = null;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal async Task<IDisposable> AcquireStartAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed)
            {
                _startGate.Release();
                throw new ObjectDisposedException(nameof(PowerPointPresentationSessionService));
            }
        }

        return new StartLease(() => _startGate.Release());
    }

    internal async Task<SessionOperationResult> PrepareForStartAsync(
        string? runtimePresentationId,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return new(false, "session-unavailable", "Presentation tracking is stopping.");
            }

            if (_completionReportId is not null)
            {
                return new(
                    false,
                    "session-saving",
                    "The presentation session is being saved. Wait a moment.");
            }

            if (_session is null ||
                IsSamePresentation(_session, runtimePresentationId, sourcePath))
            {
                return new(true, null, "Presentation can start.");
            }
        }

        var completed = await CompleteAsync(
            save: true,
            cancellationToken).ConfigureAwait(false);
        return completed.Succeeded
            ? new(true, null, "The previous presentation was saved automatically.")
            : completed;
    }

    internal SessionOperationResult Start(
        string clientId,
        string deviceName,
        PowerPointPresentationSnapshot presentation) =>
        StartOrResume(clientId, deviceName, presentation);

    internal SessionOperationResult StartOrResume(
        string clientId,
        string deviceName,
        PowerPointPresentationSnapshot presentation)
    {
        SessionOperationResult result;
        lock (_gate)
        {
            if (_disposed)
            {
                return new(false, "session-unavailable", "Presentation tracking is stopping.");
            }

            if (_completionReportId is not null)
            {
                return new(
                    false,
                    "session-saving",
                    "The presentation session is being saved. Wait a moment.");
            }

            var previousSession = _session;
            if (_session is not null)
            {
                if (_session.State == "tracking" &&
                    IsSamePresentation(_session, presentation))
                {
                    if (string.Equals(
                            _session.OwnerClientId,
                            clientId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            _session.OwnerDeviceName,
                            deviceName,
                            StringComparison.Ordinal))
                    {
                        return new(true, null, "Presentation tracking is already active.");
                    }

                    _session = _session with
                    {
                        OwnerClientId = clientId,
                        OwnerDeviceName = deviceName
                    };
                    if (!PersistLocked())
                    {
                        _session = previousSession;
                        return PersistenceFailure();
                    }

                    result = new(true, null, "Presentation control transferred.");
                }

                else if (_session.State == "pending-review" &&
                    IsSamePresentation(_session, presentation))
                {
                    ResumePausedSessionLocked(presentation, clientId, deviceName);
                    if (!PersistLocked())
                    {
                        _session = previousSession;
                        return PersistenceFailure();
                    }

                    _statusTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    result = new(true, null, "Presentation tracking resumed.");
                }
                else
                {
                    return new(
                        false,
                        "session-active",
                        "Save or discard the paused session before starting a different presentation.");
                }
            }
            else
            {
                var now = _timeProvider.GetUtcNow();
                var timestamp = _timeProvider.GetTimestamp();
                _session = new(
                    State: "tracking",
                    ReportId: Guid.NewGuid().ToString("N"),
                    RuntimePresentationId: presentation.RuntimePresentationId,
                    PresentationName: presentation.Name,
                    SourcePath: presentation.SourcePath,
                    OwnerClientId: clientId,
                    OwnerDeviceName: deviceName,
                    StartedAt: now,
                    EndedAt: null,
                    AccumulatedSeconds: 0,
                    RunningSinceTimestamp: timestamp,
                    BreakActive: false,
                    BreakStartedAt: null,
                    BreakAccumulatedSeconds: 0,
                    BreakSinceTimestamp: null,
                    VisitStartedTimestamp: presentation.CurrentSlideIndex is null
                        ? null
                        : timestamp,
                    CurrentSlideIndex: presentation.CurrentSlideIndex,
                    SlideCount: presentation.SlideCount,
                    SlideShowState: presentation.SlideShowState,
                    Visits: presentation.CurrentSlideIndex is { } slide
                        ? [new(slide, now, 0, "voltura-air")]
                        : [],
                    Breaks: [],
                    RecoverableAfterRestart: true);
                if (!PersistLocked())
                {
                    _session = previousSession;
                    return PersistenceFailure();
                }

                _statusTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                result = new(true, null, "Presentation tracking started.");
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);

        return result;
    }

    internal SessionOperationResult SetBreak(bool enabled)
        => SetBreakCore(enabled, resumeReportId: null);

    private SessionOperationResult SetBreakCore(
        bool enabled,
        string? resumeReportId)
    {
        lock (_gate)
        {
            if (_completionReportId is not null)
            {
                return new(
                    false,
                    "session-saving",
                    "The presentation session is being saved. Wait a moment.");
            }

            if (_resumeReportId is not null &&
                !string.Equals(_resumeReportId, resumeReportId, StringComparison.Ordinal))
            {
                return new(
                    false,
                    "session-resuming",
                    "The presentation session is resuming. Wait a moment.");
            }

            if (!TryGetTrackingSession(out var session, out var failure))
            {
                return failure;
            }

            if (session!.BreakActive == enabled)
            {
                return new(true, null, enabled ? "Break already active." : "Presentation already resumed.");
            }

            if (enabled &&
                session.Breaks.Length >= PresentationReportProtocol.MaxBreakCount)
            {
                return new(
                    false,
                    "session-break-limit",
                    "This presentation has reached the break limit.");
            }

            var now = _timeProvider.GetUtcNow();
            var timestamp = _timeProvider.GetTimestamp();
            var previousSession = session!;
            if (enabled)
            {
                var overlayResult = _breakOverlay.TryShowPresentationBreak(
                    () => _timeProvider.GetElapsedTime(
                        timestamp,
                        _timeProvider.GetTimestamp()));
                if (!overlayResult.Succeeded)
                {
                    return new(
                        false,
                        "session-break-overlay-failed",
                        "Voltura Air could not show the presentation break screen.");
                }

                _session = session with
                {
                    AccumulatedSeconds = CurrentElapsedSeconds(session),
                    RunningSinceTimestamp = null,
                    BreakActive = true,
                    BreakStartedAt = now,
                    BreakSinceTimestamp = timestamp,
                    VisitStartedTimestamp = null,
                    Visits = CloseCurrentVisit(session)
                };
            }
            else
            {
                _ = _breakOverlay.DismissPresentationBreakIfActive();
                var duration = CurrentBreakSeconds(session);
                var breaks = session.Breaks.Append(new SessionBreak(
                    session.Breaks.Length + 1,
                    CurrentElapsedSeconds(session),
                    session.BreakStartedAt ?? now,
                    now,
                    duration,
                    session.CurrentSlideIndex)).ToArray();
                var visits = session.CurrentSlideIndex is { } slide
                    ? [.. session.Visits.Append(new(
                        slide,
                        now,
                        0,
                        "powerpoint"))
                        .TakeLast(PresentationReportProtocol.MaxSlideVisitCount)]
                    : session.Visits;
                _session = session with
                {
                    RunningSinceTimestamp = timestamp,
                    VisitStartedTimestamp = session.CurrentSlideIndex is null
                        ? null
                        : timestamp,
                    BreakActive = false,
                    BreakStartedAt = null,
                    BreakAccumulatedSeconds = session.BreakAccumulatedSeconds + duration,
                    BreakSinceTimestamp = null,
                    Breaks = breaks,
                    Visits = visits
                };
            }

            if (!PersistLocked())
            {
                _session = previousSession;
                if (enabled)
                {
                    _ = _breakOverlay.DismissPresentationBreakIfActive();
                }
                else if (previousSession.BreakSinceTimestamp is { } breakTimestamp)
                {
                    _ = _breakOverlay.TryShowPresentationBreak(
                        () => _timeProvider.GetElapsedTime(
                            breakTimestamp,
                            _timeProvider.GetTimestamp()));
                }

                return PersistenceFailure();
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return new(true, null, enabled ? "Break started." : "Presentation resumed.");
    }

    internal async Task<SessionOperationResult> ResumeAsync(
        CancellationToken cancellationToken)
    {
        string runtimePresentationId;
        int? currentSlideIndex;
        string? sourcePath;
        string reportId;
        lock (_gate)
        {
            if (_completionReportId is not null || _resumeReportId is not null)
            {
                return new(
                    false,
                    "session-busy",
                    "The presentation session is busy. Wait a moment.");
            }

            if (!TryGetTrackingSession(out var session, out var failure))
            {
                return failure;
            }

            if (session is null || !session.BreakActive)
            {
                return new(false, "session-break-state", "The presentation is not on a break.");
            }

            runtimePresentationId = session.RuntimePresentationId;
            currentSlideIndex = session.CurrentSlideIndex;
            sourcePath = session.SourcePath;
            reportId = session.ReportId;
            _resumeReportId = reportId;
        }

        try
        {
            var originalRuntimePresentationId = runtimePresentationId;
            var activation = await _powerPoint.ExecuteAsync(
                new("activate", runtimePresentationId),
                cancellationToken).ConfigureAwait(false);
            if (!IsResumeCurrent(reportId))
            {
                return SessionChangedDuringResume();
            }

            var activatedPresentation = activation.Presentation ??
                activation.Snapshot.Presentations.FirstOrDefault(item =>
                    string.Equals(
                        item.RuntimePresentationId,
                        runtimePresentationId,
                        StringComparison.Ordinal));
            var mustStartSlideshow =
                activation.Succeeded && activatedPresentation?.IsPresenting == false ||
                !activation.Succeeded &&
                string.Equals(activation.Code, "powerpoint-not-presenting", StringComparison.Ordinal);
            if (!activation.Succeeded &&
                string.Equals(activation.Code, "powerpoint-target-stale", StringComparison.Ordinal) &&
                sourcePath is not null)
            {
                var reopened = await _powerPoint.ExecuteAsync(
                    new("open", SourcePath: sourcePath),
                    cancellationToken).ConfigureAwait(false);
                if (!IsResumeCurrent(reportId))
                {
                    return SessionChangedDuringResume();
                }

                if (!reopened.Succeeded || reopened.Presentation is null)
                {
                    return new(
                        false,
                        reopened.Code ?? "powerpoint-open-failed",
                        "PowerPoint could not reopen the tracked presentation.");
                }

                runtimePresentationId = reopened.Presentation.RuntimePresentationId;
                lock (_gate)
                {
                    if (IsResumeCurrentLocked(reportId))
                    {
                        _session = _session! with
                        {
                            RuntimePresentationId = runtimePresentationId
                        };
                        if (!PersistLocked())
                        {
                            _session = _session with
                            {
                                RuntimePresentationId = originalRuntimePresentationId
                            };
                            return PersistenceFailure();
                        }
                    }
                }

                mustStartSlideshow = true;
            }

            if (mustStartSlideshow)
            {
                var started = await _powerPoint.ExecuteAsync(
                    new("start", runtimePresentationId),
                    cancellationToken).ConfigureAwait(false);
                if (!IsResumeCurrent(reportId))
                {
                    return SessionChangedDuringResume();
                }

                if (!started.Succeeded)
                {
                    return new(
                        false,
                        started.Code ?? "powerpoint-start-failed",
                        "PowerPoint could not restart the slideshow.");
                }

                if (currentSlideIndex is > 1)
                {
                    var restored = await _powerPoint.ExecuteAsync(
                        new(
                            "goto",
                            runtimePresentationId,
                            SlideNumber: currentSlideIndex),
                        cancellationToken).ConfigureAwait(false);
                    if (!IsResumeCurrent(reportId))
                    {
                        return SessionChangedDuringResume();
                    }

                    if (!restored.Succeeded)
                    {
                        return new(
                            false,
                            restored.Code ?? "powerpoint-slide-restore-failed",
                            "PowerPoint restarted, but could not return to the previous slide.");
                    }
                }

                activation = await _powerPoint.ExecuteAsync(
                    new("activate", runtimePresentationId),
                    cancellationToken).ConfigureAwait(false);
                if (!IsResumeCurrent(reportId))
                {
                    return SessionChangedDuringResume();
                }
            }

            if (!activation.Succeeded)
            {
                return new(
                    false,
                    activation.Code ?? "powerpoint-activation-failed",
                    "PowerPoint could not bring the slideshow back into focus.");
            }

            return SetBreakCore(enabled: false, reportId);
        }
        finally
        {
            lock (_gate)
            {
                if (string.Equals(_resumeReportId, reportId, StringComparison.Ordinal))
                {
                    _resumeReportId = null;
                }
            }
        }
    }

    internal async Task<SessionOperationResult> CompleteAsync(
        bool save,
        CancellationToken cancellationToken)
    {
        if (!save)
        {
            return Discard();
        }

        SessionDraft session;
        lock (_gate)
        {
            if (_completionReportId is not null || _resumeReportId is not null)
            {
                return new(
                    false,
                    "session-busy",
                    "The presentation session is busy. Wait a moment.");
            }

            if (_session is null)
            {
                return new(false, "session-unavailable", "There is no presentation draft to finish.");
            }

            var previousSession = _session;
            session = _session.State == "tracking"
                ? FinalizeLocked(_session, "pending-review")
                : _session;
            _session = session;
            _statusTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (!PersistLocked())
            {
                _session = previousSession;
                if (previousSession.State == "tracking")
                {
                    _statusTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                }

                return PersistenceFailure();
            }

            _completionReportId = session.ReportId;
        }

        _ = _breakOverlay.DismissPresentationBreakIfActive();
        StateChanged?.Invoke(this, EventArgs.Empty);
        PresentationReportSaveResult saveResult;
        try
        {
            saveResult = await _reportStore.SaveAsync(
                CreateReportRequest(session),
                session.OwnerClientId,
                session.OwnerDeviceName,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                if (string.Equals(_completionReportId, session.ReportId, StringComparison.Ordinal))
                {
                    _completionReportId = null;
                }
            }

            return PersistenceFailure();
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (string.Equals(_completionReportId, session.ReportId, StringComparison.Ordinal))
                {
                    _completionReportId = null;
                }
            }

            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (_gate)
            {
                if (string.Equals(
                    _completionReportId,
                    session.ReportId,
                    StringComparison.Ordinal))
                {
                    _completionReportId = null;
                }
            }

            return new(
                false,
                "session-save-failed",
                "The presentation could not be saved on the PC.");
        }

        if (!saveResult.Succeeded)
        {
            lock (_gate)
            {
                if (string.Equals(_completionReportId, session.ReportId, StringComparison.Ordinal))
                {
                    _completionReportId = null;
                }
            }

            return new(false, saveResult.Code, saveResult.Message);
        }

        SessionOperationResult? cleanupFailure = null;
        lock (_gate)
        {
            if (!string.Equals(_completionReportId, session.ReportId, StringComparison.Ordinal) ||
                _session?.ReportId != session.ReportId)
            {
                return new(
                    false,
                    "session-changed",
                    "The presentation session changed before completion finished.");
            }

            if (!DeleteDraftLocked())
            {
                _completionReportId = null;
                cleanupFailure = PersistenceFailure();
            }
            else
            {
                _session = null;
                _completionReportId = null;
            }
        }

        if (cleanupFailure is not null)
        {
            return cleanupFailure.Value;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return new(true, null, "Presentation saved.");
    }

    private SessionOperationResult Discard()
    {
        lock (_gate)
        {
            if (_completionReportId is not null || _resumeReportId is not null)
            {
                return new(
                    false,
                    "session-busy",
                    "The presentation session is busy. Wait a moment.");
            }

            if (_session is null)
            {
                return new(false, "session-unavailable", "There is no presentation draft to finish.");
            }

            if (!DeleteDraftLocked())
            {
                return PersistenceFailure();
            }

            _session = null;
            _statusTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        _ = _breakOverlay.DismissPresentationBreakIfActive();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return new(true, null, "Presentation draft discarded.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_session?.State == "tracking")
            {
                _session = FinalizeLocked(
                    _session,
                    "pending-review",
                    recoverableAfterRestart: true);
                PersistLocked();
            }

            _disposed = true;
            _resumeReportId = null;
        }

        _powerPoint.SnapshotChanged -= OnPowerPointSnapshotChanged;
        _ = _breakOverlay.DismissPresentationBreakIfActive();
        _statusTimer.Dispose();
    }

    private void OnPowerPointSnapshotChanged(object? sender, EventArgs eventArgs)
    {
        var changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var snapshot = _powerPoint.Snapshot;
            if (snapshot.State != PowerPointDiscoveryState.Ready)
            {
                return;
            }

            if (TryRecoverInterruptedSessionLocked(snapshot))
            {
                changed = true;
            }

            if (_session?.State == "pending-review" &&
                ReconcilePausedSessionLocked(snapshot))
            {
                changed = true;
            }

            if (_session?.State == "tracking")
            {
                var presentation = snapshot.Presentations.FirstOrDefault(
                    item => string.Equals(
                        item.RuntimePresentationId,
                        _session.RuntimePresentationId,
                        StringComparison.Ordinal));
                if ((presentation is null || !presentation.IsPresenting) &&
                    !_session.BreakActive)
                {
                    var previousSession = _session;
                    _session = FinalizeLocked(_session, "pending-review");
                    _ = _breakOverlay.DismissPresentationBreakIfActive();
                    _statusTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    if (!ReconcilePausedSessionLocked(snapshot))
                    {
                        if (!PersistLocked())
                        {
                            _session = previousSession;
                            _statusTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                            return;
                        }
                    }
                    changed = true;
                }
                else if (presentation?.IsPresenting == true)
                {
                    var previousSession = _session;
                    if (ApplyPresentationSnapshotLocked(presentation))
                    {
                        if (!PersistLocked())
                        {
                            _session = previousSession;
                        }
                        else
                        {
                            changed = true;
                        }
                    }
                }
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnStatusTimer(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _session?.State != "tracking")
            {
                return;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ApplyPresentationSnapshotLocked(PowerPointPresentationSnapshot presentation)
    {
        var session = _session!;
        var slideChanged = presentation.CurrentSlideIndex != session.CurrentSlideIndex;
        var stateChanged = !string.Equals(
            presentation.SlideShowState,
            session.SlideShowState,
            StringComparison.Ordinal);
        if (!slideChanged && !stateChanged)
        {
            return false;
        }

        var visits = session.Visits;
        if (slideChanged)
        {
            var now = _timeProvider.GetUtcNow();
            visits = CloseCurrentVisit(session);
            if (presentation.CurrentSlideIndex is { } slide)
            {
                visits = [.. visits
                    .Append(new(
                        slide,
                        now,
                        0,
                        _pendingCommandOrigin ?? "powerpoint"))
                    .TakeLast(PresentationReportProtocol.MaxSlideVisitCount)];
            }
        }

        _pendingCommandOrigin = null;
        _session = session with
        {
            CurrentSlideIndex = presentation.CurrentSlideIndex,
            SlideCount = presentation.SlideCount,
            SlideShowState = presentation.SlideShowState,
            VisitStartedTimestamp = slideChanged &&
                !session.BreakActive &&
                presentation.CurrentSlideIndex is not null
                ? _timeProvider.GetTimestamp()
                : session.VisitStartedTimestamp,
            Visits = visits
        };
        return true;
    }

    private bool TryGetTrackingSession(
        out SessionDraft? session,
        out SessionOperationResult failure)
    {
        session = _session;
        if (session?.State != "tracking")
        {
            failure = new(false, "session-unavailable", "There is no active tracked presentation.");
            return false;
        }

        failure = default;
        return true;
    }

    private SessionDraft FinalizeLocked(
        SessionDraft session,
        string state,
        bool recoverableAfterRestart = false)
    {
        var now = _timeProvider.GetUtcNow();
        var currentBreakSeconds = CurrentBreakSeconds(session);
        var breaks = session.BreakActive &&
            session.BreakSinceTimestamp is not null
            ?

            [
                .. session.Breaks,
                new SessionBreak(
                        session.Breaks.Length + 1,
                        CurrentElapsedSeconds(session),
                        session.BreakStartedAt ?? now,
                        now,
                        currentBreakSeconds,
                        session.CurrentSlideIndex),
            ] : session.Breaks;
        return session with
        {
            State = state,
            EndedAt = now,
            AccumulatedSeconds = CurrentElapsedSeconds(session),
            RunningSinceTimestamp = null,
            BreakAccumulatedSeconds = session.BreakAccumulatedSeconds + currentBreakSeconds,
            BreakSinceTimestamp = null,
            VisitStartedTimestamp = null,
            Visits = CloseCurrentVisit(session),
            Breaks = breaks,
            RecoverableAfterRestart = recoverableAfterRestart
        };
    }

    private bool TryRecoverInterruptedSessionLocked(
        PowerPointAutomationSnapshot snapshot)
    {
        if (_session is not { State: "pending-review" } session ||
            snapshot.State != PowerPointDiscoveryState.Ready)
        {
            return false;
        }

        var candidates = snapshot.Presentations
            .Where(presentation => presentation.IsPresenting)
            .Where(presentation => IsSamePresentation(session, presentation))
            .ToArray();
        if (candidates.Length != 1)
        {
            return false;
        }

        var previousSession = _session;
        ResumePausedSessionLocked(candidates[0]);
        if (!PersistLocked())
        {
            _session = previousSession;
            return false;
        }

        _statusTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        return true;
    }

    private bool ReconcilePausedSessionLocked(
        PowerPointAutomationSnapshot snapshot)
    {
        var session = _session!;
        var candidates = snapshot.Presentations
            .Where(presentation => !presentation.IsPresenting)
            .Where(presentation => IsSamePresentation(session, presentation))
            .ToArray();
        if (candidates.Length != 1 ||
            candidates[0].CurrentSlideIndex is null)
        {
            return false;
        }

        var presentation = candidates[0];
        var changed =
            !string.Equals(
                session.RuntimePresentationId,
                presentation.RuntimePresentationId,
                StringComparison.Ordinal) ||
            session.CurrentSlideIndex != presentation.CurrentSlideIndex ||
            session.SlideCount != presentation.SlideCount ||
            !string.Equals(
                session.SlideShowState,
                presentation.SlideShowState,
                StringComparison.Ordinal);
        if (!changed)
        {
            return false;
        }

        _session = session with
        {
            RuntimePresentationId = presentation.RuntimePresentationId,
            PresentationName = presentation.Name,
            SourcePath = presentation.SourcePath ?? session.SourcePath,
            CurrentSlideIndex = presentation.CurrentSlideIndex,
            SlideCount = presentation.SlideCount,
            SlideShowState = presentation.SlideShowState
        };
        if (!PersistLocked())
        {
            _session = session;
            return false;
        }

        return true;
    }

    private void ResumePausedSessionLocked(
        PowerPointPresentationSnapshot presentation,
        string? ownerClientId = null,
        string? ownerDeviceName = null)
    {
        var session = _session!;
        var now = _timeProvider.GetUtcNow();
        var timestamp = _timeProvider.GetTimestamp();
        var visits = presentation.CurrentSlideIndex is { } slide
            ? [.. session.Visits.Append(new(
                slide,
                now,
                0,
                "powerpoint")).TakeLast(PresentationReportProtocol.MaxSlideVisitCount)]
            : session.Visits;
        _session = session with
        {
            State = "tracking",
            RuntimePresentationId = presentation.RuntimePresentationId,
            PresentationName = presentation.Name,
            SourcePath = presentation.SourcePath ?? session.SourcePath,
            OwnerClientId = ownerClientId ?? session.OwnerClientId,
            OwnerDeviceName = ownerDeviceName ?? session.OwnerDeviceName,
            EndedAt = null,
            RunningSinceTimestamp = timestamp,
            BreakActive = false,
            BreakStartedAt = null,
            BreakSinceTimestamp = null,
            VisitStartedTimestamp = presentation.CurrentSlideIndex is null
                ? null
                : timestamp,
            CurrentSlideIndex = presentation.CurrentSlideIndex,
            SlideCount = presentation.SlideCount,
            SlideShowState = presentation.SlideShowState,
            Visits = visits,
            RecoverableAfterRestart = true
        };
    }

    private static bool IsSamePresentation(
        SessionDraft session,
        PowerPointPresentationSnapshot presentation) =>
        IsSamePresentation(
            session,
            presentation.RuntimePresentationId,
            presentation.SourcePath);

    private static bool IsSamePresentation(
        SessionDraft session,
        string? runtimePresentationId,
        string? sourcePath) =>
        string.Equals(
            session.RuntimePresentationId,
            runtimePresentationId,
            StringComparison.Ordinal) ||
        session.SourcePath is { Length: > 0 } sessionPath &&
        sourcePath is { Length: > 0 } presentationPath &&
        string.Equals(
            sessionPath,
            presentationPath,
            StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<PresentationReportSlideVisit> CloseCurrentVisit(
        SessionDraft session)
    {
        if (session.Visits.Count == 0)
        {
            return session.Visits;
        }

        var visits = session.Visits.ToArray();
        if (session.BreakActive || session.VisitStartedTimestamp is null)
        {
            return visits;
        }

        var current = visits[^1];
        visits[^1] = current with
        {
            DurationSeconds = Math.Max(
                current.DurationSeconds,
                _timeProvider.GetElapsedTime(
                    session.VisitStartedTimestamp.Value,
                    _timeProvider.GetTimestamp()).TotalSeconds)
        };
        return visits;
    }

    private PresentationReportSaveRequest CreateReportRequest(SessionDraft session)
    {
        var endedAt = session.EndedAt ?? _timeProvider.GetUtcNow();
        var visits = CloseCurrentVisit(session);
        var slides = visits
            .GroupBy(visit => visit.SlideNumber)
            .OrderBy(group => group.Key)
            .Select(group => new PresentationReportSlide(
                group.Key,
                group.Sum(visit => visit.DurationSeconds)))
            .ToArray();
        var breaks = session.Breaks.Select(entry => new PresentationReportBreak(
            entry.Number,
            entry.PresentationElapsedSeconds,
            entry.DurationSeconds,
            entry.StartedAt,
            entry.EndedAt,
            null,
            null,
            entry.SlideNumber,
            entry.SlideNumber)).ToArray();
        return new(
            OperationId: $"host-{session.ReportId}",
            ReportId: session.ReportId,
            Target: "powerpoint",
            StartedAt: session.StartedAt,
            EndedAt: endedAt,
            UtcOffsetMinutes: (int)TimeZoneInfo.Local.GetUtcOffset(endedAt).TotalMinutes,
            PlannedDurationSeconds: 0,
            PresentationDurationSeconds: CurrentElapsedSeconds(session),
            EndedDuringBreak: session.BreakActive,
            Breaks: breaks,
            Slides: slides,
            SlideVisits: visits,
            SuggestedTitle: session.PresentationName,
            PresentationFilePath: session.SourcePath);
    }

    private double CurrentElapsedSeconds(SessionDraft session) =>
        session.AccumulatedSeconds +
        (session.RunningSinceTimestamp is { } timestamp
            ? _timeProvider.GetElapsedTime(timestamp, _timeProvider.GetTimestamp()).TotalSeconds
            : 0);

    private double CurrentBreakSeconds(SessionDraft session) =>
        session.BreakSinceTimestamp is { } timestamp
            ? _timeProvider.GetElapsedTime(timestamp, _timeProvider.GetTimestamp()).TotalSeconds
            : 0;

    private PowerPointSessionSnapshot CreateSnapshot(SessionDraft? session) =>
        session is null
            ? PowerPointSessionSnapshot.Inactive
            : new(
                session.State,
                session.RuntimePresentationId,
                session.PresentationName,
                session.OwnerClientId,
                session.OwnerDeviceName,
                session.StartedAt,
                CurrentElapsedSeconds(session),
                session.BreakActive,
                CurrentBreakSeconds(session),
                session.CurrentSlideIndex,
                session.SlideCount,
                session.SlideShowState);

    private bool PersistLocked()
    {
        if (_session is null)
        {
            return true;
        }

        if (_draftPath is null)
        {
            return true;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_draftPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = $"{_draftPath}.{Guid.NewGuid():N}.tmp";
            var now = _timeProvider.GetUtcNow();
            var currentBreakSeconds = CurrentBreakSeconds(_session);
            var persistedBreaks = _session.BreakActive &&
                _session.BreakSinceTimestamp is not null
                ?

                [
                    .. _session.Breaks,
                    new SessionBreak(
                            _session.Breaks.Length + 1,
                            CurrentElapsedSeconds(_session),
                            _session.BreakStartedAt ?? now,
                            now,
                            currentBreakSeconds,
                            _session.CurrentSlideIndex),
                ] : _session.Breaks;
            var persisted = _session with
            {
                EndedAt = now,
                AccumulatedSeconds = CurrentElapsedSeconds(_session),
                RunningSinceTimestamp = null,
                BreakAccumulatedSeconds = _session.BreakAccumulatedSeconds + currentBreakSeconds,
                BreakSinceTimestamp = null,
                VisitStartedTimestamp = null,
                Visits = CloseCurrentVisit(_session),
                Breaks = persistedBreaks
            };
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(JsonSerializer.Serialize(persisted, JsonOptions));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _draftPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private SessionDraft? ReadDraft()
    {
        try
        {
            if (_draftPath is null ||
                !File.Exists(_draftPath) ||
                new FileInfo(_draftPath).Length > 1024 * 1024)
            {
                return null;
            }

            var draft = JsonSerializer.Deserialize<SessionDraft>(
                File.ReadAllText(_draftPath),
                JsonOptions);
            return draft is not null && IsSafeDraft(draft)
                ? draft
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private bool DeleteDraftLocked()
    {
        try
        {
            if (_draftPath is not null && File.Exists(_draftPath))
            {
                File.Delete(_draftPath);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSafeDraft(SessionDraft draft)
    {
        if (draft.State is not ("tracking" or "pending-review") ||
            !IsSafeIdentifier(draft.ReportId) ||
            !IsSafeIdentifier(draft.RuntimePresentationId) ||
            !IsSafeText(draft.PresentationName, 300) ||
            !IsSafeText(draft.OwnerClientId, 128) ||
            !IsSafeText(draft.OwnerDeviceName, 120) ||
            draft.SourcePath is not null &&
            (draft.SourcePath.Length is < 1 or > 1024 ||
             !Path.IsPathFullyQualified(draft.SourcePath)) ||
            draft.EndedAt is not { } endedAt ||
            endedAt < draft.StartedAt ||
            endedAt - draft.StartedAt >
                TimeSpan.FromSeconds(PresentationReportProtocol.MaxDurationSeconds) ||
            !IsSafeDuration(draft.AccumulatedSeconds) ||
            !IsSafeDuration(draft.BreakAccumulatedSeconds) ||
            draft.RunningSinceTimestamp is not null ||
            draft.BreakSinceTimestamp is not null ||
            draft.VisitStartedTimestamp is not null ||
            draft.CurrentSlideIndex is not null &&
            (draft.CurrentSlideIndex < 1 ||
             draft.CurrentSlideIndex > PresentationReportProtocol.MaxSlideCount) ||
            draft.SlideCount is < 0 or > PresentationReportProtocol.MaxSlideCount ||
            draft.CurrentSlideIndex > draft.SlideCount ||
            draft.SlideShowState is not ("ready" or "running" or "paused" or "black" or "white") ||
            draft.Visits is null ||
            draft.Visits.Count > PresentationReportProtocol.MaxSlideVisitCount ||
            draft.Breaks is null ||
            draft.Breaks.Length > PresentationReportProtocol.MaxBreakCount ||
            draft.BreakActive && draft.BreakStartedAt is null ||
            !draft.BreakActive && draft.BreakStartedAt is not null)
        {
            return false;
        }

        DateTimeOffset? previousVisit = null;
        foreach (var visit in draft.Visits)
        {
            if (visit is null ||
                visit.SlideNumber is < 1 or > PresentationReportProtocol.MaxSlideCount ||
                visit.EnteredAt < draft.StartedAt ||
                visit.EnteredAt > endedAt ||
                previousVisit is { } previous && visit.EnteredAt < previous ||
                !IsSafeDuration(visit.DurationSeconds) ||
                visit.Origin is not ("voltura-air" or "powerpoint" or "keyboard" or "mouse" or "clicker"))
            {
                return false;
            }

            previousVisit = visit.EnteredAt;
        }

        var expectedBreakNumber = 1;
        DateTimeOffset? previousBreakEnd = null;
        foreach (var entry in draft.Breaks)
        {
            if (entry is null ||
                entry.Number != expectedBreakNumber ||
                !IsSafeDuration(entry.PresentationElapsedSeconds) ||
                !IsSafeDuration(entry.DurationSeconds) ||
                entry.StartedAt < draft.StartedAt ||
                entry.EndedAt < entry.StartedAt ||
                entry.EndedAt > endedAt ||
                previousBreakEnd is { } previous && entry.StartedAt < previous ||
                entry.SlideNumber is not null &&
                (entry.SlideNumber < 1 ||
                 entry.SlideNumber > PresentationReportProtocol.MaxSlideCount))
            {
                return false;
            }

            expectedBreakNumber++;
            previousBreakEnd = entry.EndedAt;
        }

        return true;
    }

    private static bool IsSafeIdentifier(string value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');

    private static bool IsSafeText(string value, int maximumLength) =>
        value is { Length: > 0 } &&
        value.Length <= maximumLength &&
        value.All(character => !char.IsControl(character));

    private static bool IsSafeDuration(double value) =>
        double.IsFinite(value) &&
        value is >= 0 and <= PresentationReportProtocol.MaxDurationSeconds;

    private static SessionOperationResult PersistenceFailure() =>
        new(
            false,
            "session-persistence-failed",
            "The presentation session could not be saved on the PC.");

    private bool IsResumeCurrent(string reportId)
    {
        lock (_gate)
        {
            return IsResumeCurrentLocked(reportId);
        }
    }

    private bool IsResumeCurrentLocked(string reportId) =>
        !_disposed &&
        string.Equals(_resumeReportId, reportId, StringComparison.Ordinal) &&
        _session is { State: "tracking" } session &&
        string.Equals(session.ReportId, reportId, StringComparison.Ordinal);

    private static SessionOperationResult SessionChangedDuringResume() =>
        new(
            false,
            "session-changed",
            "The presentation session changed while it was resuming.");

    private sealed record SessionDraft(
        string State,
        string ReportId,
        string RuntimePresentationId,
        string PresentationName,
        string? SourcePath,
        string OwnerClientId,
        string OwnerDeviceName,
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        double AccumulatedSeconds,
        long? RunningSinceTimestamp,
        bool BreakActive,
        DateTimeOffset? BreakStartedAt,
        double BreakAccumulatedSeconds,
        long? BreakSinceTimestamp,
        long? VisitStartedTimestamp,
        int? CurrentSlideIndex,
        int SlideCount,
        string SlideShowState,
        IReadOnlyList<PresentationReportSlideVisit> Visits,
        SessionBreak[] Breaks,
        bool? RecoverableAfterRestart);

    private sealed record SessionBreak(
        int Number,
        double PresentationElapsedSeconds,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        double DurationSeconds,
        int? SlideNumber);

    private sealed class StartLease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

internal readonly record struct SessionOperationResult(
    bool Succeeded,
    string? Code,
    string Message);
