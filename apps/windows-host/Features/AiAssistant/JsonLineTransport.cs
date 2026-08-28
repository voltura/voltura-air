using System.Text;

namespace VolturaAir.Host.Features.AiAssistant;

internal interface IJsonLineTransport : IAsyncDisposable
{
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}

internal sealed class StdioJsonLineTransport : IJsonLineTransport
{
    internal const int MaximumRecordCharacters = 8 * 1024 * 1024;
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly char[] _readBuffer = new char[4096];
    private readonly StringBuilder _pending = new();
    private int _readOffset;
    private int _readCount;

    internal StdioJsonLineTransport(TextReader reader, TextWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            for (int index = _readOffset; index < _readCount; index++)
            {
                if (_readBuffer[index] != '\n') continue;
                AppendBounded(_readBuffer.AsSpan(_readOffset, index - _readOffset));
                _readOffset = index + 1;
                return TakePendingLine();
            }

            if (_readOffset < _readCount)
                AppendBounded(_readBuffer.AsSpan(_readOffset, _readCount - _readOffset));
            _readOffset = 0;
            _readCount = await _reader.ReadAsync(_readBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (_readCount != 0) continue;
            return _pending.Length == 0 ? null : TakePendingLine();
        }
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        if (line.Length > MaximumRecordCharacters)
            throw new CodexCompatibilityException("A Codex app-server request exceeded Voltura Air's record limit.");
        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AppendBounded(ReadOnlySpan<char> value)
    {
        if (_pending.Length > MaximumRecordCharacters - value.Length)
            throw new CodexCompatibilityException("A Codex app-server response exceeded Voltura Air's record limit.");
        _pending.Append(value);
    }

    private string TakePendingLine()
    {
        if (_pending.Length > 0 && _pending[^1] == '\r') _pending.Length--;
        string line = _pending.ToString();
        _pending.Clear();
        return line;
    }

    public ValueTask DisposeAsync()
    {
        try { _writer.Dispose(); }
        finally { _reader.Dispose(); }
        return ValueTask.CompletedTask;
    }
}
