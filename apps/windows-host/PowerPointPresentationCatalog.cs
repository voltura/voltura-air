namespace VolturaAir.Host;

internal sealed record AvailablePowerPointPresentation(
    string PresentationId,
    string Title,
    string FileName,
    string CanonicalPath);

internal sealed class PowerPointPresentationCatalog : IDisposable
{
    internal const int MaxCandidates = 100;
    private readonly IPresentationReportStore _store;
    private readonly Lock _gate = new();
    private IReadOnlyList<AvailablePowerPointPresentation> _candidates = [];
    private int _disposeState;

    internal PowerPointPresentationCatalog(IPresentationReportStore store)
    {
        _store = store;
        _store.ReportsChanged += OnReportsChanged;
        Refresh();
    }

    internal event EventHandler? Changed;

    internal IReadOnlyList<AvailablePowerPointPresentation> GetAvailable(
        PowerPointAutomationSnapshot snapshot)
    {
        IReadOnlyList<AvailablePowerPointPresentation> candidates;
        lock (_gate)
        {
            candidates = _candidates;
        }

        var openPaths = snapshot.Presentations
            .Select(item => NormalizePath(item.SourcePath))
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. candidates.Where(candidate => !openPaths.Contains(candidate.CanonicalPath))];
    }

    internal AvailablePowerPointPresentation? Resolve(string presentationId)
    {
        Refresh();
        lock (_gate)
        {
            return _candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.PresentationId, presentationId, StringComparison.Ordinal));
        }
    }

    internal void Refresh()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var read = _store.ReadAll();
        var next = read.Succeeded
            ? read.Reports
                .Where(report =>
                    string.Equals(report.Target, "powerpoint", StringComparison.Ordinal) &&
                    NormalizePath(report.PresentationFilePath) is not null)
                .Select(report => new
                {
                    Report = report,
                    Path = NormalizePath(report.PresentationFilePath)!,
                    LastModified = LastModified(report)
                })
                .Where(item => File.Exists(item.Path))
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.LastModified)
                    .ThenByDescending(item => item.Report.EndedAt)
                    .First())
                .OrderByDescending(item => item.LastModified)
                .ThenByDescending(item => item.Report.EndedAt)
                .Take(MaxCandidates)
                .Select(item => new AvailablePowerPointPresentation(
                    item.Report.ReportId,
                    Features.Presentations.PresentationReportNames.DisplayName(item.Report),
                    Path.GetFileName(item.Path),
                    item.Path))
                .ToArray()
            : [];

        var changed = false;
        lock (_gate)
        {
            if (!_candidates.SequenceEqual(next))
            {
                _candidates = next;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _store.ReportsChanged -= OnReportsChanged;
    }

    private void OnReportsChanged(object? sender, EventArgs eventArgs) => Refresh();

    private DateTimeOffset LastModified(PresentationReport report)
    {
        if (string.IsNullOrWhiteSpace(_store.ReportDirectory))
        {
            return report.EndedAt;
        }

        var path = Path.Combine(_store.ReportDirectory, report.DeviceKey, $"{report.ReportId}.json");
        try
        {
            return File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : report.EndedAt;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return report.EndedAt;
        }
    }

    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
