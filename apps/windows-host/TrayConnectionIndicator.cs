namespace VolturaAir.Host;

internal enum TrayConnectionState
{
    Starting,
    NoDevicesRegistered,
    Disconnected,
    Connected
}

/// <summary>
/// Keeps the tray badge stable while an already-connected controller performs a short reconnect.
/// </summary>
internal sealed class TrayConnectionIndicator(bool isPaired, bool hasActiveController, bool holdInitialDisconnectedState = false)
{
    public TrayConnectionState DisplayedState { get; private set; } = GetInitialState(isPaired, hasActiveController, holdInitialDisconnectedState);

    public TrayConnectionState Update(
        bool isPaired,
        bool hasActiveController,
        bool holdConnectedDuringReconnect = false,
        bool holdInitialDisconnectedState = false)
    {
        var requestedState = GetRequestedState(isPaired, hasActiveController);
        if (holdInitialDisconnectedState &&
            DisplayedState == TrayConnectionState.Starting &&
            requestedState == TrayConnectionState.Disconnected)
        {
            return DisplayedState;
        }

        if (holdConnectedDuringReconnect &&
            DisplayedState == TrayConnectionState.Connected &&
            requestedState == TrayConnectionState.Disconnected)
        {
            return DisplayedState;
        }

        DisplayedState = requestedState;
        return DisplayedState;
    }

    private static TrayConnectionState GetInitialState(bool isPaired, bool hasActiveController, bool holdInitialDisconnectedState)
    {
        var requestedState = GetRequestedState(isPaired, hasActiveController);
        return holdInitialDisconnectedState && requestedState == TrayConnectionState.Disconnected
            ? TrayConnectionState.Starting
            : requestedState;
    }

    private static TrayConnectionState GetRequestedState(bool isPaired, bool hasActiveController)
    {
        if (hasActiveController)
        {
            return TrayConnectionState.Connected;
        }

        return isPaired
            ? TrayConnectionState.Disconnected
            : TrayConnectionState.NoDevicesRegistered;
    }
}
