using System.Text.Json;
using System.Globalization;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AppLogTests
{
    private static readonly DateTimeOffset TestNow = DateTimeOffset.Parse("2026-07-13T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task DisabledLogWritesNoFile()
    {
        using var folder = new TemporaryFolder();
        await using var log = CreateLog(folder, enabled: false);
        var changes = 0;
        log.Changed += (_, _) => changes += 1;

        log.Write(new AppLogEntry("command_received", "remote_client", "client-a", "system.power", "lock"));

        Assert.Empty(Directory.EnumerateFiles(folder.Path));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task EnabledLogWritesStructuredSanitizedEntries()
    {
        using var folder = new TemporaryFolder();
        await using var log = CreateLog(folder);
        var changes = 0;
        log.Changed += (_, _) => changes += 1;

        log.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "remote_client",
            ClientId: "client-a",
            MessageType: "system.power",
            Action: "lock",
            Outcome: "execution_failed",
            Code: "VAIR-POWER-EXECUTION-FAILED",
            Win32Error: 5));
        await log.FlushAsync();

        var path = Assert.Single(Directory.EnumerateFiles(folder.Path));
        Assert.EndsWith("app-log-2026-07-13.jsonl", path);
        using var entry = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("action_taken", entry.RootElement.GetProperty("event").GetString());
        Assert.Equal("remote_client", entry.RootElement.GetProperty("source").GetString());
        Assert.Equal(5, entry.RootElement.GetProperty("win32Error").GetInt32());
        Assert.DoesNotContain("secret", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ReadFiltersByDateEventSourceActionAndClient()
    {
        using var folder = new TemporaryFolder();
        var now = TestNow;
        await using var log = new AppLog(() => true, () => 30, () => now, folder.Path);
        log.Write(new AppLogEntry("host_action", "windows_host", Action: "enable_windows_locking", Outcome: "failed"));
        log.Write(new AppLogEntry("action_taken", "remote_client", "client-a", "system.power", "lock", "execution_failed"));
        log.Write(new AppLogEntry("action_taken", "remote_client", "client-b", "system.power", "displayOff", "request_accepted"));
        var localDate = DateOnly.FromDateTime(now.LocalDateTime);

        var result = log.Read(new AppLogQuery(
            localDate,
            localDate,
            Event: "action_taken",
            Source: "remote_client",
            Action: "lock",
            Client: "client-a"));

        var entry = Assert.Single(result.Entries);
        Assert.True(result.Succeeded);
        Assert.Equal("lock", entry.Action);
        Assert.Equal("client-a", entry.ClientId);
    }

    [Fact]
    public async Task WritePrunesFilesOlderThanConfiguredMaximumAge()
    {
        using var folder = new TemporaryFolder();
        var oldPath = Path.Combine(folder.Path, "app-log-2026-07-01.jsonl");
        File.WriteAllText(oldPath, "{}" + Environment.NewLine);
        File.SetLastWriteTimeUtc(oldPath, TestNow.UtcDateTime.AddDays(-3));
        await using var log = CreateLog(folder, maxAgeDays: 2);

        log.Write(new AppLogEntry("host_action", "windows_host", Action: "test"));
        await log.FlushAsync();

        Assert.False(File.Exists(oldPath));
        Assert.Single(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public async Task DeleteAllRemovesApplicationLogFiles()
    {
        using var folder = new TemporaryFolder();
        await using var log = CreateLog(folder);
        log.Write(new AppLogEntry("host_action", "windows_host", Action: "test"));

        var result = log.DeleteAll();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public async Task KeepsNewEntriesReadableAfterQueries()
    {
        using var folder = new TemporaryFolder();
        await using var log = CreateLog(folder);
        var localDate = DateOnly.FromDateTime(TestNow.LocalDateTime);

        log.Write(new AppLogEntry("host_action", "windows_host", Action: "first"));
        var firstRead = log.Read(new AppLogQuery(localDate, localDate));
        log.Write(new AppLogEntry("host_action", "windows_host", Action: "second"));
        var secondRead = log.Read(new AppLogQuery(localDate, localDate));

        Assert.Single(firstRead.Entries);
        Assert.Equal(["first", "second"], secondRead.Entries.Select(entry => entry.Action));
    }

    [Fact]
    public async Task LoggingFailureNeverBreaksHostActions()
    {
        using var folder = new TemporaryFolder();
        var failures = new List<Exception>();
        await using var log = new AppLog(
            () => throw new InvalidOperationException("settings unavailable"),
            () => 2,
            () => TestNow,
            folder.Path,
            reportWriteFailure: failures.Add);

        var exception = Record.Exception(() => log.Write(new AppLogEntry("host_action", "windows_host", Action: "test")));

        Assert.Null(exception);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
        var failure = Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("settings unavailable", failure.Message);
    }

    [Fact]
    public async Task FullWriteQueueDropsWithoutBlockingAndRecordsBackpressure()
    {
        using var folder = new TemporaryFolder();
        using var writerEntered = new ManualResetEventSlim();
        using var releaseWriter = new ManualResetEventSlim();
        var blocked = 0;
        await using var log = new AppLog(
            () => true,
            () => 2,
            () => TestNow,
            folder.Path,
            (path, line) =>
            {
                if (Interlocked.Exchange(ref blocked, 1) == 0)
                {
                    writerEntered.Set();
                    releaseWriter.Wait(TimeSpan.FromSeconds(3));
                }

                File.AppendAllText(path, line + Environment.NewLine);
            });

        log.Write(new AppLogEntry("host_action", "windows_host", Action: "first"));
        Assert.True(writerEntered.Wait(TimeSpan.FromSeconds(3)));
        try
        {
            for (var index = 0; index < 600; index++)
            {
                log.Write(new AppLogEntry("host_action", "windows_host", Action: $"queued-{index}"));
            }
        }
        finally
        {
            releaseWriter.Set();
        }

        await log.FlushAsync();
        var localDate = DateOnly.FromDateTime(TestNow.LocalDateTime);
        var result = log.Read(new AppLogQuery(localDate, localDate, Action: "application_log_backpressure"));

        var backpressure = Assert.Single(result.Entries);
        Assert.Equal("entries_dropped", backpressure.Outcome);
        Assert.StartsWith("count=", backpressure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeFlushesAcceptedEntries()
    {
        using var folder = new TemporaryFolder();
        var log = CreateLog(folder);

        log.Write(new AppLogEntry("host_action", "windows_host", Action: "before-shutdown"));
        await log.DisposeAsync();

        var path = Assert.Single(Directory.EnumerateFiles(folder.Path));
        Assert.Contains("before-shutdown", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedSubscriberCanReadPersistedEntries()
    {
        using var folder = new TemporaryFolder();
        await using var log = CreateLog(folder);
        var localDate = DateOnly.FromDateTime(TestNow.LocalDateTime);
        var observed = new TaskCompletionSource<AppLogReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        log.Changed += (_, _) => observed.TrySetResult(log.Read(new AppLogQuery(localDate, localDate)));

        log.Write(new AppLogEntry("host_action", "windows_host", Action: "event-read"));
        await log.FlushAsync();
        var result = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("event-read", Assert.Single(result.Entries).Action);
    }

    private static AppLog CreateLog(TemporaryFolder folder, bool enabled = true, int maxAgeDays = 30)
    {
        return new AppLog(() => enabled, () => maxAgeDays, () => TestNow, folder.Path);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VolturaAir.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
