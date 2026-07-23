namespace VolturaAir.Host;

internal static class PresentationStoredReportValidator
{
    public static bool IsSafe(PresentationReport report) =>
        report.ReportId is { Length: > 0 and <= 64 } &&
        IsSafeIdentifier(report.ReportId) &&
        report.OperationId is { Length: > 0 and <= 64 } &&
        IsSafeIdentifier(report.OperationId) &&
        report.Title is { Length: > 0 and <= 300 } &&
        report.DeviceKey is { Length: > 0 and <= 64 } &&
        report.DeviceName is { Length: > 0 and <= 120 } &&
        PresentationCommands.IsTarget(report.Target) &&
        report.EndedAt >= report.StartedAt &&
        report.EndedAt - report.StartedAt <= TimeSpan.FromSeconds(PresentationReportProtocol.MaxDurationSeconds) &&
        report.UtcOffsetMinutes is >= -840 and <= 840 &&
        double.IsFinite(report.PlannedDurationSeconds) &&
        double.IsFinite(report.PresentationDurationSeconds) &&
        report.PlannedDurationSeconds is >= 0 and <= PresentationReportProtocol.MaxDurationSeconds &&
        report.PresentationDurationSeconds is >= 0 and <= PresentationReportProtocol.MaxDurationSeconds &&
        IsSafeTimeline(report) &&
        (report.PresentationFilePath is null ||
            report.PresentationFilePath is { Length: > 0 and <= 1024 } &&
            Path.IsPathFullyQualified(report.PresentationFilePath)) &&
        (report.PresentationUrl is null ||
            report.PresentationUrl is { Length: > 0 and <= 2048 } &&
            Uri.TryCreate(report.PresentationUrl, UriKind.Absolute, out var presentationUri) &&
            presentationUri.Scheme is "https" or "http");

    private static bool IsSafeIdentifier(string value) =>
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');

    private static bool IsSafeTimeline(PresentationReport report)
    {
        if (report.Breaks is null ||
            report.Slides is null ||
            report.Breaks.Count > PresentationReportProtocol.MaxBreakCount ||
            report.Slides.Count > PresentationReportProtocol.MaxSlideCount ||
            report.EndedDuringBreak && report.Breaks.Count == 0)
        {
            return false;
        }

        var expectedBreakNumber = 1;
        var previousPresentationElapsed = 0d;
        DateTimeOffset? previousBreakEndedAt = null;
        foreach (var entry in report.Breaks)
        {
            if (entry is null ||
                entry.BreakNumber != expectedBreakNumber ||
                !IsSafeDuration(entry.PresentationElapsedSeconds) ||
                entry.PresentationElapsedSeconds < previousPresentationElapsed ||
                entry.PresentationElapsedSeconds > report.PresentationDurationSeconds ||
                !IsSafeDuration(entry.BreakDurationSeconds) ||
                entry.StartedAt < report.StartedAt ||
                entry.EndedAt < entry.StartedAt ||
                entry.EndedAt > report.EndedAt ||
                previousBreakEndedAt is { } previous && entry.StartedAt < previous ||
                !IsSafeSlideRange(entry.SessionSlideMinimum, entry.SessionSlideMaximum) ||
                !IsSafeSlideNumber(entry.SlideNumberAtStart) ||
                !IsSafeSlideNumber(entry.SlideNumberAtEnd))
            {
                return false;
            }

            expectedBreakNumber++;
            previousPresentationElapsed = entry.PresentationElapsedSeconds;
            previousBreakEndedAt = entry.EndedAt;
        }

        if (report.EndedDuringBreak &&
            (report.Breaks[^1].EndedAt != report.EndedAt ||
             report.Breaks[^1].PresentationElapsedSeconds != report.PresentationDurationSeconds))
        {
            return false;
        }

        var seenSlides = new HashSet<int>();
        return report.Slides.All(entry =>
            entry is not null &&
            entry.SlideNumber is >= 1 and <= PresentationReportProtocol.MaxSlideCount &&
            seenSlides.Add(entry.SlideNumber) &&
            (entry.DurationSeconds is null || IsSafeDuration(entry.DurationSeconds.Value)));
    }

    private static bool IsSafeDuration(double value) =>
        double.IsFinite(value) && value is >= 0 and <= PresentationReportProtocol.MaxDurationSeconds;

    private static bool IsSafeSlideRange(int? minimum, int? maximum) =>
        minimum is null && maximum is null ||
        minimum is >= 1 and <= PresentationReportProtocol.MaxSlideCount &&
        maximum is >= 1 and <= PresentationReportProtocol.MaxSlideCount &&
        minimum <= maximum;

    private static bool IsSafeSlideNumber(int? value) =>
        value is null or >= 1 and <= PresentationReportProtocol.MaxSlideCount;
}
