using System.Net.WebSockets;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class HostStatusBroadcaster : IAsyncDisposable
{
    private readonly PairingManager _pairingManager;
    private readonly IAwakeService _awakeService;
    private readonly IWorkstationLockPolicy _workstationLockPolicy;
    private readonly WebSocketTransport _transport;
    private readonly HostStatusPayloadFactory _statusFactory;
    private readonly IAppLogWriter _appLog;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Lock _closeTasksGate = new();
    private readonly HashSet<Task> _closeTasks = [];
    private readonly Task _worker;
    private int _disposeState;

    public HostStatusBroadcaster(
        PairingManager pairingManager,
        IAwakeService awakeService,
        IWorkstationLockPolicy workstationLockPolicy,
        WebSocketTransport transport,
        HostStatusPayloadFactory statusFactory,
        IAppLogWriter appLog)
    {
        _pairingManager = pairingManager;
        _awakeService = awakeService;
        _workstationLockPolicy = workstationLockPolicy;
        _transport = transport;
        _statusFactory = statusFactory;
        _appLog = appLog;
        _lifetimeToken = _lifetimeCancellation.Token;

        _pairingManager.PairingRevoked += OnPairingRevoked;
        _pairingManager.PermissionsChanged += OnStatusChanged;
        _pairingManager.DeviceProfileChanged += OnStatusChanged;
        AppPermissionSettings.Changed += OnStatusChanged;
        AppDeveloperSettings.Changed += OnStatusChanged;
        AppRemoteSettings.Changed += OnStatusChanged;
        AppLaunchSettings.Changed += OnStatusChanged;
        AppTextDestinationSettings.Changed += OnStatusChanged;
        AppPointerSettings.Changed += OnStatusChanged;
        AppAppearanceSettings.Changed += OnStatusChanged;
        _workstationLockPolicy.Changed += OnStatusChanged;
        _awakeService.StateChanged += OnStatusChanged;
        _worker = Task.Run(ProcessAsync);
    }

    public void Queue()
    {
        if (Volatile.Read(ref _disposeState) == 0)
        {
            _requests.Writer.TryWrite(true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _pairingManager.PairingRevoked -= OnPairingRevoked;
        _pairingManager.PermissionsChanged -= OnStatusChanged;
        _pairingManager.DeviceProfileChanged -= OnStatusChanged;
        AppPermissionSettings.Changed -= OnStatusChanged;
        AppDeveloperSettings.Changed -= OnStatusChanged;
        AppRemoteSettings.Changed -= OnStatusChanged;
        AppLaunchSettings.Changed -= OnStatusChanged;
        AppTextDestinationSettings.Changed -= OnStatusChanged;
        AppPointerSettings.Changed -= OnStatusChanged;
        AppAppearanceSettings.Changed -= OnStatusChanged;
        _workstationLockPolicy.Changed -= OnStatusChanged;
        _awakeService.StateChanged -= OnStatusChanged;

        _requests.Writer.TryComplete();
        await _lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
            Task[] closeTasks;
            lock (_closeTasksGate)
            {
                closeTasks = [.. _closeTasks];
            }

            await Task.WhenAll(closeTasks).ConfigureAwait(false);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }

    private void OnPairingRevoked(object? sender, PairingRevokedEventArgs e)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var sockets = _transport.TakeRevoked(e.ClientId);
        if (sockets.Length == 0)
        {
            return;
        }

        lock (_closeTasksGate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                AbortSockets(sockets);
                return;
            }

            var closeTask = CloseSocketsAsync(sockets, _lifetimeToken);
            _closeTasks.Add(closeTask);
            _ = closeTask.ContinueWith(
                completed =>
                {
                    lock (_closeTasksGate)
                    {
                        _closeTasks.Remove(completed);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Queue();

    private async Task ProcessAsync()
    {
        try
        {
            while (await _requests.Reader.WaitToReadAsync(_lifetimeToken).ConfigureAwait(false))
            {
                while (_requests.Reader.TryRead(out _))
                {
                }

                try
                {
                    await BroadcastAsync(_lifetimeToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _appLog.Write(new AppLogEntry(
                        Event: "host_lifecycle",
                        Source: "websocket",
                        Action: "broadcast_status",
                        Outcome: "failed",
                        Detail: ex.Message));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        foreach (var (clientId, socket) in _transport.Snapshot())
        {
            try
            {
                await _transport.SendAsync(socket, _statusFactory.CreateConnectedStatus(clientId), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task CloseSocketsAsync(IEnumerable<WebSocket> sockets, CancellationToken cancellationToken)
    {
        foreach (var socket in sockets)
        {
            try
            {
                await WebSocketTransport.CloseAsync(socket, "Device disconnected", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }
    }

    private static void AbortSockets(IEnumerable<WebSocket> sockets)
    {
        foreach (var socket in sockets)
        {
            try
            {
                socket.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
