using VolturaAir.Host.Features.Connection;

namespace VolturaAir.Host.Tests;

public sealed class RelayOutageNotificationPolicyTests
{
    [Fact]
    public void ReportsOneFailureAndOneRestorationPerOutage()
    {
        var policy = new RelayOutageNotificationPolicy();

        Assert.Null(policy.Observe(RelayConnectionState.Connecting));
        Assert.Null(policy.Observe(RelayConnectionState.Connected));
        Assert.Equal(RelayOutageNotification.Failure, policy.Observe(RelayConnectionState.Failed));
        Assert.Null(policy.Observe(RelayConnectionState.Retrying));
        Assert.Null(policy.Observe(RelayConnectionState.Failed));
        Assert.Equal(RelayOutageNotification.Restored, policy.Observe(RelayConnectionState.Connected));
        Assert.Null(policy.Observe(RelayConnectionState.Connected));
        Assert.Equal(RelayOutageNotification.Failure, policy.Observe(RelayConnectionState.Failed));
    }

    [Fact]
    public void ReportsAnOutageWhenObservationStartsDuringRetry()
    {
        var policy = new RelayOutageNotificationPolicy();

        Assert.Equal(
            RelayOutageNotification.Failure,
            policy.Observe(RelayConnectionState.Retrying, "websocket"));
        Assert.Null(policy.Observe(RelayConnectionState.Failed, "websocket"));
        Assert.Equal(RelayOutageNotification.Restored, policy.Observe(RelayConnectionState.Connected));
    }
}
