using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class AppLogFileStore(
    string logDirectory,
    Func<int> maxAgeDays,
    Func<DateTimeOffset> now,
    Action<string, string>? appendLine = null,
    Action<string>? deleteFile = null)
{
    private const int MaximumCandidateFiles = 400;
    private const int MaximumLineCharacters = 64 * 1024;
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Lock _gate = new();
    private readonly Action<string, string> _appendLine = appendLine ?? AppendLine;
    private readonly Action<string> _deleteFile = deleteFile ?? File.Delete;
    private DateOnly? _lastPruneUtcDate;

    public string LogDirectory { get; } = logDirectory;

    public void Append(DateTimeOffset timestamp, AppLogEntry entry) => Append(timestamp, [entry]);

    public void Append(DateTimeOffset timestamp, IReadOnlyList<AppLogEntry> entries)
    {
        lock (_gate)
        {
            var path = Path.Combine(LogDirectory, $"app-log-{timestamp:yyyy-MM-dd}.jsonl");
            TryPruneExpired(timestamp, force: false);
            Directory.CreateDirectory(LogDirectory);
            foreach (var entry in entries)
            {
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
        }
    }

    public AppLogReadResult Read(AppLogQuery query)
    {
        try
        {
            string[] files;
            lock (_gate)
            {
                TryPruneExpired(now(), force: true);
                if (!Directory.Exists(LogDirectory))
                {
                    return new AppLogReadResult(true, []);
                }

                files = [.. Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl")
                    .Where(path => IsCandidateFile(path, query))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumCandidateFiles)];
            }

            var limit = Math.Clamp(query.MaxEntries, 1, 5000);
            var entries = new Queue<AppLogRecord>(limit);
            var truncated = false;
            foreach (var file in files)
            {
                foreach (var line in ReadLinesShared(file))
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

            return new AppLogReadResult(true, [.. entries], truncated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new AppLogReadResult(false, [], Error: exception.Message);
        }
    }

    public AppLogDeleteResult DeleteAll()
    {
        var deleted = 0;
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(LogDirectory))
                {
                    return new AppLogDeleteResult(true, 0);
                }

                foreach (var path in Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl"))
                {
                    _deleteFile(path);
                    deleted++;
                }

                return new AppLogDeleteResult(true, deleted);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new AppLogDeleteResult(false, deleted, exception.Message);
        }
    }

    private void TryPruneExpired(DateTimeOffset now, bool force)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (!force && _lastPruneUtcDate == utcDate)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                return;
            }

            var retentionDays = Math.Clamp(
                maxAgeDays(),
                AppLoggingSettings.MinMaxAgeDays,
                AppLoggingSettings.MaxMaxAgeDays);
            var cutoff = now.UtcDateTime.AddDays(-retentionDays);
            foreach (var path in Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }

            _lastPruneUtcDate = utcDate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Voltura Air could not prune the application log: {0}", exception.Message);
        }
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var line = new StringBuilder(Math.Min(MaximumLineCharacters, 4096));
        var oversized = false;
        var buffer = new char[4096];
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < count; index += 1)
            {
                char value = buffer[index];
                if (value == '\n')
                {
                    if (!oversized)
                    {
                        if (line.Length > 0 && line[^1] == '\r') line.Length -= 1;
                        yield return line.ToString();
                    }
                    line.Clear();
                    oversized = false;
                }
                else if (!oversized)
                {
                    if (line.Length == MaximumLineCharacters)
                    {
                        line.Clear();
                        oversized = true;
                    }
                    else
                    {
                        line.Append(value);
                    }
                }
            }
        }

        if (!oversized && line.Length > 0)
        {
            if (line[^1] == '\r') line.Length -= 1;
            yield return line.ToString();
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

        return string.IsNullOrWhiteSpace(query.Client) ||
            entry.ClientId?.Contains(query.Client.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool MatchesExact(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);

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
}
