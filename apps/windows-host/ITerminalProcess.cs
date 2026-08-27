namespace VolturaAir.Host;

internal interface ITerminalProcess : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    Task<int> ExitCode { get; }
    void Resize(ushort columns, ushort rows);
    void Terminate();
}

internal interface ITerminalProcessFactory
{
    ITerminalProcess Start(ushort columns, ushort rows);
}
