using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace VolturaAir.Host;

public sealed partial class AppLog : IAppLog, IAsyncDisposable
{
    private const int MaxPendingWrites = 512;
    private readonly Lock _gate = new();
    private readonly Func<bool> _isEnabled;
    private readonly Func<int> _maxAgeDays;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string, string> _appendLine;
    private readonly Channel<LogWorkItem> _pendingWrites;
    private readonly Task _writerTask;
    private int _disposeState;
    private int _droppedEntryCount;
    private int _notifyingChanged;
    private int _reportedWriteFailure;
    private DateOnly? _lastPruneUtcDate;

    public AppLog()
        : this(
            AppLoggingSettings.IsEnabled,
            AppLoggingSettings.GetMaxAgeDays,
            () => DateTimeOffset.UtcNow,
            DefaultLogDirectory)
    {
    }

    internal AppLog(
        Func<bool> isEnabled,
        Func<int> maxAgeDays,
        Func<DateTimeOffset> now,
        string logDirectory,
        Action<string, string>? appendLine = null)
    {
        _isEnabled = isEnabled;
        _maxAgeDays = maxAgeDays;
        _now = now;
        _appendLine = appendLine ?? AppendLine;
        LogDirectory = logDirectory;
        _pendingWrites = Channel.CreateBounded<LogWorkItem>(new BoundedChannelOptions(MaxPendingWrites)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = ProcessPendingWritesAsync();
    }

    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Voltura Air",
        "Logs");

    public string LogDirectory { get; }

    public event EventHandler? Changed;

    public void Write(AppLogEntry entry)
    {
        try
        {
            if (Volatile.Read(ref _disposeState) != 0 || !_isEnabled())
            {
                return;
            }

            var workItem = LogWorkItem.ForEntry(new PendingLogEntry(_now(), entry));
            if (!_pendingWrites.Writer.TryWrite(workItem))
            {
                Interlocked.Increment(ref _droppedEntryCount);
            }
        }
        catch (Exception ex)
        {
            ReportWriteFailure(ex);
        }
    }

    internal async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            await _writerTask.WaitAsync(cancellationToken);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _pendingWrites.Writer.WriteAsync(LogWorkItem.ForFlush(completion), cancellationToken);
        }
        catch (ChannelClosedException)
        {
            await _writerTask.WaitAsync(cancellationToken);
            return;
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            _pendingWrites.Writer.TryComplete();
        }

        await _writerTask;
    }

    private async Task ProcessPendingWritesAsync()
    {
        try
        {
            while (await _pendingWrites.Reader.WaitToReadAsync())
            {
                var changed = false;
                while (_pendingWrites.Reader.TryRead(out var workItem))
                {
                    if (workItem.FlushCompletion is { } completion)
                    {
                        changed |= PersistDroppedEntries();
                        RaiseChanged(changed);
                        changed = false;
                        completion.TrySetResult();
                        continue;
                    }

                    if (workItem.Entry is { } pendingEntry)
                    {
                        changed |= PersistEntry(pendingEntry);
                    }
                }

                RaiseChanged(changed);
            }

            RaiseChanged(PersistDroppedEntries());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportWriteFailure(ex);
            Interlocked.Exchange(ref _disposeState, 1);
            _pendingWrites.Writer.TryComplete();
            while (_pendingWrites.Reader.TryRead(out var workItem))
            {
                workItem.FlushCompletion?.TrySetResult();
            }
        }
    }

    private bool PersistEntry(PendingLogEntry pendingEntry)
    {
        try
        {
            lock (_gate)
            {
                var droppedEntries = Interlocked.Exchange(ref _droppedEntryCount, 0);
                if (droppedEntries > 0)
                {
                    AppendEntry(
                        pendingEntry.Timestamp,
                        new AppLogEntry(
                            Event: "host_lifecycle",
                            Source: "windows_host",
                            Action: "application_log_backpressure",
                            Outcome: "entries_dropped",
                            Detail: $"count={droppedEntries}"));
                }

                AppendEntry(pendingEntry.Timestamp, pendingEntry.Entry);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportWriteFailure(ex);
            return false;
        }
    }

    private bool PersistDroppedEntries()
    {
        var droppedEntries = Interlocked.Exchange(ref _droppedEntryCount, 0);
        if (droppedEntries == 0)
        {
            return false;
        }

        try
        {
            lock (_gate)
            {
                AppendEntry(
                    _now(),
                    new AppLogEntry(
                        Event: "host_lifecycle",
                        Source: "windows_host",
                        Action: "application_log_backpressure",
                        Outcome: "entries_dropped",
                        Detail: $"count={droppedEntries}"));
            }

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportWriteFailure(ex);
            return false;
        }
    }

    private void AppendEntry(DateTimeOffset timestamp, AppLogEntry entry)
    {
        var path = Path.Combine(LogDirectory, $"app-log-{timestamp:yyyy-MM-dd}.jsonl");
        TryPruneExpired(timestamp, force: false);
        Directory.CreateDirectory(LogDirectory);
        var line = JsonSerializer.Serialize(new
        {
            timestampUtc = timestamp.ToUniversalTime(),
            @event = entry.Event,
            source = entry.Source,
            clientId = entry.ClientId,
            messageType = entry.MessageType,
            action = entry.Action,
            outcome = entry.Outcome,
            code = entry.Code,
            win32Error = entry.Win32Error,
            detail = entry.Detail
        });
        _appendLine(path, line);
    }

    private void FlushPendingWrites()
    {
        if (Volatile.Read(ref _notifyingChanged) != 0)
        {
            return;
        }

        try
        {
            FlushAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportWriteFailure(ex);
        }
    }

    private void RaiseChanged(bool changed)
    {
        if (!changed)
        {
            return;
        }

        try
        {
            Volatile.Write(ref _notifyingChanged, 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportWriteFailure(ex);
        }
        finally
        {
            Volatile.Write(ref _notifyingChanged, 0);
        }
    }

    private void ReportWriteFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref _reportedWriteFailure, 1) == 0)
        {
            Console.Error.WriteLine("Voltura Air could not write the application log: {0}", exception.Message);
        }
    }

    private readonly record struct PendingLogEntry(DateTimeOffset Timestamp, AppLogEntry Entry);

    private readonly record struct LogWorkItem(PendingLogEntry? Entry, TaskCompletionSource? FlushCompletion)
    {
        public static LogWorkItem ForEntry(PendingLogEntry entry) => new(entry, null);

        public static LogWorkItem ForFlush(TaskCompletionSource completion) => new(null, completion);
    }

    private static void AppendLine(string path, string line)
    {
        using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(line);
    }

    private void TryPruneExpired(DateTimeOffset now, bool force)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (!force && _lastPruneUtcDate == utcDate)
        {
            return;
        }

        _lastPruneUtcDate = utcDate;
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                return;
            }

            var maxAgeDays = Math.Clamp(
                _maxAgeDays(),
                AppLoggingSettings.MinMaxAgeDays,
                AppLoggingSettings.MaxMaxAgeDays);
            var cutoff = now.UtcDateTime.AddDays(-maxAgeDays);
            foreach (var path in Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Voltura Air could not prune the application log: {0}", ex.Message);
        }
    }
}
