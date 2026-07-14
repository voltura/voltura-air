namespace VolturaAir.Host;

internal sealed class LazyPointerHighlightService : IPointerHighlightService, IDisposable
{
    private readonly object _gate = new();
    private readonly IAppLog? _appLog;
    private readonly Func<IPointerHighlightService> _factory;
    private IPointerHighlightService? _inner;
    private bool _disabledForSession;
    private bool _overlaySuppressed;
    private bool _disposed;

    public LazyPointerHighlightService(IAppLog appLog, bool cursorWatchdogStarted)
    {
        _appLog = appLog;
        _factory = () => new PointerHighlightService(appLog, cursorWatchdogStarted);
        PointerHighlightService.RecoverSystemCursors(appLog);
    }

    internal LazyPointerHighlightService(Func<IPointerHighlightService> factory)
    {
        _factory = factory;
    }

    internal bool IsValueCreated
    {
        get
        {
            lock (_gate)
            {
                return _inner is not null;
            }
        }
    }

    public void NotifyPointerActivity()
    {
        IPointerHighlightService service;
        lock (_gate)
        {
            if (_disposed || _disabledForSession || _overlaySuppressed)
            {
                return;
            }

            service = _inner ??= _factory();
        }

        service.NotifyPointerActivity();
    }

    public void SetOverlaySuppressed(bool suppressed)
    {
        IPointerHighlightService? service;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _overlaySuppressed = suppressed;
            service = _inner;
        }

        if (suppressed && _appLog is not null)
        {
            PointerHighlightService.RecoverSystemCursors(_appLog);
        }

        service?.SetOverlaySuppressed(suppressed);
    }

    internal void DisableForSession()
    {
        PointerHighlightService? service;
        lock (_gate)
        {
            if (_disposed || _disabledForSession)
            {
                return;
            }

            _disabledForSession = true;
            service = _inner as PointerHighlightService;
        }

        if (_appLog is not null)
        {
            PointerHighlightService.RecoverSystemCursors(_appLog);
        }

        service?.DisableForSession();
    }

    public void Dispose()
    {
        IDisposable? disposable;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            disposable = _inner as IDisposable;
            _inner = null;
        }

        disposable?.Dispose();
    }
}
