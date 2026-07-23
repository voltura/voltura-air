namespace VolturaAir.Host.Features.Presentations;

internal static class PresentationReportNames
{
    public const string DefaultName = "Presentation";

    public static string CreateDefaultName(
        IReadOnlyList<PresentationReport> existingReports,
        string deviceKey)
    {
        var usedNames = existingReports
            .Where(report => string.Equals(report.DeviceKey, deviceKey, StringComparison.Ordinal))
            .Select(DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var suffix = 0; suffix <= PresentationReportStore.MaxReportCount; suffix += 1)
        {
            var candidate = suffix == 0 ? DefaultName : $"{DefaultName} ({suffix})";
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No presentation name is available within the archive limit.");
    }

    public static string DisplayName(PresentationReport report)
    {
        var localStart = report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes));
        var generatedWithDash = $"Presentation — {localStart:yyyy-MM-dd HH:mm} — {report.DeviceName}";
        var generatedWithHyphen = $"Presentation - {localStart:yyyy-MM-dd HH:mm} - {report.DeviceName}";
        return string.Equals(report.Title, generatedWithDash, StringComparison.Ordinal) ||
            string.Equals(report.Title, generatedWithHyphen, StringComparison.Ordinal)
            ? DefaultName
            : report.Title;
    }

    public static string SuggestedExportBaseName(PresentationReport report)
    {
        var localStart = report.StartedAt.ToOffset(TimeSpan.FromMinutes(report.UtcOffsetMinutes));
        return $"{DisplayName(report)} - {localStart:yyyy-MM-dd HH:mm} - {report.DeviceName}";
    }
}
