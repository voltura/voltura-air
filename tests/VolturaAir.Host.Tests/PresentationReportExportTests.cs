using System.IO.Compression;
using System.Text;
using VolturaAir.Host;
using VolturaAir.Host.Features.Presentations;

namespace VolturaAir.Host.Tests;

public sealed class PresentationReportExportTests
{
    [Fact]
    public void TimelineRowsPreserveIntermittentSessionsAndBreaks()
    {
        var report = CreateReport("Presentation");

        var rows = PresentationReportExports.BuildTimelineRows(report).Reverse().ToList();

        Assert.Equal(["Session 1", "Break 1", "Session 2"], rows.Select(row => row.Label));
        Assert.Equal([60d, 30d, 60d], rows.Select(row => row.DurationSeconds));
        Assert.Equal("Slides 1-2", rows[0].Detail);
    }

    [Fact]
    public void HtmlAndEmailExportsEscapeUntrustedReportText()
    {
        var report = CreateReport("<script>alert(1)</script>");
        using var directory = new TemporaryExportDirectory();
        var htmlPath = System.IO.Path.Combine(directory.Path, "report.html");

        PresentationReportExports.Write(htmlPath, [report], PresentationExportFormat.Html);
        var html = File.ReadAllText(htmlPath, Encoding.UTF8);
        var emailHtml = PresentationReportExports.BuildEmailHtml([report]);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", emailHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", emailHtml, StringComparison.Ordinal);
        Assert.Contains("This email may contain sensitive information. Please handle it accordingly.", emailHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"session\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"break\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvAndExcelKeepFormulaLikeTitlesAsText()
    {
        var report = CreateReport("=HYPERLINK(\"https://example.test\")");
        using var directory = new TemporaryExportDirectory();
        var csvPath = System.IO.Path.Combine(directory.Path, "report.csv");
        var excelPath = System.IO.Path.Combine(directory.Path, "report.xlsx");

        PresentationReportExports.Write(csvPath, [report], PresentationExportFormat.Csv);
        PresentationReportExports.Write(excelPath, [report], PresentationExportFormat.Excel);

        var csv = File.ReadAllText(csvPath, Encoding.UTF8);
        Assert.Contains("\"'=HYPERLINK(\"\"https://example.test\"\")\"", csv, StringComparison.Ordinal);
        using var archive = ZipFile.OpenRead(excelPath);
        var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(worksheet);
        using var reader = new StreamReader(worksheet!.Open(), Encoding.UTF8);
        var worksheetXml = reader.ReadToEnd();
        Assert.Contains("t=\"inlineStr\"", worksheetXml, StringComparison.Ordinal);
        Assert.DoesNotContain("<f>", worksheetXml, StringComparison.Ordinal);
        Assert.Contains("=HYPERLINK", worksheetXml, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfAndTextExportsContainReportStructure()
    {
        var report = CreateReport("Physics 101");
        using var directory = new TemporaryExportDirectory();
        var pdfPath = System.IO.Path.Combine(directory.Path, "report.pdf");
        var textPath = System.IO.Path.Combine(directory.Path, "report.txt");

        PresentationReportExports.Write(pdfPath, [report], PresentationExportFormat.Pdf);
        PresentationReportExports.Write(textPath, [report], PresentationExportFormat.Text);

        var pdfHeader = new byte[8];
        using (var stream = File.OpenRead(pdfPath))
        {
            Assert.Equal(pdfHeader.Length, stream.Read(pdfHeader));
        }

        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdfHeader), StringComparison.Ordinal);
        var text = File.ReadAllText(textPath, Encoding.UTF8);
        Assert.Contains("Physics 101", text, StringComparison.Ordinal);
        Assert.Contains("Session 1", text, StringComparison.Ordinal);
        Assert.Contains("Break 1", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(PresentationExportFormat.Excel))]
    [InlineData(nameof(PresentationExportFormat.Pdf))]
    public void OfficeExportsReplaceAnExistingDestination(string formatName)
    {
        var format = Enum.Parse<PresentationExportFormat>(formatName);
        var report = CreateReport("Replacement export");
        using var directory = new TemporaryExportDirectory();
        var path = System.IO.Path.Combine(
            directory.Path,
            "existing" + PresentationReportExports.Extension(format));
        File.WriteAllText(path, "previous export", Encoding.UTF8);

        PresentationReportExports.Write(path, [report], format);

        var header = new byte[4];
        using var stream = File.OpenRead(path);
        Assert.Equal(header.Length, stream.Read(header));
        Assert.Equal(
            format == PresentationExportFormat.Excel ? "PK\u0003\u0004" : "%PDF",
            Encoding.ASCII.GetString(header));
    }

    [Fact]
    public void TerminalBreakDoesNotCreateAnEmptyTrailingSession()
    {
        var report = CreateReport("Terminal break") with { EndedDuringBreak = true };

        var rows = PresentationReportExports.BuildTimelineRows(report).Reverse().ToList();

        Assert.Equal(1, report.SessionCount);
        Assert.Equal(["Session 1", "Break 1"], rows.Select(row => row.Label));
    }

    [Fact]
    public void EmailDraftArtifactsAreAgedAndCapacityBounded()
    {
        using var directory = new TemporaryExportDirectory();
        var now = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
        var expired = System.IO.Path.Combine(directory.Path, "expired.eml");
        File.WriteAllText(expired, "expired", Encoding.UTF8);
        File.SetLastWriteTimeUtc(expired, now.AddDays(-8));
        for (var index = 0; index < 99; index++)
        {
            File.WriteAllText(System.IO.Path.Combine(directory.Path, $"{index}.eml"), "active", Encoding.UTF8);
        }

        PresentationReportSharing.PruneDraftArtifacts(directory.Path, now, requiredSlots: 1);

        Assert.False(File.Exists(expired));
        Assert.Throws<IOException>(() =>
            PresentationReportSharing.PruneDraftArtifacts(directory.Path, now, requiredSlots: 2));
    }

    [Fact]
    public async Task EmailDraftCleanupRunsImmediatelyAndIsOwned()
    {
        var cleanupRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cleanup = new PresentationEmailDraftCleanup(
            NullAppLog.Instance,
            () => cleanupRan.TrySetResult(),
            TimeSpan.Zero,
            TimeSpan.FromHours(1));

        await cleanupRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cleanupRan.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void PortableEmailDraftRejectsMissingRequestedAttachment()
    {
        using var directory = new TemporaryExportDirectory();
        var draftPath = System.IO.Path.Combine(directory.Path, "draft.eml");
        var missingPath = System.IO.Path.Combine(directory.Path, "missing.pptx");

        Assert.Throws<FileNotFoundException>(() =>
            PresentationReportSharing.WritePortableEmailDraft(
                draftPath,
                "Presentation",
                "<p>Statistics</p>",
                [missingPath]));
    }

    [Fact]
    public void EmailStopsBeforeDraftCreationWhenRequestedAttachmentIsMissing()
    {
        using var directory = new TemporaryExportDirectory();
        var missingPath = System.IO.Path.Combine(directory.Path, "missing.pptx");

        var result = PresentationReportSharing.EmailBody(
            [CreateReport("Presentation")],
            [missingPath]);

        Assert.False(result.Succeeded);
        Assert.Contains("no longer available", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkedFileIsCollectedIndependentlyOfPresentationUrl()
    {
        using var directory = new TemporaryExportDirectory();
        var presentationPath = System.IO.Path.Combine(directory.Path, "slides.pptx");
        File.WriteAllText(presentationPath, "presentation", Encoding.UTF8);
        var report = CreateReport("Presentation") with
        {
            PresentationFilePath = presentationPath,
            PresentationUrl = "https://example.test/slides"
        };

        var files = PresentationsPageView.CollectAvailablePresentationFiles([report]);

        Assert.Equal([presentationPath], files);
    }

    [Fact]
    public void SingleFileAttachmentIsResolvedEvenWhenAUrlIsLinked()
    {
        var report = CreateReport("Presentation") with
        {
            PresentationFilePath = null,
            PresentationUrl = "https://example.test/slides"
        };
        var resolverCalled = false;

        var succeeded = PresentationsPageView.TryResolveRequestedPresentationFiles(
            [report],
            candidate =>
            {
                resolverCalled = true;
                Assert.Same(report, candidate);
                return @"C:\Presentations\Selected.pptx";
            },
            out var files);

        Assert.True(succeeded);
        Assert.True(resolverCalled);
        Assert.Equal([@"C:\Presentations\Selected.pptx"], files);
    }

    [Fact]
    public void ArchiveDateUsesOffsetCapturedWithReport()
    {
        var report = CreateReport("Presentation") with
        {
            StartedAt = new DateTimeOffset(2026, 7, 22, 23, 30, 0, TimeSpan.Zero),
            UtcOffsetMinutes = 120
        };

        var captured = PresentationsPageView.CapturedLocalDateTime(report);

        Assert.Equal(new DateTime(2026, 7, 23, 1, 30, 0), captured.DateTime);
        Assert.Equal(TimeSpan.FromHours(2), captured.Offset);
    }

    private static PresentationReport CreateReport(string title)
    {
        var start = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(2));
        return new(
            "report-1",
            "operation-1",
            title,
            "powerpoint",
            "device-key",
            "Presenter phone",
            start,
            start.AddSeconds(150),
            120,
            150,
            120,
            false,
            [
                new(
                    1,
                    60,
                    30,
                    start.AddSeconds(60),
                    start.AddSeconds(90),
                    1,
                    2,
                    2,
                    2)
            ],
            [
                new(1, 30),
                new(2, 40),
                new(3, 50)
            ],
            @"C:\Presentations\Physics 101.pptx",
            "https://example.test/presentation");
    }

    private sealed class TemporaryExportDirectory : IDisposable
    {
        public TemporaryExportDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VolturaAir.Tests",
                $"presentation-exports-{Guid.NewGuid():N}");
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
