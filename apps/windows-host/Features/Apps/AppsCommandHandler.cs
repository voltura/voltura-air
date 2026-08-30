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
        var entries = new List<object>(Math.Min(discovery.Windows.Count, AppsProtocol.MaximumWindows));
        var handles = new Dictionary<string, AppsWindowSnapshot>(StringComparer.Ordinal);
        var previousWindows = new Dictionary<nint, (string Id, AppsWindowSnapshot Window)>();
        lock (_gate)
        {
            if (_windowMaps.TryGetValue((clientId, socket), out var previousMap))
            {
                foreach (var previous in previousMap.Windows)
                {
                    previousWindows[previous.Value.Handle] = (previous.Key, previous.Value);
                }
            }
        }
        foreach (AppsWindowSnapshot window in discovery.Windows.Take(AppsProtocol.MaximumWindows))
        {
            string windowId = previousWindows.TryGetValue(window.Handle, out var previous) &&
                string.Equals(previous.Window.Title, window.Title, StringComparison.Ordinal) &&
                string.Equals(previous.Window.ApplicationName, window.ApplicationName, StringComparison.Ordinal)
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

        lock (_gate)
        {
            _windowMaps[(clientId, socket)] = new AppsWindowMap(revision, handles);
        }

        await SendListResultAsync(
            socket,
            operationId,
            true,
            "accepted",
            discovery.Message,
            revision,
            entries,
            cancellationToken);

        if (_status.CanPreviewOpenApps(clientId))
        {
            _previews.Ensure(socket, clientId, operationId);
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
            static (adapter, handle, includeVolturaAir) => adapter.Activate(handle, includeVolturaAir),
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
            static (adapter, handle, includeVolturaAir) => adapter.Close(handle, includeVolturaAir),
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
        Func<IAppsWindowAdapter, nint, bool, AppsWindowActionResult> action,
        CancellationToken cancellationToken)
    {
        if (!_status.CanControlOpenApps(clientId))
        {
            await SendActionResultAsync(
                socket,
                resultType,
                operationId,
                false,
                "permission-denied",
                "Control open applications is disabled for this device.",
                cancellationToken,
                windowId);
            return;
        }

        AppsWindowSnapshot? window;
        lock (_gate)
        {
            window = _windowMaps.TryGetValue((clientId, socket), out var map) &&
                map.Revision == revision &&
                map.Windows.TryGetValue(windowId, out var current)
                    ? current
                    : null;
        }

        if (window is null)
        {
            await SendActionResultAsync(
                socket,
                resultType,
                operationId,
                false,
                "stale-window",
                "Refresh Apps and try again.",
                cancellationToken,
                windowId);
            return;
        }

        if (!_status.CanControlOpenApps(clientId))
        {
            await SendActionResultAsync(
                socket,
                resultType,
                operationId,
                false,
                "permission-denied",
                "Control open applications is disabled for this device.",
                cancellationToken,
                windowId);
            return;
        }

        bool includeVolturaAir = _status.CanControlHostApplication(clientId);
        AppsWindowActionResult result;
        try
        {
            result = await Task.Run(
                () => action(_windows, window.Handle, includeVolturaAir),
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
            foreach (var key in _windowMaps.Keys.Where(key => !_status.CanControlOpenApps(key.ClientId)).ToArray())
            {
                _windowMaps.Remove(key);
            }
        }
        _previews.PermissionsChanged();
    }

    private void OnHostControlChanged(object? sender, EventArgs eventArgs)
    {
        lock (_gate)
        {
            _windowMaps.Clear();
        }
    }

    private void RemoveMap(string clientId, WebSocket socket)
    {
        lock (_gate)
        {
            _windowMaps.Remove((clientId, socket));
        }
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

}
