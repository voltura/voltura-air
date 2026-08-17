using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolturaAir.Host;

internal sealed class PresentationReportStore(
    string? reportDirectory = null,
    Action<string>? beforeDeleteArtifact = null,
    Action<string>? beforeReplaceArtifact = null,
    Action<string>? beforeReplaceCommit = null,
    bool reportReplaceFailureAfterSuccess = false,
    bool reportReplaceFailureBeforeSuccess = false) : IPresentationReportStore
{
    public const int MaxReportCount = 1000;
    internal const int MaxStoredReportBytes = 256 * 1024;
    internal const int MaximumScannedFiles = (MaxReportCount + 1) * 10;
    private const int MaxManifestBytes = 512 * 1024;
    private const int MaxJournalBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly Lock _gate = new();
    private readonly Action<string>? _beforeDeleteArtifact = beforeDeleteArtifact;
    private readonly Action<string>? _beforeReplaceArtifact = beforeReplaceArtifact;
    private readonly Action<string>? _beforeReplaceCommit = beforeReplaceCommit;
    private readonly bool _reportReplaceFailureAfterSuccess = reportReplaceFailureAfterSuccess;
    private readonly bool _reportReplaceFailureBeforeSuccess = reportReplaceFailureBeforeSuccess;

    public string ReportDirectory { get; } = reportDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voltura Air", "Presentation statistics");
    private string ManifestPath => Path.Combine(ReportDirectory, "manifest.json");
    private string StagedManifestPath => Path.Combine(ReportDirectory, ".manifest.staged");
    private string BackupManifestPath => Path.Combine(ReportDirectory, ".manifest.backup");
    private string JournalPath => Path.Combine(ReportDirectory, "transaction.json");

    public event EventHandler? ReportsChanged;

    public Task<PresentationReportSaveResult> SaveAsync(
        PresentationReportSaveRequest request, string clientId, string deviceName, CancellationToken cancellationToken) =>
        Task.Run(() => Save(request, clientId, deviceName, cancellationToken), cancellationToken);

    public PresentationReportReadResult ReadAll()
    {
        try
        {
            lock (_gate)
            {
                var reports = LoadAvailableManifest().Entries.Select(ReadEntry)
                    .OrderByDescending(report => report.StartedAt).ToArray();
                return new(true, reports);
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return new(false, [], "The presentation archive is unavailable because its inventory could not be verified.");
        }
    }

    public PresentationReportMutationResult Rename(string reportId, string title) =>
        Replace(reportId, report => report with { Title = title }, "Presentation renamed.");
    public PresentationReportMutationResult SetPresentationFile(string reportId, string? path) =>
        Replace(reportId, report => report with { PresentationFilePath = path },
            path is null ? "Presentation file removed." : "Presentation file linked.");
    public PresentationReportMutationResult SetPresentationUrl(string reportId, string? url) =>
        Replace(reportId, report => report with { PresentationUrl = url },
            url is null ? "Presentation URL removed." : "Presentation URL linked.");
    public PresentationReportMutationResult Delete(string reportId) =>
        DeleteManyCore([reportId], "Presentation deleted.");

    public PresentationReportMutationResult DeleteMany(IReadOnlyCollection<string> reportIds)
    {
        ArgumentNullException.ThrowIfNull(reportIds);
        return reportIds.Count == 0
            ? new(true, "No presentations matched the current filters.")
            : DeleteManyCore(reportIds, $"{reportIds.Count} presentations deleted.");
    }

    public PresentationReportMutationResult DeleteAll()
    {
        try
        {
            lock (_gate)
            {
                var manifest = LoadAvailableManifest();
                Commit(manifest, new Manifest([]), null);
            }
            NotifyReportsChanged();
            return new(true, "All presentations deleted.");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return new(false, "Windows could not delete every presentation. The archive was left recoverable.");
        }
    }

    private PresentationReportSaveResult Save(
        PresentationReportSaveRequest request, string clientId, string deviceName, CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = LoadAvailableManifest();
                var existing = manifest.Entries.Select(ReadEntry).ToArray();
                var duplicate = existing.FirstOrDefault(report =>
                    report.OperationId == request.OperationId || report.ReportId == request.ReportId);
                if (duplicate is not null)
                {
                    return duplicate.OperationId == request.OperationId && duplicate.ReportId == request.ReportId
                        ? new(true, null, "Presentation data was already saved.", duplicate.ReportId)
                        : new(false, "report-conflict", "That presentation report identifier is already in use.", request.ReportId);
                }
                if (manifest.Entries.Count >= MaxReportCount)
                    return new(false, "archive-full", "The presentation archive is full. Delete reports on the PC before saving another.", request.ReportId);

                var deviceKey = CreateDeviceKey(clientId);
                var report = new PresentationReport(
                    request.ReportId, request.OperationId,
                    Features.Presentations.PresentationReportNames.CreateInitialName(existing, deviceKey, request.SuggestedTitle),
                    request.Target, deviceKey,
                    string.IsNullOrWhiteSpace(deviceName) ? "Unknown device" : deviceName.Trim(),
                    request.StartedAt, request.EndedAt, request.UtcOffsetMinutes,
                    request.PlannedDurationSeconds, request.PresentationDurationSeconds, request.EndedDuringBreak,
                    request.Breaks, request.Slides, request.PresentationFilePath, null, request.SlideVisits);
                var artifact = CreateArtifact(report);
                Commit(manifest, new Manifest([.. manifest.Entries, artifact.Entry]), artifact);
            }
            NotifyReportsChanged();
            return new(true, null, "Presentation data saved on the PC.", request.ReportId);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return new(false, "storage-failed", "Windows could not save the presentation data. The archive was left recoverable.", request.ReportId);
        }
    }

    private PresentationReportMutationResult Replace(
        string reportId, Func<PresentationReport, PresentationReport> update, string successMessage)
    {
        try
        {
            lock (_gate)
            {
                var manifest = LoadAvailableManifest();
                var index = FindEntryIndex(manifest, reportId);
                if (index < 0) return new(false, "The presentation could not be found.");
                var oldEntry = manifest.Entries[index];
                _beforeReplaceArtifact?.Invoke(ArtifactPath(oldEntry.FileName));
                var artifact = CreateArtifact(update(ReadEntry(oldEntry)));
                var entries = manifest.Entries.ToArray();
                entries[index] = artifact.Entry;
                Commit(manifest, new Manifest(entries), artifact);
            }
            NotifyReportsChanged();
            return new(true, successMessage);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return new(false, "Windows could not update the presentation. The archive was left recoverable.");
        }
    }

    private PresentationReportMutationResult DeleteManyCore(IReadOnlyCollection<string> reportIds, string successMessage)
    {
        try
        {
            lock (_gate)
            {
                var manifest = LoadAvailableManifest();
                var ids = reportIds.ToHashSet(StringComparer.Ordinal);
                if (ids.Count != reportIds.Count || ids.Any(id => FindEntryIndex(manifest, id) < 0))
                    return new(false, "One or more presentations could not be found. Refresh and try again.");
                Commit(manifest, new Manifest([.. manifest.Entries.Where(entry => !ids.Contains(entry.ReportId))]), null);
            }
            NotifyReportsChanged();
            return new(true, successMessage);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return new(false, "Windows could not delete every presentation. The archive was left recoverable.");
        }
    }

    private Manifest LoadAvailableManifest()
    {
        EnsureSafeRoot();
        RecoverIfNeeded();
        if (!File.Exists(ManifestPath)) return new([]);
        var manifest = JsonSerializer.Deserialize<Manifest>(ReadBounded(ManifestPath, MaxManifestBytes), JsonOptions)
            ?? throw new IOException("The presentation manifest is empty.");
        ValidateManifest(manifest);
        return manifest;
    }

    private void RecoverIfNeeded()
    {
        if (!File.Exists(JournalPath)) return;
        var journal = JsonSerializer.Deserialize<Journal>(ReadBounded(JournalPath, MaxJournalBytes), JsonOptions)
            ?? throw new IOException("The presentation transaction journal is empty.");
        ValidateJournal(journal);
        var currentHash = HashFileIfPresent(ManifestPath);
        var stagedHash = HashFileIfPresent(StagedManifestPath);
        var backupHash = HashFileIfPresent(BackupManifestPath);
        if (currentHash == journal.NewManifestHash)
        {
            CompleteCommittedTransaction(journal);
            return;
        }
        if (currentHash == journal.OldManifestHash && stagedHash == journal.NewManifestHash)
        {
            PromoteManifest(journal.OldManifestHash);
            CompleteCommittedTransaction(journal);
            return;
        }
        if (journal.OldManifestHash is null && currentHash is null && stagedHash == journal.NewManifestHash)
        {
            File.Move(StagedManifestPath, ManifestPath);
            CompleteCommittedTransaction(journal);
            return;
        }
        if (currentHash is null && backupHash == journal.OldManifestHash && stagedHash == journal.NewManifestHash)
        {
            File.Move(StagedManifestPath, ManifestPath);
            CompleteCommittedTransaction(journal);
            return;
        }
        if (currentHash == journal.OldManifestHash && stagedHash is null)
        {
            RollBackUncommittedTransaction(journal);
            return;
        }
        throw new IOException("The presentation transaction does not match its owned manifests.");
    }

    private void Commit(Manifest oldManifest, Manifest newManifest, Artifact? newArtifact)
    {
        Directory.CreateDirectory(ReportDirectory);
        EnsureSafeRoot();
        var oldBytes = File.Exists(ManifestPath) ? ReadBounded(ManifestPath, MaxManifestBytes) : null;
        var oldHash = oldBytes is null ? null : Hash(oldBytes);
        var newBytes = SerializeBounded(newManifest, MaxManifestBytes);
        var newHash = Hash(newBytes);
        if (oldHash == newHash) return;
        if (newArtifact is not null) WriteArtifactIfMissing(newArtifact);
        WriteFlushedNew(StagedManifestPath, newBytes);
        var removed = oldManifest.Entries
            .Where(old => newManifest.Entries.All(current => current.FileName != old.FileName)).ToArray();
        var journal = new Journal(oldHash, newHash, newArtifact?.Entry, removed);
        WriteFlushedNew(JournalPath, SerializeBounded(journal, MaxJournalBytes));
        try
        {
            if (oldManifest.Entries.Count > 0) _beforeReplaceCommit?.Invoke(ManifestPath);
            if (oldManifest.Entries.Count > 0 && _reportReplaceFailureBeforeSuccess)
                throw new IOException("Injected manifest replacement failure.");
            PromoteManifest(oldHash);
            // A replacement API may report failure after the rename took effect.
            // The manifest hash, rather than the API result, owns the outcome.
            if (oldManifest.Entries.Count > 0 && _reportReplaceFailureAfterSuccess && HashFileIfPresent(ManifestPath) != newHash)
                throw new IOException("Injected post-commit manifest status failure.");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            var currentHash = HashFileIfPresent(ManifestPath);
            if (currentHash == newHash) return;
            if (currentHash == oldHash)
            {
                RollBackUncommittedTransaction(journal);
                DeleteIfPresent(StagedManifestPath);
            }
            throw;
        }
        try { CompleteCommittedTransaction(journal); }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // The manifest commit is authoritative. Keep the journal so the
            // next store access retries only verified transaction-owned cleanup.
        }
    }

    private void PromoteManifest(string? oldHash)
    {
        if (oldHash is null)
        {
            if (File.Exists(ManifestPath)) throw new IOException("The presentation manifest appeared before commit.");
            File.Move(StagedManifestPath, ManifestPath);
        }
        else
        {
            if (HashFileIfPresent(ManifestPath) != oldHash)
                throw new IOException("The presentation manifest changed before commit.");
            File.Replace(StagedManifestPath, ManifestPath, BackupManifestPath, ignoreMetadataErrors: false);
        }
    }

    private void CompleteCommittedTransaction(Journal journal)
    {
        foreach (var entry in journal.RemovedEntries)
        {
            var path = ArtifactPath(entry.FileName);
            if (!File.Exists(path) || HashFileIfPresent(path) != entry.Sha256) continue;
            _beforeDeleteArtifact?.Invoke(path);
            if (HashFileIfPresent(path) != entry.Sha256) continue;
            File.Delete(path);
        }
        DeleteIfPresent(StagedManifestPath);
        DeleteIfPresent(BackupManifestPath);
        DeleteIfPresent(JournalPath);
    }

    private void RollBackUncommittedTransaction(Journal journal)
    {
        if (journal.NewArtifact is { } artifact)
        {
            var path = ArtifactPath(artifact.FileName);
            if (HashFileIfPresent(path) == artifact.Sha256) File.Delete(path);
        }
        DeleteIfPresent(BackupManifestPath);
        DeleteIfPresent(JournalPath);
    }

    private PresentationReport ReadEntry(ManifestEntry entry)
    {
        var bytes = ReadBounded(ArtifactPath(entry.FileName), MaxStoredReportBytes);
        if (bytes.LongLength != entry.ByteLength || Hash(bytes) != entry.Sha256)
            throw new IOException("A presentation artifact does not match the manifest.");
        var report = JsonSerializer.Deserialize<PresentationReport>(bytes, JsonOptions);
        if (report is null || report.ReportId != entry.ReportId || !PresentationStoredReportValidator.IsSafe(report))
            throw new IOException("A presentation artifact is invalid.");
        return report;
    }

    private static Artifact CreateArtifact(PresentationReport report)
    {
        if (!PresentationStoredReportValidator.IsSafe(report)) throw new IOException("The presentation report is invalid.");
        var bytes = SerializeBounded(report, MaxStoredReportBytes);
        var hash = Hash(bytes);
        return new(bytes, new(report.ReportId, $"{hash.ToLowerInvariant()}.report", bytes.LongLength, hash));
    }

    private void WriteArtifactIfMissing(Artifact artifact)
    {
        var path = ArtifactPath(artifact.Entry.FileName);
        if (File.Exists(path))
        {
            if (HashFileIfPresent(path) != artifact.Entry.Sha256)
                throw new IOException("A content-addressed artifact was substituted.");
            return;
        }
        WriteFlushedNew(path, artifact.Bytes);
    }

    private static void ValidateManifest(Manifest manifest)
    {
        if (manifest.Entries is null || manifest.Entries.Count > MaxReportCount ||
            manifest.Entries.Select(entry => entry.ReportId).Distinct(StringComparer.Ordinal).Count() != manifest.Entries.Count ||
            manifest.Entries.Any(entry => !IsReportId(entry.ReportId) || !IsValidEntry(entry)))
            throw new IOException("The presentation manifest is invalid.");
    }

    private static void ValidateJournal(Journal journal)
    {
        if (!IsHashOrNull(journal.OldManifestHash) || !IsHash(journal.NewManifestHash) ||
            journal.RemovedEntries is null ||
            journal.RemovedEntries.Count > MaxReportCount ||
            journal.RemovedEntries.Any(entry => !IsReportId(entry.ReportId) || !IsValidEntry(entry)) ||
            journal.NewArtifact is { } artifact && (!IsReportId(artifact.ReportId) || !IsValidEntry(artifact)))
            throw new IOException("The presentation transaction journal is invalid.");
    }

    private string ArtifactPath(string fileName)
    {
        if (!IsArtifactFileName(fileName)) throw new IOException("The presentation artifact name is invalid.");
        return Path.Combine(ReportDirectory, fileName);
    }

    private static bool IsValidEntry(ManifestEntry entry) =>
        IsArtifactFileName(entry.FileName) && entry.ByteLength is > 0 and <= MaxStoredReportBytes && IsHash(entry.Sha256);
    private static bool IsArtifactFileName(string value) =>
        value.Length == 71 && value.EndsWith(".report", StringComparison.Ordinal) &&
        value.AsSpan(0, 64).ToArray().All(Uri.IsHexDigit);
    private static bool IsReportId(string value) => value is { Length: > 0 and <= 64 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool IsHashOrNull(string? value) => value is null || IsHash(value);

    private static int FindEntryIndex(Manifest manifest, string reportId)
    {
        for (var index = 0; index < manifest.Entries.Count; index++)
            if (manifest.Entries[index].ReportId == reportId) return index;
        return -1;
    }

    private void EnsureSafeRoot()
    {
        if (Directory.Exists(ReportDirectory) &&
            (File.GetAttributes(ReportDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The presentation archive root cannot be a file-system link.");
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new IOException("A presentation store file has an invalid length.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] SerializeBounded<T>(T value, int maximum)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length is <= 0 || bytes.Length > maximum) throw new IOException("A presentation store value exceeds its bound.");
        return bytes;
    }

    private static void WriteFlushedNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string? HashFileIfPresent(string path) => File.Exists(path) ? Hash(ReadBounded(path, MaxJournalBytes)) : null;
    private static void DeleteIfPresent(string path) { if (File.Exists(path)) File.Delete(path); }
    private static string CreateDeviceKey(string clientId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId)).AsSpan(0, 16)).ToLowerInvariant();
    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException;

    private void NotifyReportsChanged()
    {
        foreach (EventHandler subscriber in ReportsChanged?.GetInvocationList().Cast<EventHandler>() ?? [])
        {
            try { subscriber(this, EventArgs.Empty); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
    }

    private sealed record Manifest(IReadOnlyList<ManifestEntry> Entries);
    private sealed record ManifestEntry(string ReportId, string FileName, long ByteLength, string Sha256);
    private sealed record Journal(
        string? OldManifestHash, string NewManifestHash, ManifestEntry? NewArtifact,
        IReadOnlyList<ManifestEntry> RemovedEntries);
    private sealed record Artifact(byte[] Bytes, ManifestEntry Entry);
}
