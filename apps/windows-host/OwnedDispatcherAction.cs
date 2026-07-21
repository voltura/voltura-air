using System.Windows.Threading;

namespace VolturaAir.Host;

internal sealed class OwnedDispatcherAction : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dispatcher _dispatcher;
    private Action? _action;
    private DispatcherOperation? _pendingOperation;
    private bool _disposed;

    public OwnedDispatcherAction(Dispatcher dispatcher, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher = dispatcher;
        _action = action;
    }

    internal bool IsPending
    {
        get
        {
            lock (_gate)
            {
                return _pendingOperation is not null;
            }
        }
    }

    public void Queue(DispatcherPriority priority = DispatcherPriority.Normal)
    {
        lock (_gate)
        {
            if (_disposed || _pendingOperation is not null || _dispatcher.HasShutdownStarted)
            {
                return;
            }

            _pendingOperation = _dispatcher.BeginInvoke(priority, Invoke);
        }
    }

    public void Dispose()
    {
        DispatcherOperation? pendingOperation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _action = null;
            pendingOperation = _pendingOperation;
            _pendingOperation = null;
        }

        _ = pendingOperation?.Abort();
    }

    private void Invoke()
    {
        Action? action;
        lock (_gate)
        {
            _pendingOperation = null;
            action = _disposed ? null : _action;
        }

        action?.Invoke();
    }
}
