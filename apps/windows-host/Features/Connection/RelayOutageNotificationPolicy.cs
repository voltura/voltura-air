namespace VolturaAir.Host.Features.Connection;

internal enum RelayOutageNotification
{
    Failure,
    Restored
}

internal sealed class RelayOutageNotificationPolicy
{
    private bool _outageActive;

    public RelayOutageNotification? Observe(RelayConnectionState state, string? failureCode = null)
    {
        var unavailableAfterFailure = state == RelayConnectionState.Failed ||
            state == RelayConnectionState.Retrying && failureCode is not null;
        if (unavailableAfterFailure)
        {
            if (_outageActive)
            {
                return null;
            }

            _outageActive = true;
            return RelayOutageNotification.Failure;
        }

        if (state == RelayConnectionState.Connected && _outageActive)
        {
            _outageActive = false;
            return RelayOutageNotification.Restored;
        }

        return null;
    }
}
