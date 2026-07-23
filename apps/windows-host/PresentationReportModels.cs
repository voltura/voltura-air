namespace VolturaAir.Host;

internal sealed record PresentationReportBreak(
    int BreakNumber,
    double PresentationElapsedSeconds,
    double BreakDurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int? SessionSlideMinimum,
    int? SessionSlideMaximum,
    int? SlideNumberAtStart,
    int? SlideNumberAtEnd);

internal sealed record PresentationReportSlide(
    int SlideNumber,
    double? DurationSeconds);

internal sealed record PresentationReport(
    string ReportId,
    string OperationId,
    string Title,
    string Target,
    string DeviceKey,
    string DeviceName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int UtcOffsetMinutes,
    double PlannedDurationSeconds,
    double PresentationDurationSeconds,
    bool EndedDuringBreak,
    IReadOnlyList<PresentationReportBreak> Breaks,
    IReadOnlyList<PresentationReportSlide> Slides,
    string? PresentationFilePath,
    string? PresentationUrl)
{
    public double BreakDurationSeconds => Breaks.Sum(entry => entry.BreakDurationSeconds);
    public int SessionCount => Breaks.Count + (EndedDuringBreak ? 0 : 1);
}

internal sealed record PresentationReportSaveRequest(
    string OperationId,
    string ReportId,
    string Target,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int UtcOffsetMinutes,
    double PlannedDurationSeconds,
    double PresentationDurationSeconds,
    bool EndedDuringBreak,
    IReadOnlyList<PresentationReportBreak> Breaks,
    IReadOnlyList<PresentationReportSlide> Slides);

internal sealed record PresentationReportSaveResult(
    bool Succeeded,
    string? Code,
    string Message,
    string ReportId);

internal sealed record PresentationReportReadResult(
    bool Succeeded,
    IReadOnlyList<PresentationReport> Reports,
    string? Error = null);

internal sealed record PresentationReportMutationResult(
    bool Succeeded,
    string Message);

internal interface IPresentationReportStore
{
    string ReportDirectory { get; }

    event EventHandler? ReportsChanged;

    Task<PresentationReportSaveResult> SaveAsync(
        PresentationReportSaveRequest request,
        string clientId,
        string deviceName,
        CancellationToken cancellationToken);

    PresentationReportReadResult ReadAll();

    PresentationReportMutationResult Rename(string reportId, string title);

    PresentationReportMutationResult Delete(string reportId);

    PresentationReportMutationResult DeleteMany(IReadOnlyCollection<string> reportIds);

    PresentationReportMutationResult DeleteAll();

    PresentationReportMutationResult SetPresentationFile(string reportId, string? path);

    PresentationReportMutationResult SetPresentationUrl(string reportId, string? url);
}
