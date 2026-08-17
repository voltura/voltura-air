using System.Text;
using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PresentationReportProtocolTests
{
    [Fact]
    public void ParsesCompleteBoundedReport()
    {
        using var document = CreateProtocolReport();

        Assert.True(PresentationReportProtocol.TryParse(document.RootElement, out var request));
        Assert.Equal("powerpoint", request.Target);
        Assert.Single(request.Breaks);
        Assert.Equal(2, request.Slides.Count);
        Assert.Equal(9, request.Breaks[0].SessionSlideMaximum);
        Assert.False(request.EndedDuringBreak);
    }

    [Fact]
    public void RejectsReportWhoseWallClockSpanExceedsSevenDays()
    {
        var startedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        using var document = CreateProtocolReport(
            startedAt: startedAt,
            endedAt: startedAt.AddDays(7).AddSeconds(1),
            breaks: []);

        Assert.False(PresentationReportProtocol.TryParse(document.RootElement, out _));
    }

    [Fact]
    public void RejectsExplicitNullDuplicateAndUndeclaredNestedFields()
    {
        var startedAt = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
        using var nullDocument = CreateProtocolReport(
            startedAt: startedAt,
            endedAt: startedAt.AddMinutes(3),
            breaks:
            [
                new
                {
                    breakNumber = 1,
                    presentationElapsedSeconds = 60,
                    breakDurationSeconds = 60,
                    startedAt = startedAt.AddMinutes(1),
                    endedAt = startedAt.AddMinutes(2),
                    sessionSlideMinimum = (int?)null
                }
            ]);
        Assert.False(PresentationReportProtocol.TryParse(nullDocument.RootElement, out _));

        using var validDocument = CreateProtocolReport();
        var duplicateSlideProperty = validDocument.RootElement.GetRawText().Replace(
            "\"durationSeconds\":50",
            "\"durationSeconds\":50,\"durationSeconds\":51",
            StringComparison.Ordinal);
        using var duplicateDocument = JsonDocument.Parse(duplicateSlideProperty);
        Assert.False(PresentationReportProtocol.TryParse(duplicateDocument.RootElement, out _));

        var undeclaredBreakProperty = validDocument.RootElement.GetRawText().Replace(
            "\"breakNumber\":1",
            "\"breakNumber\":1,\"unexpected\":true",
            StringComparison.Ordinal);
        using var undeclaredDocument = JsonDocument.Parse(undeclaredBreakProperty);
        Assert.False(PresentationReportProtocol.TryParse(undeclaredDocument.RootElement, out _));
    }

    [Fact]
    public void ParsesReportThatEndsDuringItsFinalBreak()
    {
        var startedAt = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
        using var document = CreateProtocolReport(
            startedAt: startedAt,
            endedAt: startedAt.AddMinutes(2),
            presentationDurationSeconds: 60,
            endedDuringBreak: true,
            breaks:
            [
                new
                {
                    breakNumber = 1,
                    presentationElapsedSeconds = 60,
                    breakDurationSeconds = 60,
                    startedAt = startedAt.AddMinutes(1),
                    endedAt = startedAt.AddMinutes(2)
                }
            ]);

        Assert.True(PresentationReportProtocol.TryParse(document.RootElement, out var request));
        Assert.True(request.EndedDuringBreak);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(1, 121)]
    public void RejectsNonConsecutiveBreaksOrCheckpointsBeyondPresentingTime(
        int breakNumber,
        double presentationElapsedSeconds)
    {
        var startedAt = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
        using var document = CreateProtocolReport(
            startedAt: startedAt,
            endedAt: startedAt.AddMinutes(3),
            presentationDurationSeconds: 120,
            breaks:
            [
                new
                {
                    breakNumber,
                    presentationElapsedSeconds,
                    breakDurationSeconds = 60,
                    startedAt = startedAt.AddMinutes(2),
                    endedAt = startedAt.AddMinutes(3),
                    sessionSlideMinimum = 1,
                    sessionSlideMaximum = 2,
                    slideNumberAtStart = 2,
                    slideNumberAtEnd = 2
                }
            ]);

        Assert.False(PresentationReportProtocol.TryParse(document.RootElement, out _));
    }

    [Fact]
    public async Task PersistentStoreSavesAtomicallyAndRecoversAroundCorruptFiles()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);
        var request = CreateRequest("operation-1", "report-1");

        var result = await store.SaveAsync(request, "device-a", "Joakim's phone", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));

        var corruptDirectory = System.IO.Path.Combine(directory.Path, "corrupt");
        Directory.CreateDirectory(corruptDirectory);
        File.WriteAllText(
            System.IO.Path.Combine(corruptDirectory, "broken.json"),
            "{ not valid json",
            Encoding.UTF8);
        var savedPath = Assert.Single(
            Directory.EnumerateFiles(directory.Path, "*.json", SearchOption.AllDirectories),
            path => !path.StartsWith(corruptDirectory, StringComparison.OrdinalIgnoreCase));
        var nullTimeline = File.ReadAllText(savedPath, Encoding.UTF8)
            .Replace("\"reportId\": \"report-1\"", "\"reportId\": \"report-null-timeline\"", StringComparison.Ordinal)
            .Replace("\"breaks\": [", "\"breaks\": null, \"discardedBreaks\": [", StringComparison.Ordinal);
        File.WriteAllText(
            System.IO.Path.Combine(corruptDirectory, "null-timeline.json"),
            nullTimeline,
            Encoding.UTF8);

        var read = store.ReadAll();
        Assert.True(read.Succeeded);
        var report = Assert.Single(read.Reports);
        Assert.Equal("Joakim's phone", report.DeviceName);
        Assert.Equal("Presentation", report.Title);
    }

    [Fact]
    public async Task PersistentStoreRejectsOversizedReportBeforeDeserialization()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);
        var result = await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None);
        var oversizedDirectory = System.IO.Path.Combine(directory.Path, "oversized");
        Directory.CreateDirectory(oversizedDirectory);
        using (var stream = new FileStream(
            System.IO.Path.Combine(oversizedDirectory, "oversized.json"),
            FileMode.CreateNew,
            FileAccess.Write))
        {
            stream.SetLength(PresentationReportStore.MaxStoredReportBytes + 1L);
        }

        var read = store.ReadAll();

        Assert.True(result.Succeeded);
        Assert.True(read.Succeeded);
        Assert.Single(read.Reports);
        Assert.Equal("report-1", read.Reports[0].ReportId);
    }

    [Fact]
    public void DeleteAllIgnoresForeignFilesBecauseTheManifestIsTheSoleInventory()
    {
        using var directory = new TemporaryReportDirectory();
        var foreignDirectory = System.IO.Path.Combine(directory.Path, "foreign");
        Directory.CreateDirectory(foreignDirectory);
        for (var index = 0; index <= PresentationReportStore.MaximumScannedFiles; index++)
        {
            File.WriteAllText(
                System.IO.Path.Combine(foreignDirectory, $"foreign-{index:D5}.json"),
                "{}",
                Encoding.UTF8);
        }
        var store = new PresentationReportStore(directory.Path);

        PresentationReportMutationResult result = store.DeleteAll();

        Assert.True(result.Succeeded);
        Assert.Equal(
            PresentationReportStore.MaximumScannedFiles + 1,
            Directory.EnumerateFiles(foreignDirectory, "*.json", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task CorruptManifestMakesTheArchiveUnavailableWithoutDeletingArtifacts()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);
        var manifestPath = Assert.Single(Directory.EnumerateFiles(directory.Path, "manifest.json"));
        var artifactPath = Assert.Single(Directory.EnumerateFiles(directory.Path, "*.report"));
        File.WriteAllText(manifestPath, "{ corrupt", Encoding.UTF8);

        PresentationReportMutationResult result = store.DeleteAll();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(artifactPath));
    }

    [Fact]
    public void ManifestWithMissingRequiredFieldsMakesTheArchiveUnavailable()
    {
        using var directory = new TemporaryReportDirectory();
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "manifest.json"), "{}", Encoding.UTF8);
        var store = new PresentationReportStore(directory.Path);

        PresentationReportReadResult result = store.ReadAll();

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void DeleteAllDoesNotTraverseAnOwnedLookingDirectoryLink()
    {
        using var directory = new TemporaryReportDirectory();
        using var external = new TemporaryReportDirectory();
        var externalReport = System.IO.Path.Combine(external.Path, "outside.json");
        File.WriteAllText(externalReport, "{ corrupt", Encoding.UTF8);
        var linkedDeviceDirectory = System.IO.Path.Combine(directory.Path, new string('a', 32));
        try
        {
            Directory.CreateSymbolicLink(linkedDeviceDirectory, external.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }
        var store = new PresentationReportStore(directory.Path);

        PresentationReportMutationResult result = store.DeleteAll();

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(externalReport));
    }

    [Fact]
    public void DeleteAllRejectsALinkedArchiveRootWithoutDeletingItsTarget()
    {
        using var directory = new TemporaryReportDirectory();
        using var external = new TemporaryReportDirectory();
        var externalDeviceDirectory = System.IO.Path.Combine(external.Path, new string('a', 32));
        Directory.CreateDirectory(externalDeviceDirectory);
        var externalReport = System.IO.Path.Combine(externalDeviceDirectory, "outside.json");
        File.WriteAllText(externalReport, "{ corrupt", Encoding.UTF8);
        var linkedRoot = System.IO.Path.Combine(directory.Path, "linked-archive");
        try
        {
            Directory.CreateSymbolicLink(linkedRoot, external.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }
        var store = new PresentationReportStore(linkedRoot);

        PresentationReportMutationResult result = store.DeleteAll();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(externalReport));
    }

    [Fact]
    public async Task DeleteAllNeverDeletesAHashMismatchedArtifact()
    {
        using var directory = new TemporaryReportDirectory();
        string? artifactPath = null;
        var store = new PresentationReportStore(
            directory.Path,
            beforeDeleteArtifact: path =>
            {
                artifactPath = path;
                File.WriteAllText(path, "substituted", Encoding.UTF8);
            });
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);

        PresentationReportMutationResult result = store.DeleteAll();

        Assert.True(result.Succeeded);
        Assert.NotNull(artifactPath);
        Assert.Equal("substituted", File.ReadAllText(artifactPath, Encoding.UTF8));
        Assert.Empty(store.ReadAll().Reports);
    }

    [Fact]
    public async Task DeletePreservesForeignFilesBesideOwnedArtifacts()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);

        var foreignPath = System.IO.Path.Combine(directory.Path, "foreign.bin");
        File.WriteAllText(foreignPath, "foreign", Encoding.UTF8);

        PresentationReportMutationResult result = store.Delete("report-1");

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(foreignPath));
    }

    [Fact]
    public async Task RenameReconcilesAReplacementThatSucceedsBeforeReportingFailure()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(
            directory.Path,
            reportReplaceFailureAfterSuccess: true);
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);

        PresentationReportMutationResult result = store.Rename("report-1", "Committed title");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Committed title", Assert.Single(store.ReadAll().Reports).Title);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));

        PresentationReportMutationResult deleted = store.DeleteAll();

        Assert.True(deleted.Succeeded);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.report", SearchOption.AllDirectories));
        Assert.Empty(store.ReadAll().Reports);
    }

    [Fact]
    public async Task RenameDoesNotReplaceAnArtifactThatChangedAfterValidation()
    {
        using var directory = new TemporaryReportDirectory();
        string? movedArtifact = null;
        string? replacementArtifact = null;
        var store = new PresentationReportStore(
            directory.Path,
            beforeReplaceArtifact: path =>
            {
                movedArtifact = $"{path}.moved";
                replacementArtifact = path;
                File.Move(path, movedArtifact);
                File.WriteAllText(path, "replacement", Encoding.UTF8);
            });
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);

        PresentationReportMutationResult result = store.Rename("report-1", "Must not be written");

        Assert.False(result.Succeeded);
        Assert.NotNull(movedArtifact);
        Assert.True(File.Exists(movedArtifact));
        Assert.NotNull(replacementArtifact);
        Assert.Equal("replacement", File.ReadAllText(replacementArtifact, Encoding.UTF8));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RenamePreservesAnEntryCreatedAfterTheValidatedTargetIsStaged()
    {
        using var directory = new TemporaryReportDirectory();
        string? finalPath = null;
        var store = new PresentationReportStore(
            directory.Path,
            beforeReplaceCommit: path =>
            {
                finalPath = path;
                File.WriteAllText(path, "external replacement", Encoding.UTF8);
            });
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);

        PresentationReportMutationResult result = store.Rename("report-1", "Must not be written");

        Assert.False(result.Succeeded);
        Assert.NotNull(finalPath);
        Assert.Equal("external replacement", File.ReadAllText(finalPath, Encoding.UTF8));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".manifest.backup"));
        Assert.Single(Directory.EnumerateFiles(directory.Path, ".manifest.staged"));
        Assert.Single(Directory.EnumerateFiles(directory.Path, "transaction.json"));
        Assert.False(store.ReadAll().Succeeded);
    }

    [Fact]
    public async Task RenameFailureDeletesOnlyItsConfirmedOpenTemporaryArtifact()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(
            directory.Path,
            reportReplaceFailureBeforeSuccess: true);
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);

        PresentationReportMutationResult result = store.Rename("report-1", "Uncommitted title");

        Assert.False(result.Succeeded);
        Assert.NotEqual("Uncommitted title", Assert.Single(store.ReadAll().Reports).Title);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".manifest.staged"));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "transaction.json"));
    }

    [Fact]
    public async Task PersistentStoreRejectsUnsafeStoredSlideVisits()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);
        Assert.True((await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None)).Succeeded);
        var savedPath = Assert.Single(
            Directory.EnumerateFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        var invalidPath = System.IO.Path.Combine(directory.Path, "invalid-visits.json");
        var invalid = File.ReadAllText(savedPath, Encoding.UTF8)
            .Replace("\"reportId\": \"report-1\"", "\"reportId\": \"report-invalid\"", StringComparison.Ordinal)
            .Replace("\"operationId\": \"operation-1\"", "\"operationId\": \"operation-invalid\"", StringComparison.Ordinal)
            .Replace(
                "\"slideVisits\": null",
                """
                "slideVisits": [
                  {
                    "slideNumber": 1,
                    "enteredAt": "2026-07-23T08:00:00+02:00",
                    "durationSeconds": -1,
                    "origin": "untrusted"
                  }
                ]
                """,
                StringComparison.Ordinal);
        File.WriteAllText(invalidPath, invalid, Encoding.UTF8);

        var reports = store.ReadAll().Reports;

        var report = Assert.Single(reports);
        Assert.Equal("report-1", report.ReportId);
    }

    [Fact]
    public async Task PersistentStoreIsIdempotentAndRejectsIdentifierConflicts()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);
        var request = CreateRequest("operation-1", "report-1");

        var first = await store.SaveAsync(request, "device-a", "Device A", CancellationToken.None);
        var retry = await store.SaveAsync(request, "device-a", "Renamed device", CancellationToken.None);
        var conflict = await store.SaveAsync(
            CreateRequest("operation-2", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Equal("Presentation data was already saved.", retry.Message);
        Assert.False(conflict.Succeeded);
        Assert.Equal("report-conflict", conflict.Code);
        var report = Assert.Single(store.ReadAll().Reports);
        Assert.Equal("Device A", report.DeviceName);
    }

    [Fact]
    public async Task DefaultNamesIncrementPerCapturedDevice()
    {
        using var directory = new TemporaryReportDirectory();
        var store = new PresentationReportStore(directory.Path);

        await store.SaveAsync(CreateRequest("operation-1", "report-1"), "device-a", "Device A", CancellationToken.None);
        await store.SaveAsync(CreateRequest("operation-2", "report-2"), "device-a", "Device A", CancellationToken.None);
        await store.SaveAsync(CreateRequest("operation-3", "report-3"), "device-b", "Device B", CancellationToken.None);

        var reports = store.ReadAll().Reports;
        Assert.Contains(reports, report => report.DeviceName == "Device A" && report.Title == "Presentation");
        Assert.Contains(reports, report => report.DeviceName == "Device A" && report.Title == "Presentation (1)");
        Assert.Contains(reports, report => report.DeviceName == "Device B" && report.Title == "Presentation");
    }

    [Fact]
    public async Task SaveReturnsStorageFailureWhenArchivePathIsAFile()
    {
        using var directory = new TemporaryReportDirectory();
        var filePath = System.IO.Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(filePath, "occupied", Encoding.UTF8);
        var store = new PresentationReportStore(filePath);

        var result = await store.SaveAsync(
            CreateRequest("operation-1", "report-1"),
            "device-a",
            "Device A",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("storage-failed", result.Code);
    }

    [Fact]
    public async Task InMemoryStoreRejectsNewSaveAtArchiveCapacity()
    {
        var store = new InMemoryPresentationReportStore();
        var startedAt = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < PresentationReportStore.MaxReportCount; index++)
        {
            store.Add(new(
                $"report-{index}",
                $"operation-{index}",
                $"Presentation {index}",
                "powerpoint",
                "device-key",
                "Device",
                startedAt,
                startedAt.AddMinutes(1),
                0,
                60,
                60,
                false,
                [],
                [],
                null,
                null));
        }

        var result = await store.SaveAsync(
            CreateRequest("operation-new", "report-new"),
            "device-a",
            "Device A",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("archive-full", result.Code);
        Assert.Equal(PresentationReportStore.MaxReportCount, store.ReadAll().Reports.Count);
    }

    private static JsonDocument CreateProtocolReport(
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        double presentationDurationSeconds = 120,
        bool endedDuringBreak = false,
        object[]? breaks = null)
    {
        var start = startedAt ?? new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(2));
        var end = endedAt ?? start.AddMinutes(3);
        breaks ??=
        [
            new
            {
                breakNumber = 1,
                presentationElapsedSeconds = 60,
                breakDurationSeconds = 60,
                startedAt = start.AddMinutes(1),
                endedAt = start.AddMinutes(2),
                sessionSlideMinimum = 1,
                sessionSlideMaximum = 9,
                slideNumberAtStart = 9,
                slideNumberAtEnd = 9
            }
        ];
        return JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "presentation.report.save",
            operationId = "operation-1",
            reportId = "report-1",
            target = "powerpoint",
            startedAt = start,
            endedAt = end,
            utcOffsetMinutes = (int)start.Offset.TotalMinutes,
            plannedDurationSeconds = 180,
            presentationDurationSeconds,
            endedDuringBreak,
            breaks,
            slides = new[]
            {
                new { slideNumber = 1, durationSeconds = (double?)50 },
                new { slideNumber = 2, durationSeconds = (double?)70 }
            }
        }));
    }

    private static PresentationReportSaveRequest CreateRequest(string operationId, string reportId)
    {
        var startedAt = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(2));
        return new(
            operationId,
            reportId,
            "powerpoint",
            startedAt,
            startedAt.AddMinutes(3),
            120,
            180,
            120,
            false,
            [
                new(
                    1,
                    60,
                    60,
                    startedAt.AddMinutes(1),
                    startedAt.AddMinutes(2),
                    1,
                    9,
                    9,
                    9)
            ],
            [new(1, 120)]);
    }

    private sealed class TemporaryReportDirectory : IDisposable
    {
        public TemporaryReportDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VolturaAir.Tests",
                $"presentation-reports-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
