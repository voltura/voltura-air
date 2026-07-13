using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AppLogTests
{
    private static readonly DateTimeOffset TestNow = DateTimeOffset.Parse("2026-07-13T10:00:00Z");

    [Fact]
    public void DisabledLogWritesNoFile()
    {
        using var folder = new TemporaryFolder();
        var log = CreateLog(folder, enabled: false);

        log.Write(new AppLogEntry("command_received", "remote_client", "client-a", "system.power", "lock"));

        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public void EnabledLogWritesStructuredSanitizedEntries()
    {
        using var folder = new TemporaryFolder();
        var log = CreateLog(folder);

        log.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "remote_client",
            ClientId: "client-a",
            MessageType: "system.power",
            Action: "lock",
            Outcome: "execution_failed",
            Code: "VAIR-POWER-EXECUTION-FAILED",
            Win32Error: 5));

        var path = Assert.Single(Directory.EnumerateFiles(folder.Path));
        Assert.EndsWith("app-log-2026-07-13.jsonl", path);
        using var entry = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("action_taken", entry.RootElement.GetProperty("event").GetString());
        Assert.Equal("remote_client", entry.RootElement.GetProperty("source").GetString());
        Assert.Equal(5, entry.RootElement.GetProperty("win32Error").GetInt32());
        Assert.DoesNotContain("secret", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFiltersByDateEventSourceActionAndClient()
    {
        using var folder = new TemporaryFolder();
        var now = TestNow;
        var log = new AppLog(() => true, () => 30, () => now, folder.Path);
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
    public void WritePrunesFilesOlderThanConfiguredMaximumAge()
    {
        using var folder = new TemporaryFolder();
        var oldPath = Path.Combine(folder.Path, "app-log-2026-07-01.jsonl");
        File.WriteAllText(oldPath, "{}" + Environment.NewLine);
        File.SetLastWriteTimeUtc(oldPath, TestNow.UtcDateTime.AddDays(-3));
        var log = CreateLog(folder, maxAgeDays: 2);

        log.Write(new AppLogEntry("host_action", "windows_host", Action: "test"));

        Assert.False(File.Exists(oldPath));
        Assert.Single(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public void DeleteAllRemovesApplicationLogFiles()
    {
        using var folder = new TemporaryFolder();
        var log = CreateLog(folder);
        log.Write(new AppLogEntry("host_action", "windows_host", Action: "test"));

        var result = log.DeleteAll();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public void LoggingFailureNeverBreaksHostActions()
    {
        using var folder = new TemporaryFolder();
        var log = new AppLog(
            () => throw new InvalidOperationException("settings unavailable"),
            () => 2,
            () => TestNow,
            folder.Path);

        var exception = Record.Exception(() => log.Write(new AppLogEntry("host_action", "windows_host", Action: "test")));

        Assert.Null(exception);
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
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
