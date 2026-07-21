using System.Windows.Threading;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed class ApplicationLogRefreshSession<TRequest, TResult> : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<TRequest> _captureRequest;
    private readonly Func<TRequest, TResult> _read;
    private readonly Action<TRequest, TResult> _render;
    private readonly Action<Exception> _renderFailure;
    private readonly OwnedDispatcherAction _automaticRefresh;
    private long _version;
    private int _automaticRefreshRequested;
    private bool _refreshRequested;
    private bool _refreshRunning;
    private bool _disposed;

    public ApplicationLogRefreshSession(
        Dispatcher dispatcher,
        Func<TRequest> captureRequest,
        Func<TRequest, TResult> read,
        Action<TRequest, TResult> render,
        Action<Exception> renderFailure)
    {
        _dispatcher = dispatcher;
        _captureRequest = captureRequest;
        _read = read;
        _render = render;
        _renderFailure = renderFailure;
        _automaticRefresh = new OwnedDispatcherAction(dispatcher, ConsumeAutomaticRefresh);
    }

    public void RequestRefresh()
    {
        _dispatcher.VerifyAccess();
        RequestRefreshCore();
    }

    public void QueueAutomaticRefresh()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _automaticRefreshRequested, 1);
        _automaticRefresh.Queue();
    }

    public void ClearAutomaticRefreshRequest()
    {
        Interlocked.Exchange(ref _automaticRefreshRequested, 0);
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        _disposed = true;
        Interlocked.Exchange(ref _automaticRefreshRequested, 0);
        _automaticRefresh.Dispose();
    }

    private void RequestRefreshCore()
    {
        if (_disposed || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _version += 1;
        _refreshRequested = true;
        if (!_refreshRunning)
        {
            _ = RunRefreshLoopAsync();
        }
    }

    private void ConsumeAutomaticRefresh()
    {
        if (Interlocked.Exchange(ref _automaticRefreshRequested, 0) != 0)
        {
            RequestRefreshCore();
        }
    }

    private async Task RunRefreshLoopAsync()
    {
        _refreshRunning = true;
        try
        {
            while (_refreshRequested && !_disposed && !_dispatcher.HasShutdownStarted)
            {
                _refreshRequested = false;
                var version = _version;
                var request = _captureRequest();
                TResult result;
                try
                {
                    result = await Task.Run(() => _read(request));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    ConsumeAutomaticRefresh();
                    if (!_disposed && !_dispatcher.HasShutdownStarted)
                    {
                        TryRenderFailure(exception);
                    }

                    continue;
                }

                ConsumeAutomaticRefresh();
                if (!_disposed && !_dispatcher.HasShutdownStarted && version == _version)
                {
                    _render(request, result);
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_disposed && !_dispatcher.HasShutdownStarted)
            {
                TryRenderFailure(exception);
            }
        }
        finally
        {
            _refreshRunning = false;
            if (_refreshRequested && !_disposed && !_dispatcher.HasShutdownStarted)
            {
                _ = RunRefreshLoopAsync();
            }
        }
    }

    private void TryRenderFailure(Exception exception)
    {
        try
        {
            _renderFailure(exception);
        }
        catch (Exception renderException) when (renderException is not OutOfMemoryException)
        {
        }
    }
}
