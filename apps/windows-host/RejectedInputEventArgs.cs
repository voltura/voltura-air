namespace VolturaAir.Host;

internal sealed class RejectedInputEventArgs : EventArgs
{
    public RejectedInputEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
