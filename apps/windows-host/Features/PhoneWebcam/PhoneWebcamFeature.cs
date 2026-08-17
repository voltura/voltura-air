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

internal sealed record PhoneWebcamActivity(string State, int? Width = null, int? Height = null);

internal interface IPhoneWebcamFeature
{
    PhoneWebcamFeatureStatus Status { get; }

    PhoneWebcamActivity Activity { get; }

    event EventHandler? ActivityChanged;

    event EventHandler? StatusChanged;

    Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default);

    Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default);

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
    private readonly IPhoneWebcamSetup _setup;
    private readonly Func<PhoneWebcamFramePipeServer> _createPipe;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private PhoneWebcamFramePipeServer? _pipe;
    private Func<Task>? _stopSessionsAsync;
    private readonly Lock _retirementGate = new();
    private Task _pipeRetirement = Task.CompletedTask;
    private int _disposeState;

    internal PhoneWebcamFeature(
        IPhoneWebcamSetup setup,
        Func<PhoneWebcamFramePipeServer>? createPipe = null)
    {
        _setup = setup;
        _createPipe = createPipe ?? (() => new PhoneWebcamFramePipeServer());
        Status = new PhoneWebcamFeatureStatus(
            PhoneWebcamFeatureState.Unavailable,
            "Phone webcam has not been initialized.");
    }

    public PhoneWebcamFeatureStatus Status { get; private set; }
    public PhoneWebcamActivity Activity { get; private set; } = new("idle");
    public event EventHandler? ActivityChanged;
    public event EventHandler? StatusChanged;

    internal void SetSessionStopper(Func<Task> stopSessionsAsync) => _stopSessionsAsync = stopSessionsAsync;

    internal void ReportActivity(string state, int? width = null, int? height = null)
    {
        if (!string.Equals(state, "streaming", StringComparison.Ordinal))
        {
            Volatile.Read(ref _pipe)?.Clear();
        }

        Activity = new PhoneWebcamActivity(state, width, height);
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
        if (feature.Status.State == PhoneWebcamFeatureState.Unavailable)
        {
            string message = feature.Status.Message;
            await feature.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(message);
        }
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

        public void Publish(PhoneWebcamFrame frame) => frame.Dispose();
    }
}
