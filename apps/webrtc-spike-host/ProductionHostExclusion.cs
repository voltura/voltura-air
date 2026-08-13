using System.Security.Principal;

namespace WebRtcSpike.Host;

internal sealed class ProductionHostExclusion : IDisposable
{
    private const string NamePrefix = @"Local\VolturaAir.Host.Instance.";
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private readonly Thread _ownerThread;
    private readonly string _mutexName;
    private Exception? _failure;
    private bool _acquired;
    private bool _disposed;

    private ProductionHostExclusion(string mutexName)
    {
        _mutexName = mutexName;
        _ownerThread = new Thread(OwnMutex)
        {
            IsBackground = true,
            Name = "Voltura Air webcam spike host exclusion"
        };
    }

    internal static ProductionHostExclusion? TryAcquireCurrentUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string userId = identity.User?.Value ?? Environment.UserName;
        return TryAcquire($"{NamePrefix}{userId}");
    }

    internal static ProductionHostExclusion? TryAcquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        var exclusion = new ProductionHostExclusion(mutexName);
        exclusion._ownerThread.Start();
        exclusion._started.Wait();
        if (exclusion._failure is not null)
        {
            exclusion.Dispose();
            throw new InvalidOperationException("Could not acquire the Voltura Air one-host guard.", exclusion._failure);
        }
        if (!exclusion._acquired)
        {
            exclusion.Dispose();
            return null;
        }
        return exclusion;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _release.Set();
        _ownerThread.Join();
        _started.Dispose();
        _release.Dispose();
    }

    private void OwnMutex()
    {
        try
        {
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            try
            {
                _acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                _acquired = true;
            }
            finally
            {
                _started.Set();
            }

            if (!_acquired) return;
            _release.Wait();
            mutex.ReleaseMutex();
        }
        catch (Exception exception)
        {
            _failure = exception;
            _started.Set();
        }
    }
}
