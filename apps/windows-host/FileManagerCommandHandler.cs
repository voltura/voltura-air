using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class FileManagerCommandHandler : IAsyncDisposable
{
    private readonly FileManagerService _service;
    private readonly HostStatusPayloadFactory _status;
    private readonly WebSocketTransport _transport;
    private readonly PairingManager _pairingManager;
    private readonly Channel<string> _updates = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;

    public FileManagerCommandHandler(FileManagerService service, HostStatusPayloadFactory status, WebSocketTransport transport, PairingManager pairingManager)
    {
        _service = service;
        _status = status;
        _transport = transport;
        _pairingManager = pairingManager;
        _service.JobChanged += OnJobChanged;
        _pairingManager.PermissionsChanged += OnPermissionsChanged;
        AppPermissionSettings.Changed += OnPermissionsChanged;
        _worker = Task.Run(ProcessUpdatesAsync);
    }

    public async Task HandleAsync(WebSocket socket, string clientId, string type, JsonElement root, CancellationToken cancellationToken)
    {
        var operationId = ProtocolMessageFields.GetString(root, "operationId");
        if (!_status.CanBrowseFiles(clientId))
        {
            _service.RevokeClient(clientId, closeSession: true);
            await SendResultAsync(socket, type, operationId, false, "permission-denied", "Browse and open files is disabled for this device on the PC.", cancellationToken);
            return;
        }

        switch (type)
        {
            case "file.session.open":
                try
                {
                    var snapshot = _service.OpenSession(clientId);
                    await _transport.SendAsync(socket, new { type = "file.session.open.result", operationId, succeeded = true, message = "Files opened.", session = snapshot }, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    await SendResultAsync(socket, type, operationId, false, "directory-unavailable", "The initial folders are unavailable.", cancellationToken);
                }
                return;
            case "file.page.get":
                await SendPanelResultAsync(socket, type, operationId, _service.TryGetPage(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    String(root, "revision"),
                    String(root, "continuation"),
                    out var nextPage,
                    out var nextCode), nextPage, nextCode, cancellationToken);
                return;
            case "file.navigate":
                await SendPanelResultAsync(socket, type, operationId, _service.TryNavigate(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    String(root, "revision"),
                    String(root, "targetId"),
                    out var navigationPage,
                    out var navigationCode), navigationPage, navigationCode, cancellationToken);
                return;
            case "file.refresh":
                await SendPanelResultAsync(socket, type, operationId, _service.TryRefresh(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    out var refreshPage,
                    out var refreshCode), refreshPage, refreshCode, cancellationToken);
                return;
            case "file.sort":
                await SendPanelResultAsync(socket, type, operationId, _service.TrySort(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    String(root, "sortBy"),
                    root.GetProperty("descending").GetBoolean(),
                    out var sortPage,
                    out var sortCode), sortPage, sortCode, cancellationToken);
                return;
            case "file.properties.get":
                var propertiesSucceeded = _service.TryGetProperties(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    String(root, "revision"),
                    String(root, "entryId"),
                    out var properties,
                    out var propertiesCode);
                await _transport.SendAsync(socket, new
                {
                    type = "file.properties.get.result",
                    operationId,
                    succeeded = propertiesSucceeded,
                    code = propertiesSucceeded ? null : propertiesCode,
                    message = propertiesSucceeded ? "Properties loaded." : "The selected item is unavailable.",
                    properties
                }, cancellationToken);
                return;
            case "file.clipboard.set":
                if (String(root, "effect") == "move" && !_status.CanChangeFiles(clientId))
                {
                    _service.RevokeClient(clientId, closeSession: false);
                    await SendResultAsync(socket, type, operationId, false, "permission-denied", "Change files is disabled for this device on the PC.", cancellationToken);
                    return;
                }
                var clipboard = _service.SetClipboard(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    String(root, "revision"),
                    ReadSelection(root),
                    String(root, "effect") == "move");
                await SendResultAsync(socket, type, operationId, clipboard.Succeeded, clipboard.Code, clipboard.Message, cancellationToken);
                return;
            case "file.open":
                var opened = _service.Open(clientId, String(root, "sessionId"), String(root, "panel"), String(root, "revision"), String(root, "entryId"));
                await SendResultAsync(socket, type, operationId, opened.Succeeded, opened.Code, opened.Message, cancellationToken);
                return;
            case "file.jobs.get":
                await _transport.SendAsync(socket, new { type = "file.jobs.status", operationId, jobs = _service.GetJobs(clientId) }, cancellationToken);
                return;
            case "file.job.control":
                if (!_status.CanChangeFiles(clientId))
                {
                    _service.RevokeClient(clientId, closeSession: false);
                    await SendResultAsync(socket, type, operationId, false, "permission-denied", "Change files is disabled for this device on the PC.", cancellationToken);
                    return;
                }
                var controlled = _service.ControlJob(clientId, String(root, "jobId"), String(root, "action"));
                await SendResultAsync(socket, type, operationId, controlled, controlled ? null : "job-unavailable", controlled ? "File job updated." : "The file job is unavailable.", cancellationToken);
                return;
            case "file.job.reorder":
                if (!_status.CanChangeFiles(clientId))
                {
                    _service.RevokeClient(clientId, closeSession: false);
                    await SendResultAsync(socket, type, operationId, false, "permission-denied", "Change files is disabled for this device on the PC.", cancellationToken);
                    return;
                }
                var reordered = _service.ReorderJob(clientId, String(root, "jobId"), String(root, "direction"));
                await SendResultAsync(socket, type, operationId, reordered, reordered ? null : "job-unavailable", reordered ? "Queue order updated." : "The queued file job cannot move there.", cancellationToken);
                return;
            case "file.job.conflict.resolve":
                if (!_status.CanChangeFiles(clientId))
                {
                    _service.RevokeClient(clientId, closeSession: false);
                    await SendResultAsync(socket, type, operationId, false, "permission-denied", "Change files is disabled for this device on the PC.", cancellationToken);
                    return;
                }
                var resolved = _service.ResolveConflict(clientId, String(root, "jobId"), String(root, "resolution"), root.GetProperty("applyToAll").GetBoolean());
                await SendResultAsync(socket, type, operationId, resolved, resolved ? null : "job-unavailable", resolved ? "Conflict choice applied." : "The conflict is no longer available.", cancellationToken);
                return;
            case "file.job.create":
                if (!_status.CanChangeFiles(clientId))
                {
                    _service.RevokeClient(clientId, closeSession: false);
                    await SendResultAsync(socket, type, operationId, false, "permission-denied", "Change files is disabled for this device on the PC.", cancellationToken);
                    return;
                }
                var created = _service.CreateJob(
                    clientId,
                    String(root, "sessionId"),
                    String(root, "panel"),
                    String(root, "revision"),
                    ReadSelection(root),
                    String(root, "operation"),
                    OptionalString(root, "destinationPanel"),
                    OptionalString(root, "newName"));
                await _transport.SendAsync(socket, new
                {
                    type = "file.job.create.result",
                    operationId,
                    succeeded = created.Succeeded,
                    code = created.Code,
                    message = created.Message,
                    job = created.Job
                }, cancellationToken);
                return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _service.JobChanged -= OnJobChanged;
        _pairingManager.PermissionsChanged -= OnPermissionsChanged;
        AppPermissionSettings.Changed -= OnPermissionsChanged;
        _updates.Writer.TryComplete();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }

    private void OnJobChanged(object? sender, string clientId) => _updates.Writer.TryWrite(clientId);

    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        foreach (var clientId in _service.SessionClientIds)
        {
            if (!_status.CanBrowseFiles(clientId)) _service.RevokeClient(clientId, closeSession: true);
            else if (!_status.CanChangeFiles(clientId)) _service.RevokeClient(clientId, closeSession: false);
        }
    }

    private async Task ProcessUpdatesAsync()
    {
        await foreach (var clientId in _updates.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
        {
            var clients = new HashSet<string>(StringComparer.Ordinal) { clientId };
            while (_updates.Reader.TryRead(out var next)) clients.Add(next);
            foreach (var updatedClientId in clients)
            {
                var payload = new { type = "file.jobs.status", jobs = _service.GetJobs(updatedClientId) };
                foreach (var (ownerClientId, socket) in _transport.Snapshot())
                {
                    if (!string.Equals(ownerClientId, updatedClientId, StringComparison.Ordinal)) continue;
                    try { await _transport.SendAsync(socket, payload, _lifetime.Token).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
            await Task.Delay(200, _lifetime.Token).ConfigureAwait(false);
        }
    }

    private async Task SendPanelResultAsync(WebSocket socket, string requestType, string operationId, bool succeeded, FileManagerPanelPage? page, string code, CancellationToken cancellationToken) =>
        await _transport.SendAsync(socket, new
        {
            type = $"{requestType}.result",
            operationId,
            succeeded,
            code = succeeded ? null : code,
            message = succeeded ? "Folder loaded." : code == "stale-panel" ? "The folder changed. Refresh it and try again." : "The folder is unavailable.",
            page
        }, cancellationToken);

    private async Task SendResultAsync(WebSocket socket, string requestType, string operationId, bool succeeded, string? code, string message, CancellationToken cancellationToken) =>
        await _transport.SendAsync(socket, new { type = $"{requestType}.result", operationId, succeeded, code, message }, cancellationToken);

    private static FileManagerSelection ReadSelection(JsonElement root) => new(
        root.GetProperty("selectionAll").GetBoolean(),
        [.. root.GetProperty("entryIds").EnumerateArray().Select(item => item.GetString()!)],
        [.. root.GetProperty("excludedEntryIds").EnumerateArray().Select(item => item.GetString()!)]);

    private static string String(JsonElement root, string name) => root.GetProperty(name).GetString()!;
    private static string? OptionalString(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;
}
