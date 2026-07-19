using System.Threading.Channels;

namespace VolturaAir.Host;

public sealed class AppLog : IAppLog, IAsyncDisposable
{
    private const int MaxPendingWrites = 512;
    private readonly Func<bool> _isEnabled;
    private readonly Func<DateTimeOffset> _now;
    private readonly AppLogFileStore _store;
    private readonly Channel<LogWorkItem> _pendingWrites;
    private readonly Task _writerTask;
    private int _disposeState;
    private int _droppedEntryCount;
    private int _notifyingChanged;
    private int _reportedWriteFailure;

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
        _now = now;
        _store = new AppLogFileStore(logDirectory, maxAgeDays, now, appendLine);
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

    public string LogDirectory => _store.LogDirectory;

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
        catch (Exception exception)
        {
            ReportWriteFailure(exception);
        }
    }

    public AppLogReadResult Read(AppLogQuery query)
    {
        FlushPendingWrites();
        return _store.Read(query);
    }

    public AppLogDeleteResult DeleteAll()
    {
        FlushPendingWrites();
        return _store.DeleteAll();
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportWriteFailure(exception);
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
            var droppedEntries = Interlocked.Exchange(ref _droppedEntryCount, 0);
            if (droppedEntries > 0)
            {
                _store.Append(
                    pendingEntry.Timestamp,
                    [CreateDroppedEntriesRecord(droppedEntries), pendingEntry.Entry]);
                return true;
            }

            _store.Append(pendingEntry.Timestamp, pendingEntry.Entry);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportWriteFailure(exception);
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
            _store.Append(_now(), CreateDroppedEntriesRecord(droppedEntries));
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportWriteFailure(exception);
            return false;
        }
    }

    private static AppLogEntry CreateDroppedEntriesRecord(int droppedEntries) => new(
        Event: "host_lifecycle",
        Source: "windows_host",
        Action: "application_log_backpressure",
        Outcome: "entries_dropped",
        Detail: $"count={droppedEntries}");

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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportWriteFailure(exception);
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportWriteFailure(exception);
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
}
