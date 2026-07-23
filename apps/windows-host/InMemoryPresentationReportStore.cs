using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal sealed class InMemoryPresentationReportStore : IPresentationReportStore
{
    private readonly Lock _gate = new();
    private readonly List<PresentationReport> _reports = [];

    public string ReportDirectory => string.Empty;

    public event EventHandler? ReportsChanged;

    public Task<PresentationReportSaveResult> SaveAsync(
        PresentationReportSaveRequest request,
        string clientId,
        string deviceName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var duplicate = _reports.FirstOrDefault(report =>
                string.Equals(report.OperationId, request.OperationId, StringComparison.Ordinal) ||
                string.Equals(report.ReportId, request.ReportId, StringComparison.Ordinal));
            if (duplicate is not null)
            {
                return Task.FromResult(
                    string.Equals(duplicate.OperationId, request.OperationId, StringComparison.Ordinal) &&
                    string.Equals(duplicate.ReportId, request.ReportId, StringComparison.Ordinal)
                        ? new PresentationReportSaveResult(true, null, "Presentation data was already saved.", duplicate.ReportId)
                        : new PresentationReportSaveResult(false, "report-conflict", "That presentation report identifier is already in use.", request.ReportId));
            }

            if (_reports.Count >= PresentationReportStore.MaxReportCount)
            {
                return Task.FromResult(new PresentationReportSaveResult(
                    false,
                    "archive-full",
                    "The presentation archive is full. Delete reports on the PC before saving another.",
                    request.ReportId));
            }

            var deviceKey = CreateDeviceKey(clientId);
            _reports.Add(new(
                request.ReportId,
                request.OperationId,
                Features.Presentations.PresentationReportNames.CreateDefaultName(_reports, deviceKey),
                request.Target,
                deviceKey,
                deviceName,
                request.StartedAt,
                request.EndedAt,
                request.UtcOffsetMinutes,
                request.PlannedDurationSeconds,
                request.PresentationDurationSeconds,
                request.EndedDuringBreak,
                request.Breaks,
                request.Slides,
                PresentationFilePath: null,
                PresentationUrl: null));
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new PresentationReportSaveResult(
            true,
            null,
            "Presentation data saved on the PC.",
            request.ReportId));
    }

    public PresentationReportReadResult ReadAll()
    {
        lock (_gate)
        {
            return new(true, [.. _reports.OrderByDescending(report => report.StartedAt)]);
        }
    }

    public PresentationReportMutationResult Rename(string reportId, string title)
    {
        lock (_gate)
        {
            var index = _reports.FindIndex(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal));
            if (index < 0)
            {
                return new(false, "The presentation could not be found.");
            }

            _reports[index] = _reports[index] with { Title = title };
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return new(true, "Presentation renamed.");
    }

    public PresentationReportMutationResult Delete(string reportId)
    {
        lock (_gate)
        {
            if (_reports.RemoveAll(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal)) == 0)
            {
                return new(false, "The presentation could not be found.");
            }
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return new(true, "Presentation deleted.");
    }

    public PresentationReportMutationResult DeleteAll()
    {
        lock (_gate)
        {
            _reports.Clear();
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return new(true, "All presentations deleted.");
    }

    public PresentationReportMutationResult DeleteMany(IReadOnlyCollection<string> reportIds)
    {
        ArgumentNullException.ThrowIfNull(reportIds);
        var requestedIds = reportIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            if (_reports.Count(report => requestedIds.Contains(report.ReportId)) != requestedIds.Count)
            {
                return new(false, "One or more filtered presentations could not be found. Refresh and try again.");
            }

            _reports.RemoveAll(report => requestedIds.Contains(report.ReportId));
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return new(true, $"{requestedIds.Count} presentations deleted.");
    }

    public PresentationReportMutationResult SetPresentationFile(string reportId, string? path)
    {
        lock (_gate)
        {
            var index = _reports.FindIndex(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal));
            if (index < 0)
            {
                return new(false, "The presentation could not be found.");
            }

            _reports[index] = _reports[index] with { PresentationFilePath = path };
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return new(true, path is null ? "Presentation file removed." : "Presentation file linked.");
    }

    public PresentationReportMutationResult SetPresentationUrl(string reportId, string? url)
    {
        lock (_gate)
        {
            var index = _reports.FindIndex(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal));
            if (index < 0)
            {
                return new(false, "The presentation could not be found.");
            }

            _reports[index] = _reports[index] with { PresentationUrl = url };
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
        return new(true, url is null ? "Presentation URL removed." : "Presentation URL linked.");
    }

    internal void Add(PresentationReport report)
    {
        lock (_gate)
        {
            _reports.Add(report);
        }

        ReportsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string CreateDeviceKey(string clientId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientId));
        return $"isolated-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}
