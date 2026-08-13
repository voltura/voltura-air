using System.Diagnostics.CodeAnalysis;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal enum PhoneWebcamFeatureState
{
    Unavailable,
    NotInstalled,
    NeedsCleanup,
    UpdateRequired,
    Installed,
    Failed
}

internal sealed record PhoneWebcamFeatureStatus(PhoneWebcamFeatureState State, string Message, bool HasError = false)
{
    internal bool IsInstalled => State == PhoneWebcamFeatureState.Installed;

    internal bool ShouldRemove => State is PhoneWebcamFeatureState.Installed or
        PhoneWebcamFeatureState.NeedsCleanup or
        PhoneWebcamFeatureState.UpdateRequired;
}

internal interface IPhoneWebcamFeature
{
    PhoneWebcamFeatureStatus Status { get; }

    Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default);

    Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default);
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
    private readonly SemaphoreSlim _operation = new(1, 1);
    private PhoneWebcamFramePipeServer? _pipe;
    private int _disposeState;

    private PhoneWebcamFeature(IPhoneWebcamSetup setup)
    {
        _setup = setup;
        Status = new PhoneWebcamFeatureStatus(
            PhoneWebcamFeatureState.Unavailable,
            "Phone webcam has not been initialized.");
    }

    public PhoneWebcamFeatureStatus Status { get; private set; }

    internal static async Task<PhoneWebcamFeature> CreateAsync(CancellationToken cancellationToken = default)
    {
        var feature = new PhoneWebcamFeature(new PhoneWebcamSetup());
        feature.Status = await feature._setup.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (feature.Status.IsInstalled)
        {
            feature.TryStartPipe();
        }

        return feature;
    }

    internal static IPhoneWebcamFeature CreateUnavailable() => new UnavailablePhoneWebcamFeature();

    internal void Publish(PhoneWebcamFrame frame)
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

            Status = await _setup.InstallAsync(cancellationToken).ConfigureAwait(false);
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
            await StopPipeAsync().ConfigureAwait(false);
            Status = await _setup.RemoveAsync(cancellationToken).ConfigureAwait(false);
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
            _pipe = new PhoneWebcamFramePipeServer();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Status = new PhoneWebcamFeatureStatus(
                PhoneWebcamFeatureState.Installed,
                $"Voltura Air Webcam is installed, but its frame connection could not start: {exception.Message}",
                HasError: true);
        }
    }

    private async ValueTask StopPipeAsync()
    {
        PhoneWebcamFramePipeServer? pipe = Interlocked.Exchange(ref _pipe, null);
        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class UnavailablePhoneWebcamFeature : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(
            PhoneWebcamFeatureState.Unavailable,
            "Phone webcam is unavailable in this host mode.");

        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);
    }
}
