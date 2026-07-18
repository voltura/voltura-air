using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed partial class AppLog
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public AppLogReadResult Read(AppLogQuery query)
    {
        FlushPendingWrites();
        try
        {
            string[] files;
            lock (_gate)
            {
                TryPruneExpired(_now(), force: true);
                if (!Directory.Exists(LogDirectory))
                {
                    return new AppLogReadResult(true, []);
                }

                files = [.. Directory.EnumerateFiles(LogDirectory, "app-log-*.jsonl")
                    .Where(path => IsCandidateFile(path, query))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppLogReadResult(false, [], Error: ex.Message);
        }
    }

    public AppLogDeleteResult DeleteAll()
    {
        FlushPendingWrites();
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
        while (reader.ReadLine() is { } line)
        {
            yield return line;
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

}
