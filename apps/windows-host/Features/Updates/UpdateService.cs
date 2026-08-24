using System.Security.Cryptography;
using System.Text.Json;

namespace VolturaAir.Host.Features.Updates;

/// <summary>Owns installed-host update discovery, staging, restore, and apply.</summary>
internal sealed class UpdateService : IAsyncDisposable
{
    private const string ReleaseUrl = "https://api.github.com/repos/voltura/voltura-air/releases/latest";
    private const long MaxMetadataBytes = 256 * 1024;
    private readonly PairingManager _pairingManager;
    private readonly bool _eligible;
    private readonly string? _modifyInstaller;
    private readonly string _pendingDirectory;
    private readonly Version _currentVersion;
    private readonly Action<string>? _requestApply;
    private readonly Func<CancellationToken, Task> _scheduleAutomaticWork;
    private readonly Func<System.Net.Http.HttpClient> _clientFactory;
    private readonly Func<byte[], byte[], bool> _verifyManifest;
    private readonly EventHandler _connectionChanged;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly System.Threading.Lock _settingsTransitionLock = new();
    private readonly System.Threading.Lock _stateLock = new();
    private System.Net.Http.HttpClient? _client;
    private CancellationTokenSource? _shutdown;
    private CancellationTokenSource? _automaticCancellation;
    private CancellationTokenSource? _candidateCancellation;
    private Task? _scheduled;
    private Task _settingsTransition = Task.CompletedTask;
    private TaskCompletionSource? _controllerAvailable;
    private DeferredCandidate? _deferred;
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
        _connectionChanged = OnConnectionChanged;

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

    internal async Task CheckForUpdatesAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!_eligible || (!manual && !AppUpdateSettings.AutomaticUpdateDownloadsEnabled())) return;
        if (!manual)
        {
            EnsurePairingSubscription();
            await WaitForNoControllerAsync(cancellationToken).ConfigureAwait(false);
        }
        await _singleFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CheckCoreAsync(manual, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !manual)
        {
            RestorePresentation();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or JsonException or IOException or CryptographicException or InvalidOperationException)
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.CheckFailed);
        }
        finally
        {
            ReleasePairingSubscriptionIfIdle();
            ReleaseClientIfIdle(ownsSingleFlight: true);
            _singleFlight.Release();
        }
    }

    internal async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (_ready is null || _requestApply is null) return;
        _deferred = null;
        await CancelCandidateAsync().ConfigureAwait(false);
        await _singleFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ready = _ready;
            if (ready is null) return;
            string installer;
            try
            {
                installer = await UpdatePolicy.ValidateReadyForApplyAsync(_pendingDirectory, ready, SelectInstallerName, _verifyManifest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or InvalidOperationException)
            {
                UpdatePolicy.DeletePendingDirectory(_pendingDirectory);
                _ready = null;
                SetState(UpdateState.Idle, null);
                Notify(UpdateNotificationKind.InvalidStagedUpdate);
                return;
            }

            try { _requestApply(installer); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Notify(UpdateNotificationKind.InstallFailed);
            }
        }
        finally
        {
            _singleFlight.Release();
            ReleasePairingSubscriptionIfIdle();
        }
    }

    private async Task CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        ShowWorkingState(UpdateState.Checking, null);
        AppUpdateSettings.SetLastUpdateCheckAttemptUtc(DateTimeOffset.UtcNow);
        using var response = await (_client ??= _clientFactory()).GetAsync(
            ReleaseUrl,
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.CheckFailed);
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var metadata = await UpdatePackageStager.ReadCappedAsync(stream, MaxMetadataBytes, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(metadata);
        var root = document.RootElement;
        if (!UpdatePolicy.TryReadLatestRelease(root, out var candidate, out var assets))
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.CheckFailed);
            return;
        }

        var newestKnown = _ready is not null && _ready.Version > _currentVersion ? _ready.Version : _currentVersion;
        if (candidate <= newestKnown)
        {
            RestorePresentation();
            ReportManualResult(manual, UpdateNotificationKind.UpToDate, _currentVersion.ToString(3));
            return;
        }

        var deferred = new DeferredCandidate(candidate, assets.Clone());
        if (_pairingManager.HasActiveController)
        {
            DeferCandidate(deferred, manual);
            return;
        }

        await StageCandidateAsync(deferred, manual, cancellationToken).ConfigureAwait(false);
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
        EnsurePairingSubscription();
        ShowWorkingState(UpdateState.Downloading, candidate.Version.ToString(3));
        var installerName = UpdatePolicy.SelectInstallerNameFromModifyInstaller(candidate.Version, _modifyInstaller);
        using var candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            GetShutdownToken());
        lock (_stateLock) { _candidateCancellation = candidateCancellation; }
        UpdateStageResult result;
        try
        {
            var stager = new UpdatePackageStager(
                _client ??= _clientFactory(),
                _pairingManager,
                installerName,
                _verifyManifest,
                _pendingDirectory);
            result = await stager.StageAsync(candidate.Assets, candidate.Version, candidateCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_candidateCancellation, candidateCancellation)) _candidateCancellation = null;
            }
        }

        switch (result.Status)
        {
            case UpdateStageStatus.Ready when result.ReadyPackage is not null:
                var firstReady = _ready is null;
                _ready = result.ReadyPackage;
                SetState(UpdateState.Ready, _ready.Version.ToString(3));
                if (firstReady) Notify(UpdateNotificationKind.Ready, _ready.Version.ToString(3));
                break;
            case UpdateStageStatus.Deferred:
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
        ShowWorkingState(UpdateState.WaitingForDevices, candidate.Version.ToString(3));
        ReportManualResult(manual, UpdateNotificationKind.WaitingForDevices, candidate.Version.ToString(3));
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (_pairingManager.HasActiveController)
        {
            CancellationTokenSource? cancellation;
            lock (_stateLock) { cancellation = _candidateCancellation; }
            if (cancellation is not null) _ = cancellation.CancelAsync();
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
        if (_deferred is null || _pairingManager.HasActiveController ||
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
            await _singleFlight.WaitAsync(GetShutdownToken()).ConfigureAwait(false);
            try
            {
                var candidate = _deferred;
                if (candidate is not null && !_pairingManager.HasActiveController)
                {
                    await StageCandidateAsync(candidate, manual: false, GetShutdownToken()).ConfigureAwait(false);
                }
            }
            finally
            {
                ReleaseClientIfIdle(ownsSingleFlight: true);
                _singleFlight.Release();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.Exchange(ref _resumeQueued, 0);
            ReleasePairingSubscriptionIfIdle();
            if (_deferred is not null && !_pairingManager.HasActiveController) QueueDeferredResume();
        }
    }

    private async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && AppUpdateSettings.AutomaticUpdateDownloadsEnabled())
        {
            var last = AppUpdateSettings.LastUpdateCheckAttemptUtc();
            var wait = last is null
                ? TimeSpan.FromMinutes(2)
                : TimeSpan.FromHours(24) - (DateTimeOffset.UtcNow - last.Value);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            await WaitForNoControllerAsync(cancellationToken).ConfigureAwait(false);
            await CheckForUpdatesAsync(manual: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForNoControllerAsync(CancellationToken cancellationToken)
    {
        while (_pairingManager.HasActiveController)
        {
            TaskCompletionSource available;
            lock (_stateLock)
            {
                if (!_pairingManager.HasActiveController) return;
                available = _controllerAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        if (!_eligible || _automaticCancellation is not null) return;
        EnsurePairingSubscription();
        _automaticCancellation = CancellationTokenSource.CreateLinkedTokenSource(GetShutdownToken());
        _scheduled = _scheduleAutomaticWork(_automaticCancellation.Token);
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
        ReleasePairingSubscriptionIfIdle();
        ReleaseClientIfIdle();
    }

    private void EnsurePairingSubscription()
    {
        lock (_stateLock)
        {
            if (_pairingSubscribed) return;
            _pairingManager.ConnectionChanged += _connectionChanged;
            _pairingSubscribed = true;
        }
    }

    private void ReleasePairingSubscriptionIfIdle()
    {
        lock (_stateLock)
        {
            if (!_pairingSubscribed || _automaticCancellation is not null || _candidateCancellation is not null || _deferred is not null)
            {
                return;
            }
            _pairingManager.ConnectionChanged -= _connectionChanged;
            _pairingSubscribed = false;
        }
    }

    private async Task CancelCandidateAsync()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock) { cancellation = _candidateCancellation; }
        if (cancellation is not null) await cancellation.CancelAsync().ConfigureAwait(false);
    }

    private void RestoreReadyState()
    {
        if (!UpdatePolicy.TryRestoreReadyPackage(_pendingDirectory, SelectInstallerName, _verifyManifest, out var ready) ||
            ready.Version <= _currentVersion)
        {
            return;
        }
        _ready = ready;
        SetState(UpdateState.Ready, ready.Version.ToString(3));
    }

    private string SelectInstallerName(Version version) => UpdatePolicy.SelectInstallerNameFromModifyInstaller(version, _modifyInstaller);

    private void ShowWorkingState(UpdateState state, string? version)
    {
        if (_ready is null) SetState(state, version);
    }

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

    private void ReleaseClientIfIdle(bool ownsSingleFlight = false)
    {
        if (AppUpdateSettings.AutomaticUpdateDownloadsEnabled() ||
            (!ownsSingleFlight && _singleFlight.CurrentCount == 0) ||
            _deferred is not null)
        {
            return;
        }

        Interlocked.Exchange(ref _client, null)?.Dispose();
    }

    private CancellationToken GetShutdownToken() => (_shutdown ??= new()).Token;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_eligible) AppUpdateSettings.Changed -= OnSettingsChanged;
        Task settingsTransition;
        lock (_settingsTransitionLock) { settingsTransition = _settingsTransition; }
        try { await settingsTransition.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _deferred = null;
        await StopAutomaticWorkAsync().ConfigureAwait(false);
        await CancelCandidateAsync().ConfigureAwait(false);
        if (_shutdown is not null) await _shutdown.CancelAsync().ConfigureAwait(false);
        lock (_stateLock)
        {
            if (_pairingSubscribed)
            {
                _pairingManager.ConnectionChanged -= _connectionChanged;
                _pairingSubscribed = false;
            }
        }
        _client?.Dispose();
        _candidateCancellation?.Dispose();
        _singleFlight.Dispose();
        _shutdown?.Dispose();
    }
}
