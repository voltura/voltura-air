namespace VolturaAir.Host.Tests;

public sealed class FileManagerServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "voltura-air-file-manager-tests", Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch (IOException) { }
            }
            Directory.Delete(_root, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CancelledSessionOpenDoesNotCommitAfterBlockedStorageReturns()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        using var locations = new BlockingLocationStore();
        await using var service = new FileManagerService(
            new FakePlatform(),
            left,
            right,
            locations,
            new MemoryJobJournal());
        using var cancellation = new CancellationTokenSource();

        var open = Task.Run(() => service.OpenSession("client-a", cancellation.Token));
        Assert.True(locations.LoadStarted.Wait(TimeSpan.FromSeconds(2)));

        cancellation.Cancel();
        locations.ReleaseLoad.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => open);
        Assert.DoesNotContain("client-a", service.SessionClientIds);
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
    public async Task DirectCopyRejectsAStaleDestinationRevisionBeforeQueueing()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        Assert.True(service.TryRefresh("client-a", session.SessionId, "right", out _, out _));

        var result = service.CreateJob(
            "client-a",
            session.SessionId,
            "left",
            session.Left.Revision,
            new FileManagerSelection(false, [source.Id], []),
            "copy",
            "right",
            null,
            session.Right.Revision);

        Assert.False(result.Succeeded);
        Assert.Equal("stale-panel", result.Code);
        Assert.Empty(service.GetJobs("client-a"));
        Assert.False(File.Exists(Path.Combine(right, "file.txt")));
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

        var result = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(true, [], []), "delete", null, null, null);

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

        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);
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
        var destination = Path.Combine(right, "existing.txt");
        var backup = Path.Combine(right, "existing.txt.voltura-air-backup");
        File.WriteAllText(backup, "original");
        var journal = new MemoryJobJournal
        {
            Entries = [new FileJobJournalEntry("job-a", "client-a", "copy", [temporary], [new FileJobBackupEntry(destination, backup)])]
        };

        await using var service = new FileManagerService(new FakePlatform(), left, right, new MemoryLocationStore(), journal);

        Assert.False(File.Exists(temporary));
        Assert.Equal("original", File.ReadAllText(destination));
        Assert.False(File.Exists(backup));
        var interrupted = Assert.Single(service.GetJobs("client-a"));
        Assert.Equal("interrupted", interrupted.State);
        Assert.Empty(service.GetJobs("client-b"));
    }

    [Fact]
    public async Task CopyCommitsTheCompleteFileAndRemovesTemporaryOutput()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var content = new string('x', 1024 * 1024 + 17);
        File.WriteAllText(Path.Combine(left, "movie.bin"), content);
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);

        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);

        Assert.True(created.Succeeded);
        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "completed");
        Assert.Equal(content, File.ReadAllText(Path.Combine(right, "movie.bin")));
        Assert.Empty(Directory.EnumerateFiles(right, "*.voltura-air-*.part"));
        Assert.True(File.Exists(Path.Combine(left, "movie.bin")));
    }

    [Fact]
    public async Task ProtectedSystemItemsAreHiddenByDefaultAndCanBeShownPerDevice()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        Directory.CreateDirectory(Path.Combine(left, "visible"));
        var protectedPath = Directory.CreateDirectory(Path.Combine(left, "protected")).FullName;
        File.SetAttributes(protectedPath, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);

        await using (var hiddenService = CreateService(left, right, out _, hideProtectedItems: _ => true))
        {
            var hidden = hiddenService.OpenSession("client-a");
            Assert.Equal(["visible"], hidden.Left.Entries.Select(entry => entry.Name));
            Assert.Equal(1, hidden.Left.TotalCount);
        }

        await using var shownService = CreateService(left, right, out _, hideProtectedItems: clientId => clientId != "client-a");
        var shown = shownService.OpenSession("client-a");
        Assert.Equal(["protected", "visible"], shown.Left.Entries.Select(entry => entry.Name));
        Assert.Contains(shown.Left.Entries, entry => entry.Name == "protected" && entry.Attributes.Contains("hidden") && entry.Attributes.Contains("system"));
    }

    [Fact]
    public async Task TerminalJobsCanOnlyBeDismissedByTheirOriginatingDevice()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);
        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "completed");

        Assert.False(service.ControlJob("client-b", created.Job.JobId, "dismiss"));
        Assert.Single(service.GetJobs("client-a"));
        Assert.True(service.ControlJob("client-a", created.Job.JobId, "dismiss"));
        Assert.Empty(service.GetJobs("client-a"));
    }

    [Fact]
    public async Task CopyRejectsTheSameOrDescendantDestinationBeforeQueueing()
    {
        var root = CreateDirectory("root");
        var selectedDirectory = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
        var descendant = Directory.CreateDirectory(Path.Combine(selectedDirectory, "child")).FullName;

        await using (var sameFolderService = CreateService(root, root, out _))
        {
            var session = sameFolderService.OpenSession("client-a");
            var source = Assert.Single(session.Left.Entries);
            var result = sameFolderService.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);
            Assert.False(result.Succeeded);
            Assert.Equal("invalid-destination", result.Code);
            Assert.Empty(sameFolderService.GetJobs("client-a"));
        }

        await using var descendantService = CreateService(root, descendant, out _);
        var descendantSession = descendantService.OpenSession("client-a");
        var selected = Assert.Single(descendantSession.Left.Entries);
        var descendantResult = descendantService.CreateJob("client-a", descendantSession.SessionId, "left", descendantSession.Left.Revision, new FileManagerSelection(false, [selected.Id], []), "copy", "right", null, descendantSession.Right.Revision);
        Assert.False(descendantResult.Succeeded);
        Assert.Equal("invalid-destination", descendantResult.Code);
        Assert.True(Directory.Exists(selectedDirectory));
    }

    [Fact]
    public async Task ReplacePreservesTheExistingDestinationWhenTheSourceCannotBeRead()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var sourcePath = Path.Combine(left, "same.txt");
        File.WriteAllText(sourcePath, "new");
        File.WriteAllText(Path.Combine(right, "same.txt"), "old");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        using var sourceLock = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);
        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "needs-attention");
        Assert.True(service.ResolveConflict("client-a", created.Job.JobId, "replace", applyToAll: false));
        await WaitForJobAsync(service, "client-a", created.Job.JobId, "failed");

        Assert.Equal("old", File.ReadAllText(Path.Combine(right, "same.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(right, "*.voltura-air-*.part"));
    }

    [Fact]
    public async Task PasteRejectsAStaleDestinationPanelBeforeReadingTheClipboard()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var clipboardSource = Path.Combine(right, "source.txt");
        File.WriteAllText(clipboardSource, "content");
        var platform = new FakePlatform();
        platform.SetFileClipboard([clipboardSource], move: false);
        await using var service = CreateService(left, right, out _, platform);
        var session = service.OpenSession("client-a");
        File.WriteAllText(Path.Combine(left, "arrived.txt"), "new");

        var result = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [], []), "paste", null, null, null);

        Assert.False(result.Succeeded);
        Assert.Equal("stale-panel", result.Code);
        Assert.Empty(service.GetJobs("client-a"));
    }

    [Fact]
    public async Task CancelingAQueuedJobRemovesItFromTheQueueImmediately()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "first.txt"), "new");
        File.WriteAllText(Path.Combine(left, "second.txt"), "second");
        File.WriteAllText(Path.Combine(right, "first.txt"), "old");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var first = Assert.Single(session.Left.Entries, entry => entry.Name == "first.txt");
        var second = Assert.Single(session.Left.Entries, entry => entry.Name == "second.txt");
        var blocking = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [first.Id], []), "copy", "right", null, session.Right.Revision);
        await WaitForJobAsync(service, "client-a", blocking.Job!.JobId, "needs-attention");
        var queued = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [second.Id], []), "copy", "right", null, session.Right.Revision);

        Assert.Equal("queued", service.GetJobs("client-a").Single(job => job.JobId == queued.Job!.JobId).State);
        Assert.True(service.ControlJob("client-a", queued.Job!.JobId, "cancel"));
        Assert.Equal("canceled", service.GetJobs("client-a").Single(job => job.JobId == queued.Job.JobId).State);
        Assert.True(service.ResolveConflict("client-a", blocking.Job.JobId, "skip", applyToAll: false));
        await WaitForJobAsync(service, "client-a", blocking.Job.JobId, "completed");
    }

    [Fact]
    public async Task NewestTerminalJobsRemainVisibleAndHistoryIsBounded()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var completedIds = new List<string>();

        for (var index = 0; index < 33; index++)
        {
            var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "delete", null, null, null);
            completedIds.Add(created.Job!.JobId);
            await WaitForJobAsync(service, "client-a", created.Job.JobId, "completed");
        }

        var retained = service.GetJobs("client-a");
        Assert.Equal(32, retained.Length);
        Assert.Contains(retained, job => job.JobId == completedIds[^1]);
        Assert.DoesNotContain(retained, job => job.JobId == completedIds[0]);
    }

    [Fact]
    public async Task ActiveQueueNeverExceedsTheInspectableJobWindow()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "new");
        File.WriteAllText(Path.Combine(right, "file.txt"), "old");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var selection = new FileManagerSelection(false, [source.Id], []);
        var blocking = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, selection, "copy", "right", null, session.Right.Revision);
        await WaitForJobAsync(service, "client-a", blocking.Job!.JobId, "needs-attention");

        for (var index = 1; index < 32; index++)
        {
            Assert.True(service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, selection, "copy", "right", null, session.Right.Revision).Succeeded);
        }
        var rejected = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, selection, "copy", "right", null, session.Right.Revision);

        Assert.False(rejected.Succeeded);
        Assert.Equal("queue-full", rejected.Code);
        Assert.Equal(32, service.GetJobs("client-a").Length);
    }

    [Fact]
    public async Task ReorderingCannotCrossAnotherDeviceAndRevocationRemovesOwnedQueuedWork()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "new");
        File.WriteAllText(Path.Combine(right, "file.txt"), "old");
        await using var service = CreateService(left, right, out _);
        var sessionA = service.OpenSession("client-a");
        var sessionB = service.OpenSession("client-b");
        var sourceA = Assert.Single(sessionA.Left.Entries);
        var sourceB = Assert.Single(sessionB.Left.Entries);
        var selectionA = new FileManagerSelection(false, [sourceA.Id], []);
        var selectionB = new FileManagerSelection(false, [sourceB.Id], []);
        var blocking = service.CreateJob("client-a", sessionA.SessionId, "left", sessionA.Left.Revision, selectionA, "copy", "right", null, sessionA.Right.Revision);
        await WaitForJobAsync(service, "client-a", blocking.Job!.JobId, "needs-attention");
        var firstA = service.CreateJob("client-a", sessionA.SessionId, "left", sessionA.Left.Revision, selectionA, "copy", "right", null, sessionA.Right.Revision).Job!;
        var onlyB = service.CreateJob("client-b", sessionB.SessionId, "left", sessionB.Left.Revision, selectionB, "copy", "right", null, sessionB.Right.Revision).Job!;
        var secondA = service.CreateJob("client-a", sessionA.SessionId, "left", sessionA.Left.Revision, selectionA, "copy", "right", null, sessionA.Right.Revision).Job!;
        var thirdA = service.CreateJob("client-a", sessionA.SessionId, "left", sessionA.Left.Revision, selectionA, "copy", "right", null, sessionA.Right.Revision).Job!;

        Assert.False(service.ReorderJob("client-a", firstA.JobId, "down"));
        Assert.False(service.ReorderJob("client-a", secondA.JobId, "up"));
        Assert.True(service.ReorderJob("client-a", thirdA.JobId, "up"));

        service.RevokeClient("client-a", closeSession: true);
        await WaitForJobAsync(service, "client-a", blocking.Job.JobId, "canceled");
        Assert.DoesNotContain("client-a", service.SessionClientIds);
        Assert.All(service.GetJobs("client-a"), job => Assert.Equal("canceled", job.State));
        Assert.NotEqual("canceled", Assert.Single(service.GetJobs("client-b"), job => job.JobId == onlyB.JobId).State);
    }

    [Fact]
    public async Task ReplaceAbortsBeforeMutationWhenTheRecoveryJournalCannotBeSaved()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "same.txt"), "new");
        File.WriteAllText(Path.Combine(right, "same.txt"), "old");
        var journal = new MemoryJobJournal { SaveSucceeds = false };
        await using var service = new FileManagerService(new FakePlatform(), left, right, new MemoryLocationStore(), journal);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);
        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "needs-attention");

        Assert.True(service.ResolveConflict("client-a", created.Job.JobId, "replace", applyToAll: false));
        await WaitForJobAsync(service, "client-a", created.Job.JobId, "failed");

        Assert.Equal("old", File.ReadAllText(Path.Combine(right, "same.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(left, "same.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(right, "*.backup"));
    }

    [Fact]
    public async Task FailedRecoveryRemainsJournaledAndRetriesOnTheNextStart()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var destination = Directory.CreateDirectory(Path.Combine(right, "destination")).FullName;
        var backup = Directory.CreateDirectory(Path.Combine(right, "destination.voltura-air-backup")).FullName;
        var lockedPath = Path.Combine(backup, "locked.txt");
        File.WriteAllText(lockedPath, "original");
        var journal = new MemoryJobJournal
        {
            Entries = [new FileJobJournalEntry("job-a", "client-a", "copy", [], [new FileJobBackupEntry(destination, backup)])]
        };

        await using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await using var firstStart = new FileManagerService(new FakePlatform(), left, right, new MemoryLocationStore(), journal);
            Assert.Single(journal.Entries);
            Assert.True(Directory.Exists(backup));
        }

        await using var secondStart = new FileManagerService(new FakePlatform(), left, right, new MemoryLocationStore(), journal);
        Assert.Empty(journal.Entries);
        Assert.False(Directory.Exists(backup));
        Assert.True(Directory.Exists(destination));
    }

    [Fact]
    public async Task CopyDoesNotCreateAPartialFileWhenItsJournalCannotBeSaved()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        var journal = new MemoryJobJournal { SaveSucceeds = false };
        await using var service = new FileManagerService(new FakePlatform(), left, right, new MemoryLocationStore(), journal);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);

        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "failed");

        Assert.Empty(Directory.EnumerateFileSystemEntries(right));
        Assert.Equal("content", File.ReadAllText(Path.Combine(left, "file.txt")));
    }

    [Fact]
    public async Task CaseOnlyRenameUsesARecoverableTemporarySibling()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        await using var service = CreateService(left, right, out _);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob("client-a", session.SessionId, "left", session.Left.Revision, new FileManagerSelection(false, [source.Id], []), "rename", null, "FILE.txt", null);

        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "completed");

        Assert.Equal("FILE.txt", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(left))));
        Assert.Equal("content", File.ReadAllText(Path.Combine(left, "FILE.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(left, "*.rename"));
    }

    [Fact]
    public async Task FailedCaseOnlyRenameRollbackKeepsRecoveryAfterHistoryDismissal()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        var sourcePath = Path.Combine(left, "file.txt");
        File.WriteAllText(sourcePath, "content");
        var journal = new MemoryJobJournal();
        var moves = 0;
        void MoveWithFailedCommitAndRollback(string source, string destination, bool directory)
        {
            moves++;
            if (moves > 1) throw new IOException("Injected move failure.");
            if (directory) Directory.Move(source, destination); else File.Move(source, destination);
        }
        await using var service = new FileManagerService(
            new FakePlatform(), left, right, new MemoryLocationStore(), journal,
            movePath: MoveWithFailedCommitAndRollback);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob(
            "client-a", session.SessionId, "left", session.Left.Revision,
            new FileManagerSelection(false, [source.Id], []), "rename", null, "FILE.txt", null);

        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "failed");

        var recovery = Assert.Single(journal.Entries);
        var backup = Assert.Single(recovery.Backups!);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(backup.BackupPath));
        Assert.True(service.ControlJob("client-a", created.Job.JobId, "dismiss"));
        Assert.DoesNotContain(service.GetJobs("client-a"), job => job.JobId == created.Job.JobId);
        Assert.Contains(journal.Entries, entry => entry.JobId == created.Job.JobId && entry.Backups!.Length == 1);
    }

    [Fact]
    public async Task FailedPartialCleanupRemainsJournaledForRestartRecovery()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        var journal = new MemoryJobJournal();
        await using var service = new FileManagerService(
            new FakePlatform(), left, right, new MemoryLocationStore(), journal,
            movePath: (_, _, _) => throw new IOException("Injected commit failure."),
            deleteTemporary: _ => false);
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob(
            "client-a", session.SessionId, "left", session.Left.Revision,
            new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);

        await WaitForJobAsync(service, "client-a", created.Job!.JobId, "failed");

        var recovery = Assert.Single(journal.Entries);
        var partial = Assert.Single(recovery.TemporaryPaths);
        Assert.True(File.Exists(partial));
        Assert.False(File.Exists(Path.Combine(right, "file.txt")));
    }

    [Fact]
    public async Task WindowsAccessDeniedIoFailureReportsPermissionClearly()
    {
        var left = CreateDirectory("left");
        var right = CreateDirectory("right");
        File.WriteAllText(Path.Combine(left, "file.txt"), "content");
        await using var service = new FileManagerService(
            new FakePlatform(), left, right, new MemoryLocationStore(), new MemoryJobJournal(),
            movePath: (_, _, _) => throw new IOException("Access denied.", unchecked((int)0x80070005)));
        var session = service.OpenSession("client-a");
        var source = Assert.Single(session.Left.Entries);
        var created = service.CreateJob(
            "client-a", session.SessionId, "left", session.Left.Revision,
            new FileManagerSelection(false, [source.Id], []), "copy", "right", null, session.Right.Revision);

        var failed = await WaitForJobAsync(service, "client-a", created.Job!.JobId, "failed");

        Assert.Equal(
            "Windows denied access. Your PC account does not have permission to change an item or destination.",
            failed.Message);
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
        MemoryLocationStore? locations = null,
        Func<string, bool>? hideProtectedItems = null)
    {
        journal = new MemoryJobJournal();
        return new FileManagerService(platform ?? new FakePlatform(), left, right, locations ?? new MemoryLocationStore(), journal, hideProtectedItems);
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

    private sealed class BlockingLocationStore : IFileManagerLocationStore, IDisposable
    {
        public ManualResetEventSlim LoadStarted { get; } = new();
        public ManualResetEventSlim ReleaseLoad { get; } = new();

        public string? Load(string clientId, string panel)
        {
            LoadStarted.Set();
            ReleaseLoad.Wait();
            return null;
        }

        public void Save(string clientId, string panel, string path)
        {
        }

        public void Dispose()
        {
            LoadStarted.Dispose();
            ReleaseLoad.Dispose();
        }
    }

    private sealed class MemoryJobJournal : IFileJobJournal
    {
        public FileJobJournalEntry[] Entries { get; set; } = [];
        public bool SaveSucceeds { get; set; } = true;
        public FileJobJournalEntry[] Load() => Entries;
        public bool Save(FileJobJournalEntry[] entries)
        {
            if (!SaveSucceeds) return false;
            Entries = entries;
            return true;
        }
    }
}
