using System.Windows.Threading;

namespace VolturaAir.Host;

internal sealed class WpfShutdownCoordinator(
    Dispatcher dispatcher,
    Func<ValueTask> disposeAsync,
    Action shutdownApplication,
    Action<Exception>? reportFailure = null)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _shutdownRequested;

    internal Task Completion => _completion.Task;

    public void RequestShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            BeginShutdown();
        }
        else
        {
            _ = dispatcher.BeginInvoke(BeginShutdown);
        }
    }

    private void BeginShutdown()
    {
        _ = CompleteShutdownAsync();
    }

    private async Task CompleteShutdownAsync()
    {
        try
        {
            await disposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try
            {
                reportFailure?.Invoke(ex);
            }
            catch (Exception reportException) when (reportException is not OutOfMemoryException)
            {
                // Shutdown must continue when failure reporting also fails.
            }
        }
        finally
        {
            try
            {
                shutdownApplication();
                _completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
        }
    }
}
