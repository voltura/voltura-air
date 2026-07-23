using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

internal sealed class PresentationReportStore(string? reportDirectory = null) : IPresentationReportStore
{
    public const int MaxReportCount = 1000;
    internal const int MaxStoredReportBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly Lock _gate = new();

    public string ReportDirectory { get; } = reportDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voltura Air",
        "Presentation statistics");

    public event EventHandler? ReportsChanged;

    public Task<PresentationReportSaveResult> SaveAsync(
        PresentationReportSaveRequest request,
        string clientId,
        string deviceName,
        CancellationToken cancellationToken) =>
        Task.Run(() => Save(request, clientId, deviceName), cancellationToken);

    public PresentationReportReadResult ReadAll()
    {
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(ReportDirectory))
                {
                    return new(true, []);
                }

                var reports = new List<PresentationReport>();
                foreach (var path in Directory.EnumerateFiles(ReportDirectory, "*.json", SearchOption.AllDirectories)
                    .Take(MaxReportCount + 1))
                {
                    try
                    {
                        if (TryReadStoredReport(path, out var report))
                        {
                            reports.Add(report!);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                    {
                        // Stored reports are untrusted. Preserve unreadable files and omit them from the archive.
                    }
                }

                return new(true, [.. reports
                    .OrderByDescending(report => report.StartedAt)
                    .Take(MaxReportCount)]);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, [], exception.Message);
        }
    }

    public PresentationReportMutationResult Rename(string reportId, string title)
    {
        try
        {
            lock (_gate)
            {
                var stored = FindStoredReport(reportId);
                if (stored is null)
                {
                    return new(false, "The presentation could not be found.");
                }

                WriteReplacement(stored.Value.Path, stored.Value.Report with { Title = title });
            }

            ReportsChanged?.Invoke(this, EventArgs.Empty);
            return new(true, "Presentation renamed.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "Windows could not rename the presentation. Check the archive folder and try again.");
        }
    }

    public PresentationReportMutationResult Delete(string reportId)
    {
        try
        {
            lock (_gate)
            {
                var stored = FindStoredReport(reportId);
                if (stored is null)
                {
                    return new(false, "The presentation could not be found.");
                }

                File.Delete(stored.Value.Path);
            }

            ReportsChanged?.Invoke(this, EventArgs.Empty);
            return new(true, "Presentation deleted.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "Windows could not delete the presentation. Check the archive folder and try again.");
        }
    }

    public PresentationReportMutationResult DeleteAll()
    {
        try
        {
            lock (_gate)
            {
                if (Directory.Exists(ReportDirectory))
                {
                    foreach (var path in Directory.EnumerateFiles(ReportDirectory, "*.json", SearchOption.AllDirectories)
                        .Take(MaxReportCount + 1))
                    {
                        File.Delete(path);
                    }
                }
            }

            ReportsChanged?.Invoke(this, EventArgs.Empty);
            return new(true, "All presentations deleted.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "Windows could not delete every presentation. Check the archive folder and try again.");
        }
    }

    public PresentationReportMutationResult DeleteMany(IReadOnlyCollection<string> reportIds)
    {
        ArgumentNullException.ThrowIfNull(reportIds);
        if (reportIds.Count == 0)
        {
            return new(true, "No presentations matched the current filters.");
        }

        try
        {
            lock (_gate)
            {
                var requestedIds = reportIds.ToHashSet(StringComparer.Ordinal);
                var storedReports = requestedIds.Select(FindStoredReport).ToList();
                if (storedReports.Any(stored => stored is null))
                {
                    return new(false, "One or more filtered presentations could not be found. Refresh and try again.");
                }

                foreach (var stored in storedReports)
                {
                    File.Delete(stored!.Value.Path);
                }
            }

            ReportsChanged?.Invoke(this, EventArgs.Empty);
            return new(true, $"{reportIds.Count} presentations deleted.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "Windows could not delete every filtered presentation. Refresh the archive and try again.");
        }
    }

    public PresentationReportMutationResult SetPresentationFile(string reportId, string? path)
    {
        try
        {
            lock (_gate)
            {
                var stored = FindStoredReport(reportId);
                if (stored is null)
                {
                    return new(false, "The presentation could not be found.");
                }

                WriteReplacement(stored.Value.Path, stored.Value.Report with { PresentationFilePath = path });
            }

            ReportsChanged?.Invoke(this, EventArgs.Empty);
            return new(true, path is null ? "Presentation file removed." : "Presentation file linked.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "Windows could not update the presentation file. Check the archive folder and try again.");
        }
    }

    public PresentationReportMutationResult SetPresentationUrl(string reportId, string? url)
    {
        try
        {
            lock (_gate)
            {
                var stored = FindStoredReport(reportId);
                if (stored is null)
                {
                    return new(false, "The presentation could not be found.");
                }

                WriteReplacement(stored.Value.Path, stored.Value.Report with { PresentationUrl = url });
            }

            ReportsChanged?.Invoke(this, EventArgs.Empty);
            return new(true, url is null ? "Presentation URL removed." : "Presentation URL linked.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "Windows could not update the presentation URL. Check the archive folder and try again.");
        }
    }

    private PresentationReportSaveResult Save(
        PresentationReportSaveRequest request,
        string clientId,
        string deviceName)
    {
        try
        {
            PresentationReport? savedReport = null;
            lock (_gate)
            {
                Directory.CreateDirectory(ReportDirectory);
                var existing = ReadAllCore();
                var duplicate = existing.FirstOrDefault(report =>
                    string.Equals(report.OperationId, request.OperationId, StringComparison.Ordinal) ||
                    string.Equals(report.ReportId, request.ReportId, StringComparison.Ordinal));
                if (duplicate is not null)
                {
                    return string.Equals(duplicate.OperationId, request.OperationId, StringComparison.Ordinal) &&
                        string.Equals(duplicate.ReportId, request.ReportId, StringComparison.Ordinal)
                        ? new(true, null, "Presentation data was already saved.", duplicate.ReportId)
                        : new(false, "report-conflict", "That presentation report identifier is already in use.", request.ReportId);
                }

                if (existing.Count >= MaxReportCount)
                {
                    return new(false, "archive-full", "The presentation archive is full. Delete reports on the PC before saving another.", request.ReportId);
                }

                var deviceKey = CreateDeviceKey(clientId);
                var normalizedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Unknown device" : deviceName.Trim();
                var report = new PresentationReport(
                    request.ReportId,
                    request.OperationId,
                    Features.Presentations.PresentationReportNames.CreateDefaultName(existing, deviceKey),
                    request.Target,
                    deviceKey,
                    normalizedDeviceName,
                    request.StartedAt,
                    request.EndedAt,
                    request.UtcOffsetMinutes,
                    request.PlannedDurationSeconds,
                    request.PresentationDurationSeconds,
                    request.EndedDuringBreak,
                    request.Breaks,
                    request.Slides,
                    PresentationFilePath: null,
                    PresentationUrl: null);
                var deviceDirectory = Path.Combine(ReportDirectory, deviceKey);
                Directory.CreateDirectory(deviceDirectory);
                var finalPath = Path.Combine(deviceDirectory, $"{request.ReportId}.json");
                var temporaryPath = Path.Combine(deviceDirectory, $".{request.ReportId}.{Guid.NewGuid():N}.tmp");
                try
                {
                    var json = JsonSerializer.Serialize(report, JsonOptions);
                    using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        16 * 1024,
                        FileOptions.WriteThrough))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    {
                        writer.Write(json);
                        writer.Flush();
                        stream.Flush(flushToDisk: true);
                    }

                    File.Move(temporaryPath, finalPath);
                    savedReport = report;
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }

            if (savedReport is not null)
            {
                ReportsChanged?.Invoke(this, EventArgs.Empty);
            }

            return new(true, null, "Presentation data saved on the PC.", request.ReportId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(false, "storage-failed", "Windows could not save the presentation data. Check the archive folder and try again.", request.ReportId);
        }
    }

    private List<PresentationReport> ReadAllCore()
    {
        var reports = new List<PresentationReport>();
        if (!Directory.Exists(ReportDirectory))
        {
            return reports;
        }

        foreach (var path in Directory.EnumerateFiles(ReportDirectory, "*.json", SearchOption.AllDirectories)
            .Take(MaxReportCount + 1))
        {
            try
            {
                if (TryReadStoredReport(path, out var report))
                {
                    reports.Add(report!);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Stored reports are untrusted. A corrupt file cannot block new valid reports.
            }
        }

        return reports;
    }

    private (string Path, PresentationReport Report)? FindStoredReport(string reportId)
    {
        if (!Directory.Exists(ReportDirectory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(ReportDirectory, "*.json", SearchOption.AllDirectories)
            .Take(MaxReportCount + 1))
        {
            try
            {
                if (TryReadStoredReport(path, out var report) &&
                    string.Equals(report!.ReportId, reportId, StringComparison.Ordinal))
                {
                    return (path, report!);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Stored reports are untrusted. Keep searching bounded files.
            }
        }

        return null;
    }

    private static void WriteReplacement(string finalPath, PresentationReport report)
    {
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new IOException("The presentation report path has no parent directory.");
        var temporaryPath = Path.Combine(directory, $".{report.ReportId}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(report, JsonOptions);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryReadStoredReport(string path, out PresentationReport? report)
    {
        report = null;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaxStoredReportBytes)
        {
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MaxStoredReportBytes + 1);
        try
        {
            var bytesRead = 0;
            while (bytesRead < MaxStoredReportBytes + 1)
            {
                var count = stream.Read(buffer, bytesRead, MaxStoredReportBytes + 1 - bytesRead);
                if (count == 0)
                {
                    break;
                }

                bytesRead += count;
            }

            if (bytesRead == 0 || bytesRead > MaxStoredReportBytes || stream.ReadByte() != -1)
            {
                return false;
            }

            report = JsonSerializer.Deserialize<PresentationReport>(
                buffer.AsSpan(0, bytesRead),
                JsonOptions);
            return report is not null && PresentationStoredReportValidator.IsSafe(report);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string CreateDeviceKey(string clientId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientId));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
