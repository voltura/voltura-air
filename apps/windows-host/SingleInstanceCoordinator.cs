using System.Security.Principal;

namespace VolturaAir.Host;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string NamePrefix = @"Local\VolturaAir.Host";
    private readonly EventWaitHandle _activationEvent;
    private readonly Mutex _instanceMutex;
    private readonly RegisteredWaitHandle _activationRegistration;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex instanceMutex, EventWaitHandle activationEvent, Action activationRequested)
    {
        _instanceMutex = instanceMutex;
        _activationEvent = activationEvent;
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    activationRequested();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static SingleInstanceCoordinator? TryAcquire(Action activationRequested, string? instanceScope = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userId = identity.User?.Value ?? Environment.UserName;
        var scopeSuffix = string.IsNullOrWhiteSpace(instanceScope) ? string.Empty : $".{instanceScope}";
        return TryAcquire(
            $@"{NamePrefix}.Instance{scopeSuffix}.{userId}",
            $@"{NamePrefix}.Activate{scopeSuffix}.{userId}",
            activationRequested);
    }

    internal static SingleInstanceCoordinator? TryAcquire(string mutexName, string activationEventName, Action activationRequested)
    {
        var instanceMutex = new Mutex(initiallyOwned: false, mutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                SignalExistingInstance(activationEventName);
                return null;
            }

            var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
            return new SingleInstanceCoordinator(instanceMutex, activationEvent, activationRequested);
        }
        finally
        {
            if (!ownsMutex)
            {
                instanceMutex.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationRegistration.Unregister(null);
        _activationEvent.Dispose();
        _instanceMutex.ReleaseMutex();
        _instanceMutex.Dispose();
    }

    private static void SignalExistingInstance(string activationEventName)
    {
        for (var attempt = 0; attempt < 20; attempt += 1)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(activationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                if (attempt == 19)
                {
                    return;
                }

                Thread.Sleep(50);
            }
        }
    }
}
