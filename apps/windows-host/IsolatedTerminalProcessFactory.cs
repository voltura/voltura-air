using System.Threading.Channels;

namespace VolturaAir.Host;

internal sealed class IsolatedTerminalProcessFactory : ITerminalProcessFactory
{
    public ITerminalProcess Start(ushort columns, ushort rows) => new IsolatedTerminalProcess();

    private sealed class IsolatedTerminalProcess : ITerminalProcess
    {
        private readonly MemoryStream _input = new();
        private readonly MemoryStream _output = new();
        private readonly TaskCompletionSource<int> _exitCode = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Stream Input => _input;
        public Stream Output => _output;
        public Task<int> ExitCode => _exitCode.Task;
        public void Resize(ushort columns, ushort rows) { }
        public void Terminate() => _exitCode.TrySetResult(-1);
        public ValueTask DisposeAsync()
        {
            Terminate();
            _input.Dispose();
            _output.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
