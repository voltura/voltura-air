namespace VolturaAir.Host.Tests;

public sealed class FileManagerServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "voltura-air-file-manager-tests", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PagesAreBoundedOpaqueExhaustiveAndSortedAcrossBoundaries()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        for (var index = 0; index < 105; index++) Directory.CreateDirectory(Path.Combine(left, $"folder-{index:D3}"));
        for (var index = 0; index < 105; index++) File.WriteAllText(Path.Combine(left, $"file-{index:D3}.txt"), new string('x', index + 1));

        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");

        Assert.Equal(FileManagerProtocol.PageSize, session.Left.Entries.Length);
        Assert.Equal(210, session.Left.TotalCount);
        Assert.All(session.Left.Entries, entry => Assert.Equal("folder", entry.Kind));
        Assert.NotNull(session.Left.Continuation);
        Assert.DoesNotContain(left, session.Left.Continuation!, StringComparison.OrdinalIgnoreCase);

        Assert.True(service.TryGetPage("client-a", session.SessionId, "left", session.Left.Revision, session.Left.Continuation!, out var second, out var code));
        Assert.Equal("accepted", code);
        Assert.NotNull(second);
        Assert.Equal(100, second!.Entries.Length);
        Assert.Equal(5, second.Entries.TakeWhile(entry => entry.Kind == "folder").Count());
        Assert.NotNull(second.Continuation);

        Assert.True(service.TryGetPage("client-a", session.SessionId, "left", second.Revision, second.Continuation!, out var final, out code));
        Assert.Equal(10, final!.Entries.Length);
        Assert.Null(final.Continuation);

        Assert.True(service.TrySort("client-a", session.SessionId, "left", "size", descending: true, out var sorted, out code));
        Assert.Equal("size", sorted!.SortBy);
        Assert.True(sorted.Descending);
        Assert.All(sorted.Entries, entry => Assert.Equal("folder", entry.Kind));
        Assert.True(service.TryGetPage("client-a", session.SessionId, "left", sorted.Revision, sorted.Continuation!, out var sortedSecond, out code));
        Assert.Equal([105L, 104L, 103L], sortedSecond!.Entries.Where(entry => entry.Kind == "file").Take(3).Select(entry => entry.Size));
    }

    [Fact]
    public async Task ContinuationsAreSingleUseAndOldRevisionsAreRejected()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        for (var index = 0; index < 101; index++) File.WriteAllText(Path.Combine(left, $"file-{index:D3}.txt"), "x");

        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var continuation = session.Left.Continuation!;

        Assert.True(service.TryGetPage("client-a", session.SessionId, "left", session.Left.Revision, continuation, out _, out _));
        Assert.False(service.TryGetPage("client-a", session.SessionId, "left", session.Left.Revision, continuation, out _, out var duplicateCode));
        Assert.Equal("stale-panel", duplicateCode);

        Assert.True(service.TryRefresh("client-a", session.SessionId, "left", out var refreshed, out _));
        Assert.NotEqual(session.Left.Revision, refreshed!.Revision);
        Assert.False(service.TryGetPage("client-a", session.SessionId, "left", session.Left.Revision, continuation, out _, out var staleCode));
        Assert.Equal("stale-panel", staleCode);
    }

    [Fact]
    public async Task SelectAllTargetsTheCompleteRevisionNotOnlyLoadedEntries()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        for (var index = 0; index < 205; index++) File.WriteAllText(Path.Combine(left, $"file-{index:D3}.txt"), "x");
        var platform = new FakePlatform();
        await using var service = CreateService(left, right, out _, platform);
        var session = service.OpenSession("client-a");
        var excluded = session.Left.Entries[2].Id;

        var result = service.SetClipboard("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(true, [], [excluded]), move: false);

        Assert.True(result.Succeeded);
        Assert.Equal(204, platform.ClipboardPaths.Length);
        Assert.DoesNotContain(platform.ClipboardPaths, path => Path.GetFileName(path) == session.Left.Entries[2].Name);
    }

    [Fact]
    public async Task StaleRevisionPerformsNoClipboardAction()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "x");
        var platform = new FakePlatform();
        await using var service = CreateService(left, right, out _, platform);
        var session = service.OpenSession("client-a");
        Assert.True(service.TryRefresh("client-a", session.SessionId, "left", out _, out _));

        var result = service.SetClipboard("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(true, [], []), move: false);

        Assert.False(result.Succeeded);
        Assert.Equal("stale-panel", result.Code);
        Assert.Empty(platform.ClipboardPaths);
    }

    [Fact]
    public async Task ExternalDirectoryChangeMakesTheCurrentRevisionStaleBeforeAnOperationStarts()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "first.txt"), "x");
        var platform = new FakePlatform();
        await using var service = CreateService(left, right, out _, platform);
        var session = service.OpenSession("client-a");
        File.WriteAllText(Path.Combine(left, "arrived.txt"), "new");

        var result = service.SetClipboard("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(true, [], []), move: false);

        Assert.False(result.Succeeded);
        Assert.Equal("stale-panel", result.Code);
        Assert.Empty(platform.ClipboardPaths);
    }

    [Fact]
    public async Task DeleteIsRejectedBeforeQueueingWhenAnyItemCannotBeRecycled()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(left, "reject.txt"), "x");
        var platform = new FakePlatform { RejectedRecycleName = "reject.txt" };
        await using var service = CreateService(left, right, out _, platform);
        var session = service.OpenSession("client-a");

        var result = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(true, [], []), "delete", null, null);

        Assert.False(result.Succeeded);
        Assert.Equal("cannot-recycle", result.Code);
        Assert.Empty(service.GetJobs("client-a"));
        Assert.Empty(platform.RecycledPaths);
    }

    [Fact]
    public async Task LastValidPanelLocationsAreRestoredPerDevice()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var nested = Directory.CreateDirectory(Path.Combine(left, "nested")).FullName;
        var store = new MemoryLocationStore();

        await using (var service = CreateService(left, right, out _, locations: store))
        {
            var session = service.OpenSession("client-a");
            var target = Assert.Single(session.Left.Entries, entry => entry.Name == "nested");
            Assert.True(service.TryNavigate("client-a", session.SessionId, "left", session.Left.Revision, target.Id, out _, out _));
        }

        await using var restoredService = CreateService(right, right, out _, locations: store);
        var restored = restoredService.OpenSession("client-a");
        Assert.Equal(nested, restored.Left.DisplayPath);
    }

    [Fact]
    public async Task ConflictsPauseOnlyTheOriginatingDevicesJobUntilResolved()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "same.txt"), "new");
        File.WriteAllText(Path.Combine(right, "same.txt"), "old");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);

        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null);
        Assert.True(created.Succeeded);
        var attention = await WaitForJobAsync(service, "client-a", created.Job!.JobId, "needs-attention");

        Assert.Equal("same.txt", attention.ConflictName);
        Assert.Empty(service.GetJobs("client-b"));
        Assert.False(service.ResolveConflict("client-b", created.Job.JobId, "skip", applyToAll: false));
        Assert.True(service.ResolveConflict("client-a", created.Job.JobId, "skip", applyToAll: false));
        await WaitForJobAsync(service, "client-a", created.Job.JobId, "completed");
        Assert.Equal("old", File.ReadAllText(Path.Combine(right, "same.txt")));
    }

    [Fact]
    public async Task RecoveryDeletesRecordedTemporaryFilesAndReportsInterruptedOnlyToOwner()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var temporary = Path.Combine(right, "movie.part");
        File.WriteAllText(temporary, "partial");
        var journal = new MemoryJobJournal
        {
            Entries = [new FileJobJournalEntry("job-a", "client-a", "copy", [temporary])]
        };

        await using var service = new FileManagerService(new FakePlatform(), left, right, new MemoryLocationStore(), journal);

        Assert.False(File.Exists(temporary));
        var interrupted = Assert.Single(service.GetJobs("client-a"));
        Assert.Equal("interrupted", interrupted.State);
        Assert.Empty(service.GetJobs("client-b"));
    }

    private static async Task<FileJobSnapshot> WaitForJobAsync(FileManagerService service, string clientId, string jobId, string state)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var job = service.GetJobs(clientId).Single(candidate => candidate.JobId == jobId);
            if (job.State == state) return job;
            await Task.Delay(10);
        }
        throw new TimeoutException($"The file job did not enter {state}.");
    }

    private string CreateDirectory(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static FileManagerService CreateService(
        string left,
        string right,
        out MemoryJobJournal journal,
        FakePlatform? platform = null,
        MemoryLocationStore? locations = null)
    {
        journal = new MemoryJobJournal();
        return new FileManagerService(platform ?? new FakePlatform(), left, right, locations ?? new MemoryLocationStore(), journal);
    }

    private sealed class FakePlatform : IFileManagerPlatform
    {
        public string[] ClipboardPaths { get; private set; } = [];
        public List<string> RecycledPaths { get; } = [];
        public string? RejectedRecycleName { get; init; }

        public (bool Succeeded, string? Code, string Message) SetFileClipboard(IReadOnlyList<string> paths, bool move)
        {
            ClipboardPaths = [.. paths];
            return (true, null, "Ready.");
        }

        public (bool Succeeded, string[] Paths, bool Move, string? Code, string Message) GetFileClipboard() =>
            (ClipboardPaths.Length > 0, ClipboardPaths, false, ClipboardPaths.Length > 0 ? null : "clipboard-empty", "Clipboard.");

        public void ClearFileClipboardIfMatches(IReadOnlyList<string> paths)
        {
            if (ClipboardPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)) ClipboardPaths = [];
        }

        public (bool Succeeded, string? Code, string Message) OpenWithShell(string path) => (true, null, "Opened.");
        public bool CanRecycle(string path) => !string.Equals(Path.GetFileName(path), RejectedRecycleName, StringComparison.OrdinalIgnoreCase);
        public void Recycle(string path) => RecycledPaths.Add(path);
    }

    private sealed class MemoryLocationStore : IFileManagerLocationStore
    {
        private readonly Dictionary<(string ClientId, string Panel), string> _values = [];
        public string? Load(string clientId, string panel) => _values.GetValueOrDefault((clientId, panel));
        public void Save(string clientId, string panel, string path) => _values[(clientId, panel)] = path;
    }

    private sealed class MemoryJobJournal : IFileJobJournal
    {
        public FileJobJournalEntry[] Entries { get; set; } = [];
        public FileJobJournalEntry[] Load() => Entries;
        public void Save(FileJobJournalEntry[] entries) => Entries = entries;
    }
}
