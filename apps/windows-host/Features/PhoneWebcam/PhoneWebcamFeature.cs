using System.Diagnostics.CodeAnalysis;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal enum PhoneWebcamFeatureState
{
    Unavailable,
    NotInstalled,
    NeedsCleanup,
    UpdateRequired,
    Installed,
    Removing,
    Failed
}

internal sealed record PhoneWebcamFeatureStatus(PhoneWebcamFeatureState State, string Message, bool HasError = false)
{
    internal bool IsInstalled => State == PhoneWebcamFeatureState.Installed && !HasError;

    internal bool ShouldRemove => State is PhoneWebcamFeatureState.Installed or
        PhoneWebcamFeatureState.NeedsCleanup or
        PhoneWebcamFeatureState.UpdateRequired or
        PhoneWebcamFeatureState.Removing;
}

internal sealed record PhoneWebcamActivity(
    string State,
    int? Width = null,
    int? Height = null,
    bool HasMicrophone = false);

internal interface IPhoneWebcamFeature
{
    PhoneWebcamFeatureStatus Status { get; }

    PhoneWebcamActivity Activity { get; }

    PhoneWebcamAudioTargetStatus AudioTargetStatus => new(
        PhoneWebcamAudioTargetState.DetectionFailed,
        "Phone microphone support is unavailable.");

    event EventHandler? ActivityChanged;

    event EventHandler? StatusChanged;

    Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default);

    Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default);

    Task<PhoneWebcamAudioTargetStatus> RefreshAudioTargetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AudioTargetStatus);

    void Publish(PhoneWebcamFrame frame) => frame.Dispose();
}

internal interface IPhoneWebcamSetup
{
    Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken);

    Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken);
}

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The dynamically replaceable pipe is exchanged and disposed by StopPipeAsync during removal and feature disposal.")]
internal sealed class PhoneWebcamFeature : IPhoneWebcamFeature, IAsyncDisposable
{
    private static readonly TimeSpan DefaultAudioTargetRefreshTimeout = TimeSpan.FromSeconds(5);
    private readonly IPhoneWebcamSetup _setup;
    private readonly IPhoneWebcamAudioTarget _audioTarget;
    private readonly Func<PhoneWebcamFramePipeServer> _createPipe;
    private readonly TimeSpan _audioTargetRefreshTimeout;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly Lock _audioTargetRefreshGate = new();
    private PhoneWebcamFramePipeServer? _pipe;
    private Task<PhoneWebcamAudioTargetStatus>? _audioTargetRefresh;
    private Func<Task>? _stopSessionsAsync;
    private readonly Lock _retirementGate = new();
    private Task _pipeRetirement = Task.CompletedTask;
    private int _disposeState;

    internal PhoneWebcamFeature(
        IPhoneWebcamSetup setup,
        Func<PhoneWebcamFramePipeServer>? createPipe = null,
        IPhoneWebcamAudioTarget? audioTarget = null,
        TimeSpan? audioTargetRefreshTimeout = null)
    {
        _setup = setup;
        _audioTarget = audioTarget ?? new PhoneWebcamAudioTarget();
        _audioTargetRefreshTimeout = audioTargetRefreshTimeout ?? DefaultAudioTargetRefreshTimeout;
        _audioTarget.StatusChanged += OnAudioTargetStatusChanged;
        _createPipe = createPipe ?? (() => new PhoneWebcamFramePipeServer());
        Status = new PhoneWebcamFeatureStatus(
            PhoneWebcamFeatureState.Unavailable,
            "Phone webcam has not been initialized.");
    }

    public PhoneWebcamFeatureStatus Status { get; private set; }
    public PhoneWebcamActivity Activity { get; private set; } = new("idle");
    public PhoneWebcamAudioTargetStatus AudioTargetStatus => _audioTarget.Status;
    public event EventHandler? ActivityChanged;
    public event EventHandler? StatusChanged;

    internal void SetSessionStopper(Func<Task> stopSessionsAsync) => _stopSessionsAsync = stopSessionsAsync;

    internal void ReportActivity(string state, int? width = null, int? height = null, bool hasMicrophone = false)
    {
        if (!string.Equals(state, "streaming", StringComparison.Ordinal))
        {
            Volatile.Read(ref _pipe)?.Clear();
        }

        Activity = new PhoneWebcamActivity(state, width, height, hasMicrophone);
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static async Task<PhoneWebcamFeature> CreateAsync(CancellationToken cancellationToken = default)
        => await CreateAsync(new PhoneWebcamSetup(), cancellationToken).ConfigureAwait(false);

    internal static async Task<PhoneWebcamFeature> CreateAsync(
        IPhoneWebcamSetup setup,
        CancellationToken cancellationToken = default)
    {
        var feature = new PhoneWebcamFeature(setup);
        feature.SetStatus(await feature._setup.GetStatusAsync(cancellationToken).ConfigureAwait(false));
        await feature.RefreshAudioTargetAsync(cancellationToken).ConfigureAwait(false);
        if (feature.Status.IsInstalled)
        {
            feature.TryStartPipe();
        }

        return feature;
    }

    internal static IPhoneWebcamFeature CreateUnavailable() => new UnavailablePhoneWebcamFeature();

    public void Publish(PhoneWebcamFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        PhoneWebcamFramePipeServer? pipe = Volatile.Read(ref _pipe);
        if (pipe is null)
        {
            frame.Dispose();
            return;
        }

        pipe.Publish(frame);
    }

    public async Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status.ShouldRemove)
            {
                return Status;
            }

            SetStatus(await _setup.InstallAsync(cancellationToken).ConfigureAwait(false));
            if (Status.IsInstalled)
            {
                TryStartPipe();
            }

            return Status;
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetStatus(new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Removing,
                "Removing Voltura Air Webcam…"));
            try
            {
                if (_stopSessionsAsync is not null)
                {
                    await _stopSessionsAsync().ConfigureAwait(false);
                }
                await StopPipeAsync().ConfigureAwait(false);
                SetStatus(await _setup.RemoveAsync(cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                await RecoverStatusAfterInterruptedRemovalAsync().ConfigureAwait(false);
                throw;
            }
            if (Status.IsInstalled)
            {
                TryStartPipe();
            }

            return Status;
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<PhoneWebcamAudioTargetStatus> RefreshAudioTargetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        Task<PhoneWebcamAudioTargetStatus> refresh;
        lock (_audioTargetRefreshGate)
        {
            if (_audioTargetRefresh is null || _audioTargetRefresh.IsCompleted)
            {
                _audioTargetRefresh = Task.Run(_audioTarget.Refresh, CancellationToken.None);
            }
            refresh = _audioTargetRefresh;
        }
        try
        {
            return await refresh.WaitAsync(_audioTargetRefreshTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return _audioTarget.ReportDetectionFailure();
        }
        catch (OperationCanceledException)
        {
            _audioTarget.InvalidateRefresh();
            throw;
        }
    }

    internal IPhoneWebcamAudioTarget AudioTarget => _audioTarget;

    private void OnAudioTargetStatusChanged(object? sender, EventArgs args) =>
        StatusChanged?.Invoke(this, EventArgs.Empty);

    private async Task RecoverStatusAfterInterruptedRemovalAsync()
    {
        try
        {
            SetStatus(await _setup.GetStatusAsync(CancellationToken.None).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetStatus(new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.NeedsCleanup,
                $"Phone webcam removal was interrupted and its installation state could not be verified: {exception.Message}",
                HasError: true));
        }

        if (Status.IsInstalled)
        {
            TryStartPipe();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _audioTarget.StatusChanged -= OnAudioTargetStatusChanged;

        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopPipeAsync().ConfigureAwait(false);
            Task retirement;
            lock (_retirementGate)
            {
                retirement = _pipeRetirement;
            }
            await retirement.ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
            _operation.Dispose();
        }
    }

    private void TryStartPipe()
    {
        if (_pipe is not null)
        {
            return;
        }

        try
        {
            PhoneWebcamFramePipeServer pipe = _createPipe();
            pipe.Failed += OnPipeFailed;
            _pipe = pipe;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetStatus(new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Installed,
                $"Voltura Air Webcam is installed, but its frame connection could not start: {exception.Message}",
                HasError: true));
        }
    }

    private void OnPipeFailed(PhoneWebcamFramePipeServer pipe, Exception exception)
    {
        lock (_retirementGate)
        {
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _pipe, null, pipe), pipe))
            {
                return;
            }

            pipe.Failed -= OnPipeFailed;
            _pipeRetirement = _pipeRetirement.ContinueWith(
                _ => RetireFailedPipeAsync(pipe),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }

        SetStatus(new PhoneWebcamFeatureStatus(
            PhoneWebcamFeatureState.Installed,
            $"Voltura Air Webcam is installed, but its frame connection stopped: {exception.Message}",
            HasError: true));
    }

    private async Task RetireFailedPipeAsync(PhoneWebcamFramePipeServer pipe)
    {
        try
        {
            if (_stopSessionsAsync is not null)
            {
                await _stopSessionsAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The feature remains failed even if a broken native session also fails cleanup.
        }
        finally
        {
            try
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
    }

    private void SetStatus(PhoneWebcamFeatureStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async ValueTask StopPipeAsync()
    {
        PhoneWebcamFramePipeServer? pipe = Interlocked.Exchange(ref _pipe, null);
        if (pipe is not null)
        {
            pipe.Failed -= OnPipeFailed;
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class UnavailablePhoneWebcamFeature : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(
            PhoneWebcamFeatureState.Unavailable,
            "Phone webcam is unavailable in this host mode.");

        public PhoneWebcamActivity Activity { get; } = new("idle");
        public PhoneWebcamAudioTargetStatus AudioTargetStatus { get; } = new(
            PhoneWebcamAudioTargetState.DetectionFailed,
            "Phone microphone support is unavailable in this host mode.");

        public event EventHandler? ActivityChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<PhoneWebcamAudioTargetStatus> RefreshAudioTargetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AudioTargetStatus);

        public void Publish(PhoneWebcamFrame frame) => frame.Dispose();
    }
}
