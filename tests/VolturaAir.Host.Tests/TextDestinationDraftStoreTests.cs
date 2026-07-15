namespace VolturaAir.Host.Tests;

public sealed class TextDestinationDraftStoreTests
{
    [Fact]
    public async Task CleanupDisposalWaitsForActiveCleanup()
    {
        using var cleanupStarted = new ManualResetEventSlim();
        using var releaseCleanup = new ManualResetEventSlim();
        var cleanup = new TextDestinationDraftCleanup(
            NullAppLog.Instance,
            () =>
            {
                cleanupStarted.Set();
                releaseCleanup.Wait();
            },
            TimeSpan.Zero,
            Timeout.InfiniteTimeSpan);

        Task? disposal = null;
        try
        {
            Assert.True(cleanupStarted.Wait(TimeSpan.FromSeconds(3)));
            disposal = cleanup.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            releaseCleanup.Set();
            if (disposal is not null)
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(3));
            }
            else
            {
                await cleanup.DisposeAsync();
            }
        }
    }

    [Fact]
    public void AutomaticRemovalNoticeNamesTheRetentionSettingAndDate()
    {
        var draft = new TextDestinationDraft(@"C:\\Drafts\\Untitled-test.txt", new DateTime(2026, 7, 16), AutomaticallyRemove: true);

        var notice = TextDestinationDraftStore.GetNoticeLines(draft);

        Assert.Contains("Scheduled for removal on 2026-07-16.", notice);
        Assert.Contains("Control automatic removal in Preferences > Text destination > Keep generated draft files.", notice);
    }

    [Fact]
    public void RetainedDraftNoticeDoesNotPromiseAutomaticRemoval()
    {
        var draft = new TextDestinationDraft(@"C:\\Drafts\\Untitled-test.txt", new DateTime(2026, 7, 16), AutomaticallyRemove: false);

        var notice = TextDestinationDraftStore.GetNoticeLines(draft);

        Assert.Contains("This generated draft is being kept until you remove it.", notice);
        Assert.DoesNotContain(notice, line => line.Contains("Scheduled for removal", StringComparison.Ordinal));
    }

    [Fact]
    public void CleanupDeletesOnlyExpiredVolturaDraftsThatAreNotInUse()
    {
        var directory = Directory.CreateTempSubdirectory("VolturaAir-DraftStore-");
        try
        {
            var oldDraft = Path.Combine(directory.FullName, "Untitled-old.txt");
            var lockedDraft = Path.Combine(directory.FullName, "Untitled-locked.docx");
            var recentDraft = Path.Combine(directory.FullName, "Untitled-recent.xlsx");
            var unrelatedFile = Path.Combine(directory.FullName, "notes.txt");
            File.WriteAllText(oldDraft, "old");
            File.WriteAllText(lockedDraft, "locked");
            File.WriteAllText(recentDraft, "recent");
            File.WriteAllText(unrelatedFile, "keep");
            var old = DateTime.UtcNow - TimeSpan.FromDays(31);
            File.SetCreationTimeUtc(oldDraft, old);
            File.SetLastWriteTimeUtc(oldDraft, old);
            File.SetCreationTimeUtc(lockedDraft, old);
            File.SetLastWriteTimeUtc(lockedDraft, old);

            using (File.Open(lockedDraft, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                TextDestinationDraftStore.DeleteExpired(directory.FullName, DateTime.UtcNow - TimeSpan.FromDays(30));
            }

            Assert.False(File.Exists(oldDraft));
            Assert.True(File.Exists(lockedDraft));
            Assert.True(File.Exists(recentDraft));
            Assert.True(File.Exists(unrelatedFile));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
