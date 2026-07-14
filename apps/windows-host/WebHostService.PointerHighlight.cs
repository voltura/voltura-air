using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private readonly ConcurrentDictionary<WebSocket, PointerHighlightConnectionState> _pointerHighlightStates = new();

    private void HandlePointerHighlightSet(string clientId, JsonElement root, PointerHighlightConnectionState state)
    {
        var enabled = root.GetProperty("enabled").GetBoolean();
        state.Enabled = enabled;
        _pairingManager.SetDeviceHighlightPointerOverride(clientId, enabled);
        LogCommandOutcome(clientId, "pointer.highlight.set", enabled ? "enable" : "disable", "saved");
    }

    private PointerHighlightConnectionState RegisterSocket(string clientId, WebSocket socket)
    {
        _connections.Register(clientId, socket);
        var state = new PointerHighlightConnectionState(clientId, _pairingManager.GetDeviceHighlightPointer(clientId));
        _pointerHighlightStates[socket] = state;
        return state;
    }

    private void UnregisterSocket(string clientId, WebSocket socket)
    {
        _connections.Unregister(clientId, socket);
        _pointerHighlightStates.TryRemove(socket, out _);
    }

    private void RefreshPointerHighlightStates()
    {
        foreach (var state in _pointerHighlightStates.Values)
        {
            state.Enabled = _pairingManager.GetDeviceHighlightPointer(state.ClientId);
        }
    }
}

internal sealed class PointerHighlightConnectionState
{
    private int _enabled;

    public PointerHighlightConnectionState(string clientId, bool enabled)
    {
        ClientId = clientId;
        Enabled = enabled;
    }

    public string ClientId { get; }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }
}
