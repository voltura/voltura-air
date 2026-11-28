using System.Security.Cryptography;
using System.Text.Json;

namespace VolturaAir.Host.Features.Updates;

/// <summary>Owns installed-host update discovery, staging, restore, and apply.</summary>
internal sealed class UpdateService : IAsyncDisposable
{
    private readonly PairingManager _pairingManager;
    private readonly bool _eligible;
    private readonly string? _modifyInstaller;
    private readonly string _pendingDirectory;
    private readonly Version _currentVersion;
    private readonly Action<string>? _requestApply;
    private readonly Func<CancellationToken, Task> _scheduleAutomaticWork;
    private readonly Func<System.Net.Http.HttpClient> _clientFactory;
    private readonly Func<byte[], byte[], bool> _verifyManifest;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly System.Threading.Lock _settingsTransitionLock = new();
    private readonly System.Threading.Lock _stateLock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly UpdateStageCancellation _stageCancellation = new();
    private System.Net.Http.HttpClient? _client;
    private volatile CancellationTokenSource? _automaticCancellation;
    private Task? _scheduled;
    private Task _settingsTransition = Task.CompletedTask;
    private TaskCompletionSource? _controllerAvailable;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private volatile DeferredCandidate? _deferred;
    private UpdateReadyPackage? _ready;
    private bool _pairingSubscribed;
    private int _resumeQueued;
    private int _disposed;

    internal UpdateService(
        PairingManager pairingManager,
        string[] args,
        Action<string>? requestApply = null,
        bool? eligibleOverride = null,
        Func<CancellationToken, Task>? scheduleAutomaticWork = null,
        Func<System.Net.Http.HttpClient>? clientFactory = null,
        string? modifyInstallerOverride = null,
        string? pendingDirectoryOverride = null,
        Version? currentVersionOverride = null,
        Func<byte[], byte[], bool>? manifestVerifier = null)
    {
        _pairingManager = pairingManager;
        _requestApply = requestApply;
        _eligible = eligibleOverride ?? UpdatePolicy.IsEligible(args, Environment.ProcessPath, out modifyInstallerOverride);
        _modifyInstaller = modifyInstallerOverride;
        _pendingDirectory = pendingDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voltura Air",
            "Updates",
            "pending");
        _currentVersion = currentVersionOverride ?? UpdatePolicy.CurrentVersion();
        _clientFactory = clientFactory ?? UpdatePolicy.CreateClient;
        _verifyManifest = manifestVerifier ?? UpdatePolicy.VerifyManifest;
        _scheduleAutomaticWork = scheduleAutomaticWork ?? ScheduleAsync;

        if (!_eligible)
        {
            return;
        }

        RestoreReadyState();
        AppUpdateSettings.Changed += OnSettingsChanged;
        if (AppUpdateSettings.AutomaticUpdateDownloadsEnabled()) StartAutomaticWork();
    }

    internal event EventHandler? StateChanged;
    internal event EventHandler<UpdateNotificationEventArgs>? NotificationRequested;
    internal UpdateState State { get; private set; } = UpdateState.Idle;
    internal string? TargetVersion { get; private set; }
    internal bool IsUpdateEligible => _eligible;
    internal bool HasNetworkClient => _client is not null;
    internal bool HasPairingSubscription => _pairingSubscribed;
    internal bool HasScheduledWork => _scheduled is not null;

    internal Task CheckForUpdatesAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!_eligible || (!manual && !AppUpdateSettings.AutomaticUpdateDownloadsEnabled())) return Task.CompletedTask;
        var automaticLifetime = manual ? CancellationToken.None : cancellationToken;
        return RunOperationAsync(token => CheckWithResultAsync(manual, automaticLifetime, token),
            null, requireNoController: !manual, cancellationToken);
    }

    private async Task CheckWithResultAsync(bool manual, CancellationToken automaticLifetime, CancellationToken cancellationToken)
    {
        try
        {
            await CheckCoreAsync(manual, automaticLifetime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RestorePresentation();
            ReportManualResult(manual && !cancellationToken.IsCancellationRequested, UpdateNotificationKind.CheckFailed);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or JsonException or IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.CheckFailed);
        }
    }

    internal Task ApplyAsync(CancellationToken cancellationToken = default) =>
        _ready is null || _requestApply is null ? Task.CompletedTask :
            RunOperationAsync(ApplyCoreAsync, PrepareApplyAsync, requireNoController: false, cancellationToken);

    private Task PrepareApplyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _deferred = null;
        return _stageCancellation.CancelAsync(UpdateStageInterruption.Superseded);
    }

    private async Task ApplyCoreAsync(CancellationToken cancellationToken)
    {
        _deferred = null;
        var ready = _ready;
        if (ready is null || _requestApply is null) return;
        string installer;
        try
        {
            installer = await UpdatePolicy.ValidateReadyForApplyAsync(
                _pendingDirectory, ready, SelectInstallerName, _verifyManifest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            UpdatePolicy.DeletePendingDirectory(_pendingDirectory);
            _ready = null;
            SetState(UpdateState.Idle, null);
            Notify(UpdateNotificationKind.InvalidStagedUpdate);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try { _requestApply(installer); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Notify(UpdateNotificationKind.InstallFailed);
            return;
        }
        // The shutdown launcher now owns the verified path. Retire updater admission before releasing the gate.
        await _shutdown.CancelAsync().ConfigureAwait(false);
    }

    private async Task CheckCoreAsync(bool manual, CancellationToken automaticLifetime, CancellationToken cancellationToken)
    {
        SetState(UpdateState.Checking, null);
        AppUpdateSettings.SetLastUpdateCheckAttemptUtc(DateTimeOffset.UtcNow);
        var candidate = await UpdatePolicy.ReadLatestReleaseAsync(_client ??= _clientFactory(), cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.CheckFailed);
            return;
        }

        var newestKnown = _ready is not null && _ready.Version > _currentVersion ? _ready.Version : _currentVersion;
        if (candidate.Version <= newestKnown)
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.UpToDate, _currentVersion.ToString(3));
            return;
        }

        await StageCandidateAsync(candidate with { AutomaticLifetime = automaticLifetime }, manual, cancellationToken).ConfigureAwait(false);
    }

    private async Task StageCandidateAsync(
        DeferredCandidate candidate,
        bool manual,
        CancellationToken cancellationToken)
    {
        if (_pairingManager.HasActiveController)
        {
            DeferCandidate(candidate, manual);
            return;
        }

        _deferred = null;
        var candidateToken = _stageCancellation.Start(cancellationToken);
        UpdateStageInterruption interruption;
        UpdateStageResult result;
        try
        {
            EnsurePairingSubscription();
            SetState(UpdateState.Downloading, candidate.Version.ToString(3));
            var installerName = SelectInstallerName(candidate.Version);
            var stager = new UpdatePackageStager(
                _client ??= _clientFactory(),
                _pairingManager,
                installerName,
                _verifyManifest,
                _pendingDirectory);
            result = await stager.StageAsync(candidate.Assets, candidate.Version, candidateToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = new(UpdateStageStatus.Failed);
        }
        finally
        {
            interruption = await _stageCancellation.FinishAsync().ConfigureAwait(false);
        }

        switch (result.Status)
        {
            case UpdateStageStatus.Ready when result.ReadyPackage is not null:
                var firstReady = _ready is null;
                _ready = result.ReadyPackage;
                SetState(UpdateState.Ready, _ready.Version.ToString(3));
                if (firstReady) Notify(UpdateNotificationKind.Ready, _ready.Version.ToString(3));
                break;
            case UpdateStageStatus.Deferred when interruption != UpdateStageInterruption.Superseded && !cancellationToken.IsCancellationRequested:
            case UpdateStageStatus.Cancelled when interruption == UpdateStageInterruption.Controller && !cancellationToken.IsCancellationRequested:
                DeferCandidate(candidate, manual);
                break;
            case UpdateStageStatus.Failed:
                RestorePresentation();
                ReportManualResult(manual, UpdateNotificationKind.CheckFailed);
                break;
            default:
                RestorePresentation();
                break;
        }
    }

    private void DeferCandidate(DeferredCandidate candidate, bool manual)
    {
        _deferred = candidate;
        EnsurePairingSubscription();
        SetState(UpdateState.WaitingForDevices, candidate.Version.ToString(3));
        ReportManualResult(manual, UpdateNotificationKind.WaitingForDevices, candidate.Version.ToString(3));
        QueueDeferredResume();
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (_pairingManager.HasActiveController)
        {
            _ = _stageCancellation.CancelAsync(UpdateStageInterruption.Controller);
            return;
        }

        TaskCompletionSource? available;
        lock (_stateLock)
        {
            available = _controllerAvailable;
            _controllerAvailable = null;
        }
        available?.TrySetResult();
        QueueDeferredResume();
    }

    private void QueueDeferredResume()
    {
        if (Volatile.Read(ref _disposed) != 0 || _shutdown.IsCancellationRequested ||
            _deferred is null || _pairingManager.HasActiveController ||
            Interlocked.CompareExchange(ref _resumeQueued, 1, 0) != 0)
        {
            return;
        }

        _ = ResumeDeferredAsync();
    }

    private async Task ResumeDeferredAsync()
    {
        try
        {
            await RunOperationAsync(StageDeferredCandidateAsync, null, requireNoController: false, default).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _resumeQueued, 0);
            QueueDeferredResume();
        }
    }

    private async Task StageDeferredCandidateAsync(CancellationToken cancellationToken)
    {
        if (_deferred is not { } candidate || _pairingManager.HasActiveController) return;
        using var deferredLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, candidate.AutomaticLifetime);
        if (!deferredLifetime.IsCancellationRequested)
            await StageCandidateAsync(candidate, manual: false, deferredLifetime.Token).ConfigureAwait(false);
    }

    private async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && AppUpdateSettings.AutomaticUpdateDownloadsEnabled())
        {
            await CheckForUpdatesAsync(manual: false, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForNoControllerAsync(CancellationToken cancellationToken)
    {
        EnsurePairingSubscription();
        while (_pairingManager.HasActiveController)
        {
            TaskCompletionSource available;
            lock (_stateLock)
            {
                if (!_pairingManager.HasActiveController) return;
                available = _controllerAvailable ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await available.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_settingsTransitionLock)
        {
            if (Volatile.Read(ref _disposed) == 0) _settingsTransition = RestartAfterAsync(_settingsTransition);
        }
    }

    private async Task RestartAfterAsync(Task previous)
    {
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await StopAutomaticWorkAsync().ConfigureAwait(false);
        if (Volatile.Read(ref _disposed) == 0 && AppUpdateSettings.AutomaticUpdateDownloadsEnabled()) StartAutomaticWork();
    }

    private void StartAutomaticWork()
    {
        if (!_eligible || _shutdown.IsCancellationRequested || _automaticCancellation is not null) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _automaticCancellation = cancellation;
        EnsurePairingSubscription();
        _scheduled = _scheduleAutomaticWork(cancellation.Token);
    }

    private async Task StopAutomaticWorkAsync()
    {
        var cancellation = _automaticCancellation;
        var scheduled = _scheduled;
        _automaticCancellation = null;
        _scheduled = null;
        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            if (scheduled is not null)
            {
                try { await scheduled.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            cancellation.Dispose();
        }
        if (_singleFlight.Wait(0))
        {
            try { ReleaseClientIfIdle(); }
            finally { _singleFlight.Release(); }
        }
        lock (_stateLock) { ReleasePairingSubscriptionIfIdle(); }
    }

    private void EnsurePairingSubscription()
    {
        lock (_stateLock)
        {
            if (_pairingSubscribed) return;
            _pairingManager.ConnectionChanged += OnConnectionChanged;
            _pairingSubscribed = true;
        }
    }

    private void ReleasePairingSubscriptionIfIdle()
    {
        if (!_pairingSubscribed || _activeOperations != 0 || _automaticCancellation is not null || _deferred is not null)
        {
            return;
        }
        _pairingManager.ConnectionChanged -= OnConnectionChanged;
        _pairingSubscribed = false;
    }

    private void RestoreReadyState()
    {
        if (UpdatePolicy.TryRestoreReadyPackage(_pendingDirectory, SelectInstallerName, _verifyManifest, out var ready) &&
            ready.Version > _currentVersion)
        {
            _ready = ready;
            SetState(UpdateState.Ready, ready.Version.ToString(3));
        }
    }

    private string SelectInstallerName(Version version) => UpdatePolicy.SelectInstallerNameFromModifyInstaller(version, _modifyInstaller);

    private void RestorePresentation()
    {
        SetState(_ready is null ? UpdateState.Idle : UpdateState.Ready, _ready?.Version.ToString(3));
    }

    private void SetState(UpdateState state, string? version)
    {
        State = state;
        TargetVersion = version;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportManualResult(bool manual, UpdateNotificationKind kind, string? version = null)
    {
        if (manual) Notify(kind, version);
    }

    private void Notify(UpdateNotificationKind kind, string? version = null) =>
        NotificationRequested?.Invoke(this, new UpdateNotificationEventArgs(kind, version));

    // The caller owns _singleFlight while it examines and releases the shared HTTP client.
    private void ReleaseClientIfIdle()
    {
        if (_deferred?.AutomaticLifetime.IsCancellationRequested == true)
        {
            _deferred = null;
            if (State == UpdateState.WaitingForDevices) RestorePresentation();
        }
        if (_automaticCancellation is not null || _deferred is not null)
        {
            return;
        }

        Interlocked.Exchange(ref _client, null)?.Dispose();
    }

    private async Task RunOperationAsync(
        Func<CancellationToken, Task> action,
        Func<CancellationToken, Task>? beforeSingleFlight,
        bool requireNoController,
        CancellationToken cancellationToken)
    {
        var operation = BeginOperation(cancellationToken);
        try
        {
            if (operation is null) return;
            var token = operation.Token;
            if (beforeSingleFlight is not null) await beforeSingleFlight(token).ConfigureAwait(false);
            while (true)
            {
                if (requireNoController) await WaitForNoControllerAsync(token).ConfigureAwait(false);
                await _singleFlight.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (requireNoController && _pairingManager.HasActiveController) continue;
                    await action(token).ConfigureAwait(false);
                    return;
                }
                finally
                {
                    try { ReleaseClientIfIdle(); }
                    finally { _singleFlight.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (operation?.IsCancellationRequested == true) { }
        finally
        {
            operation?.Dispose();
            if (operation is not null) EndOperation();
        }
    }

    private CancellationTokenSource? BeginOperation(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || _shutdown.IsCancellationRequested) return null;
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            _activeOperations++;
            return operation;
        }
    }

    private void EndOperation()
    {
        lock (_stateLock)
        {
            _activeOperations--;
            if (_activeOperations != 0) return;
            ReleasePairingSubscriptionIfIdle();
            _operationsDrained?.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_eligible) AppUpdateSettings.Changed -= OnSettingsChanged;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        Task settingsTransition;
        lock (_settingsTransitionLock) { settingsTransition = _settingsTransition; }
        try { await settingsTransition.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _deferred = null;
        await StopAutomaticWorkAsync().ConfigureAwait(false);
        Task operationsDrained;
        lock (_stateLock)
        {
            operationsDrained = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        await operationsDrained.ConfigureAwait(false);
        lock (_stateLock)
        {
            if (_pairingSubscribed)
            {
                _pairingManager.ConnectionChanged -= OnConnectionChanged;
                _pairingSubscribed = false;
            }
        }
        Interlocked.Exchange(ref _client, null)?.Dispose();
        _stageCancellation.Dispose();
        _singleFlight.Dispose();
        _shutdown.Dispose();
    }
}
