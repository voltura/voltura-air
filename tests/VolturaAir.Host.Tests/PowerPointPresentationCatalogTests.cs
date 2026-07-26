namespace VolturaAir.Host.Tests;

public sealed class PowerPointPresentationCatalogTests
{
    [Fact]
    public async Task CatalogKeepsOneExistingPowerPointFileAndOmitsItWhenOpen()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PresentationCatalog-");
        var presentationPath = Path.Combine(directory.Path, "Quarterly update.pptx");
        await File.WriteAllTextAsync(presentationPath, "test");
        var store = new InMemoryPresentationReportStore();
        await SaveAsync(store, "report-old", "Older title", presentationPath, DateTimeOffset.UtcNow.AddDays(-2));
        await SaveAsync(store, "report-new", "Latest title", presentationPath.ToUpperInvariant(), DateTimeOffset.UtcNow);
        await SaveAsync(store, "pdf-report", "PDF", presentationPath, DateTimeOffset.UtcNow, "pdf");
        await SaveAsync(store, "missing", "Missing", Path.Combine(directory.Path, "missing.pptx"), DateTimeOffset.UtcNow);

        using var catalog = new PowerPointPresentationCatalog(store);

        var candidate = Assert.Single(catalog.GetAvailable(PowerPointAutomationSnapshot.Unavailable));
        Assert.Equal("report-new", candidate.PresentationId);
        Assert.Equal("Latest title", candidate.Title);
        Assert.Equal("Quarterly update.pptx", candidate.FileName, ignoreCase: true);

        var open = new PowerPointAutomationSnapshot(
            PowerPointDiscoveryState.Ready,
            [new("runtime-1", "Quarterly update.pptx", false, 20, null, null, "ready", presentationPath)]);
        Assert.Empty(catalog.GetAvailable(open));
    }

    [Fact]
    public async Task ResolveRevalidatesFilesAndReportChanges()
    {
        using var directory = new TemporaryDirectory("VolturaAir-PresentationCatalogRefresh-");
        var presentationPath = Path.Combine(directory.Path, "Training.pptx");
        await File.WriteAllTextAsync(presentationPath, "test");
        var store = new InMemoryPresentationReportStore();
        using var catalog = new PowerPointPresentationCatalog(store);
        Assert.Empty(catalog.GetAvailable(PowerPointAutomationSnapshot.Unavailable));

        await SaveAsync(store, "report-1", "Training", presentationPath, DateTimeOffset.UtcNow);
        Assert.NotNull(catalog.Resolve("report-1"));

        File.Delete(presentationPath);
        Assert.Null(catalog.Resolve("report-1"));
    }

    private static Task<PresentationReportSaveResult> SaveAsync(
        InMemoryPresentationReportStore store,
        string reportId,
        string title,
        string path,
        DateTimeOffset endedAt,
        string target = "powerpoint")
    {
        var startedAt = endedAt.AddMinutes(-10);
        return store.SaveAsync(
            new(
                $"operation-{reportId}",
                reportId,
                target,
                startedAt,
                endedAt,
                0,
                600,
                600,
                false,
                [],
                [],
                SuggestedTitle: title,
                PresentationFilePath: path),
            "client-1",
            "Phone",
            CancellationToken.None);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"{prefix}{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
