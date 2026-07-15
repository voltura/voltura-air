namespace VolturaAir.Host;

internal sealed class RejectedInputEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
