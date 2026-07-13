using System.Globalization;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed record AppLogEntry(
    string Event,
    string Source,
    string? ClientId = null,
    string? MessageType = null,
    string? Action = null,
    string? Outcome = null,
    string? Code = null,
    int? Win32Error = null,
    string? Detail = null);

public sealed record AppLogRecord(
    DateTimeOffset TimestampUtc,
    string Event,
    string Source,
    string? ClientId,
    string? MessageType,
    string? Action,
    string? Outcome,
    string? Code,
    int? Win32Error,
    string? Detail);

public sealed record AppLogQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string? Event = null,
    string? Source = null,
    string? Action = null,
    string? Client = null,
    int MaxEntries = 1000);

public sealed record AppLogReadResult(
    bool Succeeded,
    IReadOnlyList<AppLogRecord> Entries,
    bool Truncated = false,
    string? Error = null);

public sealed record AppLogDeleteResult(bool Succeeded, int DeletedFiles, string? Error = null);

public interface IAppLog
{
    string LogDirectory { get; }

    void Write(AppLogEntry entry);

    AppLogReadResult Read(AppLogQuery query);

    AppLogDeleteResult DeleteAll();
}

public sealed class AppLog : IAppLog
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private readonly Func<bool> _isEnabled;
    private readonly Func<int> _maxAgeDays;
    private readonly Func<DateTimeOffset> _now;
    private bool _reportedWriteFailure;
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
        string logDirectory)
    {
        _isEnabled = isEnabled;
        _maxAgeDays = maxAgeDays;
        _now = now;
        LogDirectory = logDirectory;
    }

    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Voltura Air",
        "Logs");

    public string LogDirectory { get; }

    public void Write(AppLogEntry entry)
    {
        try
        {
            if (!_isEnabled())
            {
                return;
            }

            lock (_gate)
            {
                var timestamp = _now();
                TryPruneExpired(timestamp, force: false);
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"app-log-{timestamp:yyyy-MM-dd}.jsonl");
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
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            if (!_reportedWriteFailure)
            {
                _reportedWriteFailure = true;
                Console.Error.WriteLine("Voltura Air could not write the application log: {0}", ex.Message);
            }
        }
    }

    public AppLogReadResult Read(AppLogQuery query)
    {
        try
        {
            lock (_gate)
            {
                TryPruneExpired(_now(), force: true);
                if (!Directory.Exists(LogDirectory))
                {
                    return new AppLogReadResult(true, []);
                }

                var limit = Math.Clamp(query.MaxEntries, 1, 5000);
                var entries = new Queue<AppLogRecord>(limit);
                var truncated = false;
                var files = Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl")
                    .Where(path => IsCandidateFile(path, query))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        AppLogRecord? entry;
                        try
                        {
                            entry = JsonSerializer.Deserialize<AppLogRecord>(line, ReadOptions);
                        }
                        catch (JsonException)
                        {
                            continue;
                        }

                        if (entry is null || !Matches(entry, query))
                        {
                            continue;
                        }

                        if (entries.Count == limit)
                        {
                            entries.Dequeue();
                            truncated = true;
                        }

                        entries.Enqueue(entry);
                    }
                }

                return new AppLogReadResult(true, entries.ToArray(), truncated);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppLogReadResult(false, [], Error: ex.Message);
        }
    }

    public AppLogDeleteResult DeleteAll()
    {
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(LogDirectory))
                {
                    return new AppLogDeleteResult(true, 0);
                }

                var deleted = 0;
                foreach (var path in Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl"))
                {
                    File.Delete(path);
                    deleted += 1;
                }

                return new AppLogDeleteResult(true, deleted);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppLogDeleteResult(false, 0, ex.Message);
        }
    }

    private static bool Matches(AppLogRecord entry, AppLogQuery query)
    {
        var localDate = DateOnly.FromDateTime(entry.TimestampUtc.LocalDateTime);
        if (localDate < query.FromDate || localDate > query.ToDate)
        {
            return false;
        }

        if (!MatchesExact(entry.Event, query.Event) ||
            !MatchesExact(entry.Source, query.Source) ||
            !MatchesExact(entry.Action, query.Action))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Client) &&
            !(entry.ClientId?.Contains(query.Client.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesExact(string? value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) || string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCandidateFile(string path, AppLogQuery query)
    {
        const string prefix = "app-log-";
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !DateOnly.TryParseExact(
                name[prefix.Length..],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var utcDate))
        {
            return false;
        }

        var firstUtcDate = query.FromDate == DateOnly.MinValue ? query.FromDate : query.FromDate.AddDays(-1);
        var lastUtcDate = query.ToDate == DateOnly.MaxValue ? query.ToDate : query.ToDate.AddDays(1);
        return utcDate >= firstUtcDate && utcDate <= lastUtcDate;
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

public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();

    private NullAppLog()
    {
    }

    public string LogDirectory => AppLog.DefaultLogDirectory;

    public void Write(AppLogEntry entry)
    {
    }

    public AppLogReadResult Read(AppLogQuery query) => new(true, []);

    public AppLogDeleteResult DeleteAll() => new(true, 0);
}
