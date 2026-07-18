using System.Windows.Threading;

namespace VolturaAir.Host;

internal sealed class OwnedDispatcherTimer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Action? _elapsed;
    private bool _disposed;

    public OwnedDispatcherTimer(Dispatcher dispatcher, TimeSpan interval, Action elapsed)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(elapsed);
        _elapsed = elapsed;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = interval
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _elapsed = null;
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        var elapsed = _elapsed;
        Dispose();
        elapsed?.Invoke();
    }
}
