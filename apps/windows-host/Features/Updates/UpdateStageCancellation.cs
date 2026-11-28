namespace VolturaAir.Host.Features.Updates;

internal enum UpdateStageInterruption { None, Controller, Superseded }

/// <summary>Serializes interruption and retirement of the one active update download.</summary>
internal sealed class UpdateStageCancellation : IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _active;
    private Task _cancelling = Task.CompletedTask;
    private UpdateStageInterruption _interruption;

    public CancellationToken Start(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancelling = Task.CompletedTask;
            _interruption = UpdateStageInterruption.None;
            return _active.Token;
        }
    }

    public Task CancelAsync(UpdateStageInterruption interruption)
    {
        lock (_gate)
        {
            if (_active is not { } cancellation) return Task.CompletedTask;
            if (_interruption != UpdateStageInterruption.Superseded) _interruption = interruption;
            if (!cancellation.IsCancellationRequested) _cancelling = cancellation.CancelAsync();
            return _cancelling;
        }
    }

    public async Task<UpdateStageInterruption> FinishAsync()
    {
        CancellationTokenSource? cancellation;
        Task cancelling;
        UpdateStageInterruption interruption;
        lock (_gate)
        {
            cancellation = _active;
            _active = null;
            cancelling = _cancelling;
            interruption = _interruption;
        }

        try { await cancelling.ConfigureAwait(false); }
        finally { cancellation?.Dispose(); }
        return interruption;
    }

    public void Dispose() => _active?.Dispose();
}
