using System.Net.WebSockets;

namespace VolturaAir.Host.Features.Apps;

internal sealed class AppsCommandHandler : IAsyncDisposable
{
    private readonly HostStatusPayloadFactory _status;
    private readonly PairingManager _pairingManager;
    private readonly WebSocketTransport _transport;
    private readonly IAppsWindowAdapter _windows;
    private readonly AppsPreviewSessionCoordinator _previews;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string ClientId, WebSocket Socket), AppsWindowMap> _windowMaps = [];
    private readonly HashSet<AppsListSendLease> _listSends = [];
    private int _disposed;

    internal AppsCommandHandler(
        HostStatusPayloadFactory status,
        PairingManager pairingManager,
        WebSocketTransport transport,
        IAppsWindowAdapter windows,
        bool relayMode,
        Func<CancellationToken, Task<RelayTurnConfiguration?>> getRelayTurnConfiguration,
        IFileTransferWebRtcPeerFactory peerFactory)
    {
        _status = status;
        _pairingManager = pairingManager;
        _transport = transport;
        _windows = windows;
        _previews = new AppsPreviewSessionCoordinator(
            status,
            pairingManager,
            transport,
            windows,
            relayMode,
            getRelayTurnConfiguration,
            peerFactory,
            GetWindowMap);
        _pairingManager.PermissionsChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
        AppClientControlSettings.Changed += OnHostControlChanged;
    }

    internal async Task ListAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (!_status.CanControlOpenApps(clientId))
        {
            RemoveMap(clientId, socket);
            _previews.Cancel(clientId, socket);
            await SendListResultAsync(
                socket,
                operationId,
                false,
                "permission-denied",
                "Control open applications is disabled for this device.",
                null,
                [],
                cancellationToken);
            return;
        }

        bool includeVolturaAir = _status.CanControlHostApplication(clientId);
        AppsWindowDiscoveryResult discovery = await Task.Run(
            () => _windows.Discover(includeVolturaAir),
            cancellationToken).ConfigureAwait(false);
        if (!discovery.Succeeded)
        {
            RemoveMap(clientId, socket);
            await SendListResultAsync(
                socket,
                operationId,
                false,
                discovery.Code,
                discovery.Message,
                null,
                [],
                cancellationToken);
            return;
        }

        if (!_status.CanControlOpenApps(clientId))
        {
            RemoveMap(clientId, socket);
            await SendListResultAsync(
                socket,
                operationId,
                false,
                "permission-denied",
                "Control open applications is disabled for this device.",
                null,
                [],
                cancellationToken);
            return;
        }

        string revision = NewId();
        List<object> entries;
        Dictionary<string, AppsWindowSnapshot> handles;
        AppsListSendLease? listSend = null;
        bool authorized;
        lock (_gate)
        {
            authorized = _status.CanControlOpenApps(clientId);
            if (!authorized)
            {
                _windowMaps.Remove((clientId, socket));
                entries = [];
                handles = new(StringComparer.Ordinal);
            }
            else
            {
                bool canIncludeVolturaAir = _status.CanControlHostApplication(clientId);
                IReadOnlyList<AppsWindowSnapshot> discoveredWindows = canIncludeVolturaAir
                    ? discovery.Windows
                    : [.. discovery.Windows.Where(window => !window.IsVolturaAir)];
                entries = new List<object>(Math.Min(discoveredWindows.Count, AppsProtocol.MaximumWindows));
                handles = new Dictionary<string, AppsWindowSnapshot>(StringComparer.Ordinal);
                var previousWindows = new Dictionary<nint, (string Id, AppsWindowSnapshot Window)>();
                if (_windowMaps.TryGetValue((clientId, socket), out var previousMap))
                {
                    foreach (var previous in previousMap.Windows)
                    {
                        previousWindows[previous.Value.Handle] = (previous.Key, previous.Value);
                    }
                }

                foreach (AppsWindowSnapshot window in discoveredWindows.Take(AppsProtocol.MaximumWindows))
                {
                    string windowId = previousWindows.TryGetValue(window.Handle, out var previous) &&
                        WindowsAppsWindowAdapter.HasSameIdentity(previous.Window, window)
                            ? previous.Id
                            : NewId();
                    handles.Add(windowId, window);
                    entries.Add(new
                    {
                        windowId,
                        title = ProtocolStringLimits.Limit(window.Title, AppsProtocol.MaximumWindowTitleLength),
                        applicationName = ProtocolStringLimits.Limit(
                            window.ApplicationName,
                            AppsProtocol.MaximumApplicationNameLength),
                        active = window.Active,
                        minimized = window.Minimized,
                        maximizeSupported = window.MaximizeSupported,
                        previewSupported = window.PreviewSupported
                    });
                }

                _windowMaps[(clientId, socket)] = new AppsWindowMap(revision, handles);
#pragma warning disable CA2000 // AppsListSendLease owns the linked cancellation source until the send completes.
                listSend = new AppsListSendLease(
                    clientId,
                    handles.Values.Any(window => window.IsVolturaAir),
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
#pragma warning restore CA2000
                _listSends.Add(listSend);
            }
        }

        if (!authorized)
        {
            _previews.Cancel(clientId, socket);
            await SendListResultAsync(
                socket,
                operationId,
                false,
                "permission-denied",
                "Control open applications is disabled for this device.",
                null,
                [],
                cancellationToken);
            return;
        }

        try
        {
            await SendListResultAsync(
                socket,
                operationId,
                true,
                "accepted",
                discovery.Message,
                revision,
                entries,
                listSend!.Token);
        }
        catch (OperationCanceledException) when (
            listSend!.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            CompleteListSend(listSend!);
        }

        if (_status.CanPreviewOpenApps(clientId))
        {
            _previews.Ensure(socket, clientId, operationId, listSend!.IncludesVolturaAir);
        }
        else
        {
            _previews.Cancel(clientId, socket);
        }
    }

    internal Task ActivateAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string revision,
        string windowId,
        CancellationToken cancellationToken) =>
        HandleWindowActionAsync(
            socket,
            clientId,
            "apps.activate.result",
            operationId,
            revision,
            windowId,
            static (adapter, window, includeVolturaAir) => adapter.Activate(window, includeVolturaAir),
            cancellationToken);

    internal Task CloseAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string revision,
        string windowId,
        CancellationToken cancellationToken) =>
        HandleWindowActionAsync(
            socket,
            clientId,
            "apps.close.result",
            operationId,
            revision,
            windowId,
            static (adapter, window, includeVolturaAir) => adapter.Close(window, includeVolturaAir),
            cancellationToken);

    internal Task AnswerPreviewAsync(
        WebSocket socket,
        string clientId,
        string operationId,
        string offerOperationId,
        string previewId,
        string answerSdp,
        string clientSignature,
        CancellationToken cancellationToken) =>
        _previews.AnswerAsync(
            socket,
            clientId,
            operationId,
            offerOperationId,
            previewId,
            answerSdp,
            clientSignature,
            cancellationToken);

    internal void StopPreview(string clientId, WebSocket socket, string previewId)
    {
        _previews.Stop(clientId, socket, previewId);
    }

    internal void ClientDisconnected(string clientId, WebSocket socket)
    {
        RemoveMap(clientId, socket);
        _previews.Cancel(clientId, socket);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        AppClientControlSettings.Changed -= OnHostControlChanged;
        lock (_gate)
        {
            foreach (AppsListSendLease listSend in _listSends)
            {
                listSend.Cancel();
            }
            _windowMaps.Clear();
        }
        await _previews.DisposeAsync().ConfigureAwait(false);
        _windows.Dispose();
    }

    private async Task HandleWindowActionAsync(
        WebSocket socket,
        string clientId,
        string resultType,
        string operationId,
        string revision,
        string windowId,
        Func<IAppsWindowAdapter, AppsWindowSnapshot, bool, AppsWindowActionResult> action,
        CancellationToken cancellationToken)
    {
        AppsWindowActionResult result;
        try
        {
            result = await Task.Run(
                () =>
                {
                    lock (_gate)
                    {
                        if (!_status.CanControlOpenApps(clientId))
                        {
                            return new AppsWindowActionResult(
                                false,
                                "permission-denied",
                                "Control open applications is disabled for this device.");
                        }

                        if (!_windowMaps.TryGetValue((clientId, socket), out var map) ||
                            map.Revision != revision ||
                            !map.Windows.TryGetValue(windowId, out var window))
                        {
                            return new AppsWindowActionResult(
                                false,
                                "stale-window",
                                "Refresh Apps and try again.");
                        }

                        bool includeVolturaAir = _status.CanControlHostApplication(clientId);
                        if (window.IsVolturaAir && !includeVolturaAir)
                        {
                            return new AppsWindowActionResult(
                                false,
                                "permission-denied",
                                "Control Voltura Air is disabled for this device.");
                        }

                        return action(_windows, window, includeVolturaAir);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = new(false, "unavailable", "Windows could not complete the application action.");
        }
        await SendActionResultAsync(
            socket,
            resultType,
            operationId,
            result.Succeeded,
            result.Code,
            result.Message,
            cancellationToken,
            windowId);
    }

    private void OnPermissionsChanged(object? sender, EventArgs eventArgs)
    {
        lock (_gate)
        {
            foreach (var entry in _windowMaps.ToArray())
            {
                if (!_status.CanControlOpenApps(entry.Key.ClientId) ||
                    !_status.CanControlHostApplication(entry.Key.ClientId) &&
                    entry.Value.Windows.Values.Any(window => window.IsVolturaAir))
                {
                    _windowMaps.Remove(entry.Key);
                }
            }
            CancelUnauthorizedListSendsLocked();
        }
        _previews.PermissionsChanged();
    }

    private void OnHostControlChanged(object? sender, EventArgs eventArgs)
    {
        lock (_gate)
        {
            _windowMaps.Clear();
            foreach (AppsListSendLease listSend in _listSends.Where(send => send.IncludesVolturaAir))
            {
                listSend.Cancel();
            }
        }
        _previews.PermissionsChanged();
    }

    private void RemoveMap(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            _windowMaps.Remove((clientId, socket));
        }
    }

    private void CancelUnauthorizedListSendsLocked()
    {
        foreach (AppsListSendLease listSend in _listSends)
        {
            if (!_status.CanControlOpenApps(listSend.ClientId) ||
                listSend.IncludesVolturaAir && !_status.CanControlHostApplication(listSend.ClientId))
            {
                listSend.Cancel();
            }
        }
    }

    private void CompleteListSend(AppsListSendLease listSend)
    {
        lock (_gate)
        {
            _listSends.Remove(listSend);
        }
        listSend.Dispose();
    }

    private IReadOnlyDictionary<string, AppsWindowSnapshot>? GetWindowMap(
        string clientId,
        WebSocket socket,
        string revision)
    {
        lock (_gate)
        {
            return _windowMaps.TryGetValue((clientId, socket), out var map) && map.Revision == revision
                ? map.Windows
                : null;
        }
    }

    private Task SendListResultAsync(
        WebSocket socket,
        string operationId,
        bool succeeded,
        string code,
        string message,
        string? revision,
        IReadOnlyList<object> windows,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(socket, new
        {
            type = "apps.list.result",
            operationId,
            succeeded,
            code,
            message,
            revision,
            windows
        }, cancellationToken);

    private Task SendActionResultAsync(
        WebSocket socket,
        string type,
        string operationId,
        bool succeeded,
        string code,
        string message,
        CancellationToken cancellationToken,
        string? windowId = null) =>
        _transport.SendAsync(socket, new
        {
            type,
            operationId,
            windowId,
            succeeded,
            code,
            message
        }, cancellationToken);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed record AppsWindowMap(
        string Revision,
        IReadOnlyDictionary<string, AppsWindowSnapshot> Windows);

    private sealed class AppsListSendLease(
        string clientId,
        bool includesVolturaAir,
        CancellationTokenSource cancellation) : IDisposable
    {
        internal string ClientId { get; } = clientId;
        internal bool IncludesVolturaAir { get; } = includesVolturaAir;
        internal CancellationToken Token => cancellation.Token;
        internal bool IsCancellationRequested => cancellation.IsCancellationRequested;
        internal void Cancel() => cancellation.Cancel();
        public void Dispose() => cancellation.Dispose();
    }

}
