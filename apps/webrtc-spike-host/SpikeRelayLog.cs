using VolturaAir.Host;

namespace VolturaAir.Host;

// AppLog.Models is linked for the production Relay connection contract. The spike deliberately
// keeps Relay diagnostics on stderr instead of opening the production application log.
public static class AppLog
{
    public static string DefaultLogDirectory => string.Empty;
}

internal sealed class SpikeRelayLog : IAppLogWriter
{
    internal static SpikeRelayLog Instance { get; } = new();
    private SpikeRelayLog() { }
    public void Write(AppLogEntry entry) => Console.Error.WriteLine($"Relay: {entry.Action ?? entry.Event}; outcome={entry.Outcome ?? "unknown"}; code={entry.Code ?? "none"}");
}

internal sealed class ScreenViewWebRtcException(string message, Exception? innerException = null)
    : Exception(message, innerException);
