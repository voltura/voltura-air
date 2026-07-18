using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

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
    event EventHandler? Changed;

    string LogDirectory { get; }

    void Write(AppLogEntry entry);

    AppLogReadResult Read(AppLogQuery query);

    AppLogDeleteResult DeleteAll();
}

public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();

    private NullAppLog()
    {
    }

    public string LogDirectory => AppLog.DefaultLogDirectory;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public void Write(AppLogEntry entry)
    {
    }

    public AppLogReadResult Read(AppLogQuery query) => new(true, []);

    public AppLogDeleteResult DeleteAll() => new(true, 0);
}
