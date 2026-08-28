namespace VolturaAir.Host.Features.AiAssistant;

internal sealed class AiAssistantPendingOpenPublication : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _openResultAdmitted;

    internal async Task PublishOpenResultAsync(
        Func<CancellationToken, Task> publish,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _openResultAdmitted = true;
            await publish(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task PublishAccessRevocationAsync(
        Func<Task> cleanup,
        Func<Task> publishOpenFailure,
        Func<Task> publishClosed)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await cleanup().ConfigureAwait(false);
            await (_openResultAdmitted ? publishClosed() : publishOpenFailure()).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
