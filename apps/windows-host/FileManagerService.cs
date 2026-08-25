using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal static class FileManagerProtocol
{
    public const int PageSize = 100;
    public const int MaxEnumeratedEntriesPerPanel = 4096;
    public const int MaxSelectionItems = 512;
    public const int MaxNameLength = 255;
}

internal sealed record FileManagerDrive(string Id, string Label, string DriveType, long? FreeBytes, long? TotalBytes);
internal sealed record FileManagerShortcut(string Id, string Label);
internal sealed record FileManagerEntry(
    string Id,
    string Name,
    string Kind,
    string Extension,
    long? Size,
    DateTimeOffset ModifiedUtc,
    string[] Attributes);
internal sealed record FileManagerPanelPage(
    string Panel,
    string Revision,
    string DisplayPath,
    string? ParentId,
    string? DriveId,
    string SortBy,
    bool Descending,
    int TotalCount,
    FileManagerEntry[] Entries,
    string? Continuation);
internal sealed record FileManagerSessionSnapshot(
    string SessionId,
    FileManagerDrive[] Drives,
    FileManagerShortcut[] Shortcuts,
    FileManagerPanelPage Left,
    FileManagerPanelPage Right);
internal sealed record FileManagerProperties(
    string EntryId,
    string Name,
    string FullPath,
    string Kind,
    string Extension,
    long? Size,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    DateTimeOffset AccessedUtc,
    string[] Attributes);
internal sealed record FileManagerSelection(bool All, string[] EntryIds, string[] ExcludedEntryIds);
internal sealed class FileTransferDownloadSource(string name, long size, FileStream stream) : IAsyncDisposable
{
    public string Name { get; } = name;
    public long Size { get; } = size;
    public FileStream Stream { get; } = stream;
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
internal delegate Task FileUploadReceiver(Stream destination, Action<long> committed, CancellationToken cancellationToken);
internal sealed record FileUploadAdmission(FileJobSnapshot Snapshot, Task<FileJobSnapshot> Completion);

internal enum FileJobState
{
    Queued,
    Preparing,
    Running,
    Paused,
    NeedsAttention,
    Canceling,
    Completed,
    Failed,
    Canceled,
    Interrupted
}

internal sealed record FileJobSnapshot(
    string JobId,
    string Operation,
    string State,
    int QueuePosition,
    int ItemsCompleted,
    int ItemsTotal,
    long BytesCompleted,
    long BytesTotal,
    double? BytesPerSecond,
    int? EtaSeconds,
    string? CurrentName,
    string? Message,
    string? ConflictName,
    bool CanPause,
    bool CanResume,
    bool CanCancel);

internal interface IFileManagerPlatform
{
    (bool Succeeded, string? Code, string Message) SetFileClipboard(IReadOnlyList<string> paths, bool move);
    (bool Succeeded, string[] Paths, bool Move, string? Code, string Message) GetFileClipboard();
    void ClearFileClipboardIfMatches(IReadOnlyList<string> paths);
    (bool Succeeded, string? Code, string Message) OpenWithShell(string path);
    bool CanRecycle(string path);
    void Recycle(string path);
}

internal interface IFileManagerLocationStore
{
    string? Load(string clientId, string panel);
    void Save(string clientId, string panel, string path);
}

internal sealed class RegistryFileManagerLocationStore : IFileManagerLocationStore
{
    private static string ValueName(string clientId, string panel) => $"FilePanel_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId)))[..24]}_{panel}";

    public string? Load(string clientId, string panel)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
            return key?.GetValue(ValueName(clientId, panel)) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public void Save(string clientId, string panel, string path)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
            key.SetValue(ValueName(clientId, panel), path, RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
        }
    }
}

internal sealed record FileJobBackupEntry(string DestinationPath, string BackupPath);
internal sealed record FileJobJournalEntry(string JobId, string ClientId, string Operation, string[] TemporaryPaths, FileJobBackupEntry[]? Backups = null);

internal interface IFileJobJournal
{
    FileJobJournalEntry[] Load();
    bool Save(FileJobJournalEntry[] entries);
}

internal sealed class LocalFileJobJournal : IFileJobJournal
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura Air", "file-jobs.json");

    public FileJobJournalEntry[] Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<FileJobJournalEntry[]>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public bool Save(FileJobJournalEntry[] entries)
    {
        var temporary = $"{_path}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, entries);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or SecurityException) { }
            return false;
        }
    }
}

internal sealed class WindowsFileManagerPlatform : IFileManagerPlatform
{
    private const string PreferredDropEffect = "Preferred DropEffect";

    public (bool Succeeded, string? Code, string Message) SetFileClipboard(IReadOnlyList<string> paths, bool move) =>
        InvokeClipboard(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, paths.ToArray());
            data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)));
            System.Windows.Clipboard.SetDataObject(data, true);
            return (true, null, move ? "Items cut on the PC." : "Items copied on the PC.");
        });

    public (bool Succeeded, string[] Paths, bool Move, string? Code, string Message) GetFileClipboard()
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return (false, [], false, "clipboard-unavailable", "The Windows file clipboard is unavailable.");
        }

        try
        {
            return application.Dispatcher.Invoke(() =>
            {
                if (!System.Windows.Clipboard.ContainsFileDropList())
                {
                    return (false, Array.Empty<string>(), false, "clipboard-empty", "The PC clipboard does not contain files.");
                }

                var data = System.Windows.Clipboard.GetDataObject();
                var move = false;
                if (data?.GetDataPresent(PreferredDropEffect) == true)
                {
                    var bytes = data.GetData(PreferredDropEffect) switch
                    {
                        MemoryStream stream => stream.ToArray(),
                        byte[] value => value,
                        _ => []
                    };
                    move = bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == 2;
                }

                var paths = System.Windows.Clipboard.GetFileDropList().Cast<string>()
                    .Where(path => File.Exists(path) || Directory.Exists(path))
                    .Take(FileManagerProtocol.MaxSelectionItems)
                    .ToArray();
                return paths.Length == 0
                    ? (false, Array.Empty<string>(), move, "clipboard-empty", "The PC clipboard does not contain available files.")
                    : (true, paths, move, null, $"{paths.Length} item{(paths.Length == 1 ? string.Empty : "s")} ready to paste.");
            });
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            return (false, [], false, "clipboard-unavailable", "The Windows file clipboard is busy. Try again.");
        }
    }

    public void ClearFileClipboardIfMatches(IReadOnlyList<string> paths)
    {
        var application = System.Windows.Application.Current;
        if (application is null) return;
        try
        {
            application.Dispatcher.Invoke(() =>
            {
                if (!System.Windows.Clipboard.ContainsFileDropList()) return;
                var current = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToArray();
                if (current.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)) System.Windows.Clipboard.Clear();
            });
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
        }
    }

    public (bool Succeeded, string? Code, string Message) OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return (true, null, "Opened on the PC.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return (false, "shell-open-failed", "Windows could not open the selected item.");
        }
    }

    public bool CanRecycle(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return false;
            var type = new DriveInfo(root).DriveType;
            return type is DriveType.Fixed or DriveType.Removable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public void Recycle(string path)
    {
        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        else
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
    }

    private static (bool Succeeded, string? Code, string Message) InvokeClipboard(
        Func<(bool Succeeded, string? Code, string Message)> action)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return (false, "clipboard-unavailable", "The Windows file clipboard is unavailable.");
        }

        try
        {
            return application.Dispatcher.Invoke(action);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            return (false, "clipboard-unavailable", "The Windows file clipboard is busy. Try again.");
        }
    }
}

internal sealed class FileManagerService : IAsyncDisposable
{
    private sealed class PanelState(string name, string path)
    {
        public string Name { get; } = name;
        public string Path { get; set; } = path;
        public string Revision { get; set; } = string.Empty;
        public List<EntryState> Entries { get; set; } = [];
        public Dictionary<string, int> Continuations { get; } = new(StringComparer.Ordinal);
        public string SortBy { get; set; } = "name";
        public bool Descending { get; set; }
        public string Signature { get; set; } = string.Empty;
    }

    private sealed record EntryState(string Id, string Path, FileManagerEntry Value);
    private sealed class ClientSession(string clientId, string id, PanelState left, PanelState right)
    {
        public string ClientId { get; } = clientId;
        public string Id { get; } = id;
        public PanelState Left { get; } = left;
        public PanelState Right { get; } = right;
        public Dictionary<string, string> Targets { get; } = new(StringComparer.Ordinal);
        public object Gate { get; } = new();
    }

    private sealed class ClientAuthorizationState
    {
        public object Gate { get; } = new();
        public long Generation { get; set; }
    }

    private sealed record FileUploadWork(string DestinationSignature, string Name, long Size, FileUploadReceiver Receive);

    private sealed class FileJob(long sequence, string ownerClientId, long authorizationGeneration, string operation, string[] sources, string? destination, string? rename, bool clearClipboard, FileUploadWork? upload = null)
    {
        public long Sequence { get; set; } = sequence;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string OwnerClientId { get; } = ownerClientId;
        public long AuthorizationGeneration { get; } = authorizationGeneration;
        public string Operation { get; } = operation;
        public string[] Sources { get; } = sources;
        public string? Destination { get; } = destination;
        public string? Rename { get; } = rename;
        public bool ClearClipboard { get; } = clearClipboard;
        public FileUploadWork? Upload { get; } = upload;
        public CancellationTokenSource Cancellation { get; } = new();
        public AsyncPauseGate PauseGate { get; } = new();
        public FileJobState State { get; set; } = FileJobState.Queued;
        public FileJobState ResumeState { get; set; } = FileJobState.Running;
        public int ItemsCompleted { get; set; }
        public int ItemsTotal { get; set; }
        public long BytesCompleted { get; set; }
        public long BytesTotal { get; set; }
        public string? CurrentName { get; set; }
        public string? Message { get; set; }
        public string? ConflictName { get; set; }
        public string? ApplyAllResolution { get; set; }
        public TaskCompletionSource<string>? Conflict { get; set; }
        public Stopwatch Speed { get; } = new();
        public ConcurrentDictionary<string, byte> TemporaryPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, string> BackupPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, long> PreparedSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public TaskCompletionSource<FileJobSnapshot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AsyncPauseGate
    {
        private volatile TaskCompletionSource<bool>? _paused;
        public bool IsPaused => _paused is not null;
        public void Pause() => Interlocked.CompareExchange(ref _paused, new(TaskCreationOptions.RunContinuationsAsynchronously), null);
        public void Resume() => Interlocked.Exchange(ref _paused, null)?.TrySetResult(true);
        public async Task WaitAsync(CancellationToken token)
        {
            var paused = _paused;
            if (paused is not null) await paused.Task.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private readonly IFileManagerPlatform _platform;
    private readonly IFileManagerLocationStore _locations;
    private readonly IFileJobJournal _journal;
    private readonly Func<string, bool> _hideProtectedItems;
    private readonly Action<string, string, bool> _movePath;
    private readonly Func<string, bool> _deleteTemporary;
    private readonly string? _initialLeftPath;
    private readonly string? _initialRightPath;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ClientAuthorizationState> _authorizations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileJobSnapshot[]> _interruptedJobs = new(StringComparer.Ordinal);
    private readonly Lock _queueGate = new();
    private readonly Lock _journalGate = new();
    private FileJobJournalEntry[] _unresolvedRecoveryEntries = [];
    private readonly List<FileJob> _pendingJobs = [];
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private readonly Channel<(string ClientId, string Panel, string Path)> _locationUpdates = Channel.CreateBounded<(string, string, string)>(new BoundedChannelOptions(16)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _locationWorker;
    private readonly Channel<bool> _journalUpdates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _journalWorker;
    private long _jobSequence;
    private int _disposeStarted;

    public FileManagerService(IFileManagerPlatform? platform = null, string? initialLeftPath = null, string? initialRightPath = null, IFileManagerLocationStore? locations = null, IFileJobJournal? journal = null, Func<string, bool>? hideProtectedItems = null, Action<string, string, bool>? movePath = null, Func<string, bool>? deleteTemporary = null)
    {
        _platform = platform ?? new WindowsFileManagerPlatform();
        _locations = locations ?? new RegistryFileManagerLocationStore();
        _journal = journal ?? new LocalFileJobJournal();
        _hideProtectedItems = hideProtectedItems ?? (_ => true);
        _movePath = movePath ?? MovePath;
        _deleteTemporary = deleteTemporary ?? TryDeleteTemporary;
        _initialLeftPath = initialLeftPath;
        _initialRightPath = initialRightPath;
        _worker = Task.Run(ProcessJobsAsync);
        _locationWorker = Task.Run(ProcessLocationUpdatesAsync);
        RecoverInterruptedJobs();
        _journalWorker = Task.Run(ProcessJournalUpdatesAsync);
    }

    public event EventHandler<string>? JobChanged;
    public string[] SessionClientIds => [.. _sessions.Keys];

    public FileManagerSessionSnapshot OpenSession(string clientId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hideProtectedItems = _hideProtectedItems(clientId);
        var savedLeft = _locations.Load(clientId, "left");
        var savedRight = _locations.Load(clientId, "right");
        cancellationToken.ThrowIfCancellationRequested();
        var leftPath = FirstValidDirectory(savedLeft, _initialLeftPath) ?? GetInitialDirectory(Environment.SpecialFolder.UserProfile, "Downloads");
        var rightPath = FirstValidDirectory(savedRight, _initialRightPath) ?? GetInitialDirectory(Environment.SpecialFolder.MyDocuments, null);
        if (hideProtectedItems && IsProtectedSystemItem(leftPath)) leftPath = GetInitialDirectory(Environment.SpecialFolder.UserProfile, "Downloads");
        if (hideProtectedItems && IsProtectedSystemItem(rightPath)) rightPath = GetInitialDirectory(Environment.SpecialFolder.MyDocuments, null);
        var session = new ClientSession(
            clientId,
            Guid.NewGuid().ToString("N"),
            new PanelState("left", leftPath),
            new PanelState("right", rightPath));
        AddTargets(session);
        cancellationToken.ThrowIfCancellationRequested();
        RefreshPanel(session.Left, hideProtectedItems, CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        RefreshPanel(session.Right, hideProtectedItems, CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new FileManagerSessionSnapshot(
            session.Id,
            GetDrives(session),
            GetShortcuts(session),
            BuildPage(session, session.Left, 0),
            BuildPage(session, session.Right, 0));
        cancellationToken.ThrowIfCancellationRequested();
        _sessions[clientId] = session;
        return snapshot;
    }

    public bool TryGetPage(string clientId, string sessionId, string panelName, string revision, string continuation, out FileManagerPanelPage? page, out string code)
    {
        page = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            if (panel.Revision != revision || !panel.Continuations.Remove(continuation, out var offset)) { code = "stale-panel"; return false; }
            page = BuildPage(session, panel, offset);
            code = "accepted";
            return true;
        }
    }

    public bool TryNavigate(string clientId, string sessionId, string panelName, string revision, string targetId, out FileManagerPanelPage? page, out string code, CancellationToken cancellationToken = default)
    {
        page = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            var hideProtectedItems = _hideProtectedItems(clientId);
            if (panel.Revision != revision) { code = "stale-panel"; return false; }
            if (!MatchesPanel(panel, hideProtectedItems, cancellationToken)) { code = "stale-panel"; return false; }
            string? target = null;
            if (targetId == "parent") target = Directory.GetParent(panel.Path)?.FullName;
            else if (session.Targets.TryGetValue(targetId, out var known)) target = known;
            else target = panel.Entries.FirstOrDefault(entry => entry.Id == targetId && entry.Value.Kind == "folder")?.Path;
            if (target is null || !Directory.Exists(target)) { code = "target-unavailable"; return false; }
            string targetPath;
            List<EntryState> targetEntries;
            try
            {
                targetPath = Path.GetFullPath(target);
                targetEntries = ReadPanelEntries(targetPath, hideProtectedItems, cancellationToken);
            }
            catch (Exception ex) when (IsFileBoundaryFailure(ex))
            {
                code = ex is UnauthorizedAccessException or SecurityException ? "access-denied" : "directory-unavailable";
                return false;
            }
            panel.Path = targetPath;
            ReplacePanelEntries(panel, targetEntries);
            _locationUpdates.Writer.TryWrite((clientId, panel.Name, panel.Path));
            page = BuildPage(session, panel, 0);
            code = "accepted";
            return true;
        }
    }

    public bool TryRefresh(string clientId, string sessionId, string panelName, out FileManagerPanelPage? page, out string code, CancellationToken cancellationToken = default)
    {
        page = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            try
            {
                RefreshPanel(panel, _hideProtectedItems(clientId), cancellationToken);
                page = BuildPage(session, panel, 0);
                code = "accepted";
                return true;
            }
            catch (Exception ex) when (IsFileBoundaryFailure(ex))
            {
                code = "directory-unavailable";
                return false;
            }
        }
    }

    public bool TrySort(string clientId, string sessionId, string panelName, string sortBy, bool descending, out FileManagerPanelPage? page, out string code)
    {
        page = null;
        if (sortBy is not ("name" or "size" or "type" or "modified")) { code = "invalid-sort"; return false; }
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            panel.SortBy = sortBy;
            panel.Descending = descending;
            SortPanel(panel);
            panel.Revision = Guid.NewGuid().ToString("N");
            panel.Continuations.Clear();
            page = BuildPage(session, panel, 0);
            code = "accepted";
            return true;
        }
    }

    public bool TryGetProperties(string clientId, string sessionId, string panelName, string revision, string entryId, out FileManagerProperties? properties, out string code)
    {
        properties = null;
        if (entryId == "current")
        {
            if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
            lock (session.Gate)
            {
                if (panel.Revision != revision || !MatchesPanel(panel, _hideProtectedItems(clientId))) { code = "stale-panel"; return false; }
                return TryBuildProperties(entryId, panel.Path, "folder", string.Empty, out properties, out code);
            }
        }
        if (!TryResolveEntry(clientId, sessionId, panelName, revision, entryId, out var entry, out code)) return false;
        return TryBuildProperties(entry!.Id, entry.Path, entry.Value.Kind, entry.Value.Extension, out properties, out code);
    }

    private static bool TryBuildProperties(string entryId, string path, string kind, string extension, out FileManagerProperties? properties, out string code)
    {
        properties = null;
        try
        {
            FileSystemInfo info = kind == "folder" ? new DirectoryInfo(path) : new FileInfo(path);
            properties = new FileManagerProperties(
                entryId,
                string.IsNullOrEmpty(info.Name) ? info.FullName : info.Name,
                info.FullName,
                kind,
                extension,
                info is FileInfo file ? file.Length : null,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                info.LastAccessTimeUtc,
                ToAttributes(info.Attributes));
            code = "accepted";
            return true;
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            code = "entry-unavailable";
            return false;
        }
    }

    public (bool Succeeded, string? Code, string Message) SetClipboard(string clientId, string sessionId, string panelName, string revision, FileManagerSelection selection, bool move)
    {
        var authorizationGeneration = CaptureAuthorizationGeneration(clientId);
        if (!TryResolveSelection(clientId, sessionId, panelName, revision, selection, out var paths, out var code))
            return (false, code, code == "stale-panel" ? "The folder changed. Refresh it and try again." : "The selection is unavailable.");
        (bool Succeeded, string? Code, string Message) result = default;
        return TryRunAuthorized(clientId, authorizationGeneration, () => result = _platform.SetFileClipboard(paths, move))
            ? result
            : (false, "permission-revoked", "File permission was revoked on the PC.");
    }

    public (bool Succeeded, string? Code, string Message) Open(string clientId, string sessionId, string panelName, string revision, string entryId)
    {
        var authorizationGeneration = CaptureAuthorizationGeneration(clientId);
        if (!TryResolveEntry(clientId, sessionId, panelName, revision, entryId, out var entry, out var code))
            return (false, code, "The selected item is unavailable.");
        (bool Succeeded, string? Code, string Message) result = default;
        return TryRunAuthorized(clientId, authorizationGeneration, () => result = _platform.OpenWithShell(entry!.Path))
            ? result
            : (false, "permission-revoked", "File permission was revoked on the PC.");
    }

    public (bool Succeeded, string? Code, string Message, FileTransferDownloadSource? Source) OpenDownload(
        string clientId,
        string sessionId,
        string panelName,
        string revision,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeStarted) != 0) return (false, "host-stopped", "Files is stopping.", null);
        if (!TryResolveEntry(clientId, sessionId, panelName, revision, entryId, out var entry, out var code, cancellationToken) || entry!.Value.Kind != "file")
            return (false, code, "Select one available PC file.", null);
        cancellationToken.ThrowIfCancellationRequested();
        FileStream? stream = null;
        try
        {
            stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileTransferProtocol.MaximumPayloadBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Length > FileTransferProtocol.MaximumSafeFileSize)
            {
                return (false, "file-too-large", "This file is too large for the transfer protocol.", null);
            }
            var source = new FileTransferDownloadSource(Path.GetFileName(entry.Path), stream.Length, stream);
            stream = null;
            return (true, null, "File ready.", source);
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            return (false, IsAccessDenied(ex) ? "access-denied" : "file-unavailable", "The selected file is unavailable.", null);
        }
        finally { stream?.Dispose(); }
    }

    public (bool Succeeded, string? Code, string Message, FileUploadAdmission? Admission) CreateUploadJob(
        string clientId,
        string sessionId,
        string panelName,
        string revision,
        string fileName,
        long declaredSize,
        FileUploadReceiver receive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receive);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeStarted) != 0) return (false, "host-stopped", "Files is stopping.", null);
        if (declaredSize is < 0 or > FileTransferProtocol.MaximumSafeFileSize)
            return (false, "invalid-size", "The selected file size is invalid.", null);
        if (!IsValidName(fileName)) return (false, "invalid-name", "Enter one valid Windows file name.", null);
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel))
            return (false, "session-expired", "Files must be reopened.", null);
        string destination;
        string panelSignature;
        lock (session.Gate)
        {
            if (panel.Revision != revision || !MatchesPanel(panel, _hideProtectedItems(clientId), cancellationToken))
                return (false, "stale-panel", "The folder changed. Refresh it and try again.", null);
            destination = panel.Path;
            panelSignature = panel.Signature;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasAvailableSpace(destination, declaredSize))
            return (false, "insufficient-space", "The PC does not have enough free space for this file.", null);
        cancellationToken.ThrowIfCancellationRequested();

        var authorizationGeneration = CaptureAuthorizationGeneration(clientId);
        var upload = new FileUploadWork(panelSignature, fileName, declaredSize, receive);
        var job = new FileJob(Interlocked.Increment(ref _jobSequence), clientId, authorizationGeneration, "upload", [], destination, fileName, false, upload);
        var admitted = AdmitJob(job, cancellationToken);
        if (!admitted.Succeeded) return (false, admitted.Code, admitted.Message, null);
        var snapshot = Snapshot(job);
        return (true, null, "Upload queued.", new FileUploadAdmission(snapshot, job.Completion.Task));
    }

    public (bool Succeeded, string? Code, string Message, FileJobSnapshot? Job) CreateJob(
        string clientId,
        string sessionId,
        string sourcePanel,
        string revision,
        FileManagerSelection selection,
        string operation,
        string? destinationPanel,
        string? newName,
        string? destinationRevision)
    {
        var authorizationGeneration = CaptureAuthorizationGeneration(clientId);
        string[] paths;
        string? destination = null;
        var clearClipboard = false;
        if (operation == "paste")
        {
            if (!TryGetPanel(clientId, sessionId, sourcePanel, out var session, out var panel)) return (false, "session-expired", "Files must be reopened.", null);
            lock (session.Gate)
            {
                if (panel.Revision != revision || !MatchesPanel(panel, _hideProtectedItems(clientId)))
                    return (false, "stale-panel", "The folder changed. Refresh it and try again.", null);
                destination = panel.Path;
            }
            var clipboard = _platform.GetFileClipboard();
            if (!clipboard.Succeeded) return (false, clipboard.Code, clipboard.Message, null);
            paths = clipboard.Paths;
            operation = clipboard.Move ? "move" : "copy";
            clearClipboard = clipboard.Move;
        }
        else
        {
            if (operation is "copy" or "move")
            {
                if (string.IsNullOrEmpty(destinationPanel) || string.IsNullOrEmpty(destinationRevision) ||
                    !TryGetPanel(clientId, sessionId, sourcePanel, out var session, out var sourceState) ||
                    !TryGetPanel(clientId, sessionId, destinationPanel, out var destinationSession, out var destinationState) ||
                    !ReferenceEquals(session, destinationSession))
                    return (false, "destination-unavailable", "The destination panel is unavailable.", null);
                lock (session.Gate)
                {
                    if (sourceState.Revision != revision || destinationState.Revision != destinationRevision ||
                        !MatchesPanel(sourceState, _hideProtectedItems(clientId)) || !MatchesPanel(destinationState, _hideProtectedItems(clientId)))
                        return (false, "stale-panel", "A folder changed. Refresh it and try again.", null);
                    var selected = selection.All
                        ? sourceState.Entries.Where(entry => !selection.ExcludedEntryIds.Contains(entry.Id, StringComparer.Ordinal))
                        : sourceState.Entries.Where(entry => selection.EntryIds.Contains(entry.Id, StringComparer.Ordinal));
                    paths = [.. selected.Select(entry => entry.Path)];
                    destination = destinationState.Path;
                }
            }
            else if (!TryResolveSelection(clientId, sessionId, sourcePanel, revision, selection, out paths, out var code))
                return (false, code, "The folder changed. Refresh it and try again.", null);
        }

        if (operation == "rename")
        {
            if (paths.Length != 1 || !IsValidName(newName)) return (false, "invalid-name", "Enter one valid Windows file name.", null);
            if (string.Equals(Path.GetFileName(paths[0]), newName, StringComparison.Ordinal)) return (false, "invalid-name", "Enter a different file name.", null);
        }
        if (operation == "delete" && paths.Any(path => !_platform.CanRecycle(path)))
            return (false, "cannot-recycle", "Every selected item must support the Windows Recycle Bin.", null);
        if (paths.Length == 0) return (false, "selection-empty", "Select at least one item.", null);
        if (operation is "copy" or "move" && destination is not null && paths.Any(source => IsUnsafeDestination(source, destination)))
            return (false, "invalid-destination", "Choose a destination outside the selected items.", null);

        var job = new FileJob(Interlocked.Increment(ref _jobSequence), clientId, authorizationGeneration, operation, paths, destination, newName, clearClipboard);
        var admitted = AdmitJob(job);
        if (!admitted.Succeeded) return (false, admitted.Code, admitted.Message, null);
        return (true, null, "File operation queued.", Snapshot(job));
    }

    private (bool Succeeded, string? Code, string Message) AdmitJob(FileJob job, CancellationToken cancellationToken = default)
    {
        lock (GetAuthorizationState(job.OwnerClientId).Gate)
        {
            if (GetAuthorizationState(job.OwnerClientId).Generation != job.AuthorizationGeneration)
            {
                job.Cancellation.Dispose();
                return (false, "permission-revoked", "File permission was revoked on the PC.");
            }
            lock (_queueGate)
            {
                if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposeStarted) != 0)
                {
                    job.Cancellation.Dispose();
                    return (false, "host-stopped", "Files is stopping.");
                }
                if (_jobs.Values.Count(candidate => !IsTerminalJob(candidate.State)) >= 32)
                {
                    job.Cancellation.Dispose();
                    return (false, "queue-full", "The file-operation queue is full. Try again later.");
                }
                _jobs[job.Id] = job;
                _pendingJobs.Add(job);
            }
        }
        _queueSignal.Release();
        Publish(job);
        return (true, null, "File operation queued.");
    }

    public bool ControlJob(string clientId, string jobId, string action)
    {
        if (action == "dismiss")
        {
            if (_jobs.TryGetValue(jobId, out var terminalJob) && terminalJob.OwnerClientId == clientId &&
                terminalJob.State is FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled &&
                TryDetachRecoveryArtifacts(terminalJob) &&
                _jobs.TryRemove(jobId, out var removedJob))
            {
                removedJob.Cancellation.Dispose();
                JobChanged?.Invoke(this, clientId);
                return true;
            }
            return DismissInterruptedJob(clientId, jobId);
        }
        if (!_jobs.TryGetValue(jobId, out var job) || job.OwnerClientId != clientId) return false;
        if (action == "pause" && job.Upload is null && job.State is FileJobState.Running or FileJobState.Preparing)
        {
            job.ResumeState = job.State;
            job.PauseGate.Pause();
            job.State = FileJobState.Paused;
        }
        else if (action == "resume" && job.State == FileJobState.Paused)
        {
            job.PauseGate.Resume();
            job.State = job.ResumeState;
        }
        else if (action == "cancel" && job.State is not (FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled))
        {
            var canceledQueued = false;
            lock (_queueGate)
            {
                if (job.State == FileJobState.Queued && _pendingJobs.Remove(job))
                {
                    job.State = FileJobState.Canceled;
                    job.Message = "File operation canceled.";
                    canceledQueued = true;
                }
            }
            if (canceledQueued)
            {
                PruneTerminalJobs(job.OwnerClientId);
                Publish(job);
                job.Completion.TrySetResult(Snapshot(job));
                return true;
            }
            job.State = FileJobState.Canceling;
            job.PauseGate.Resume();
            job.Conflict?.TrySetResult("cancel");
            _ = job.Cancellation.CancelAsync();
        }
        else return false;
        Publish(job);
        return true;
    }

    private bool DismissInterruptedJob(string clientId, string jobId)
    {
        while (_interruptedJobs.TryGetValue(clientId, out var current))
        {
            var remaining = current.Where(job => job.JobId != jobId).ToArray();
            if (remaining.Length == current.Length) return false;
            if (!_interruptedJobs.TryUpdate(clientId, remaining, current)) continue;
            if (remaining.Length == 0) _interruptedJobs.TryRemove(clientId, out _);
            JobChanged?.Invoke(this, clientId);
            return true;
        }
        return false;
    }

    public bool ResolveConflict(string clientId, string jobId, string resolution, bool applyToAll)
    {
        if (!_jobs.TryGetValue(jobId, out var job) || job.OwnerClientId != clientId || job.State != FileJobState.NeedsAttention) return false;
        if (job.Upload is null && resolution is not ("replace" or "skip" or "cancel") ||
            job.Upload is not null && (resolution is not ("replace" or "keep-both" or "cancel") || applyToAll)) return false;
        if (applyToAll) job.ApplyAllResolution = resolution;
        job.Conflict?.TrySetResult(resolution);
        return true;
    }

    public bool ReorderJob(string clientId, string jobId, string direction)
    {
        if (direction is not ("up" or "down")) return false;
        lock (_queueGate)
        {
            var queued = _pendingJobs.Where(job => job.State == FileJobState.Queued).OrderBy(job => job.Sequence).ToList();
            var index = queued.FindIndex(job => job.Id == jobId && job.OwnerClientId == clientId);
            var otherIndex = direction == "up" ? index - 1 : index + 1;
            if (index < 0 || otherIndex < 0 || otherIndex >= queued.Count || queued[otherIndex].OwnerClientId != clientId) return false;
            (queued[index].Sequence, queued[otherIndex].Sequence) = (queued[otherIndex].Sequence, queued[index].Sequence);
            Publish(queued[index]);
            Publish(queued[otherIndex]);
            return true;
        }
    }

    public FileJobSnapshot[] GetJobs(string clientId)
    {
        var owned = _jobs.Values.Where(job => job.OwnerClientId == clientId).Select(job => (Job: job, State: job.State)).ToArray();
        var active = owned.Where(item => !IsTerminalJob(item.State)).OrderBy(item => item.Job.Sequence).Select(item => Snapshot(item.Job));
        var terminal = owned.Where(item => IsTerminalJob(item.State)).OrderByDescending(item => item.Job.Sequence).Select(item => Snapshot(item.Job));
        return [.. active.Concat(terminal).Concat(_interruptedJobs.TryGetValue(clientId, out var interrupted) ? interrupted : []).Take(32)];
    }

    public void RevokeClient(string clientId, bool closeSession)
    {
        if (closeSession) _sessions.TryRemove(clientId, out _);

        var authorization = GetAuthorizationState(clientId);
        lock (authorization.Gate)
        {
            authorization.Generation++;
        }

        foreach (var job in _jobs.Values.Where(candidate => candidate.OwnerClientId == clientId && candidate.State is not (FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled)))
        {
            ControlJob(clientId, job.Id, "cancel");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        _locationUpdates.Writer.TryComplete();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _queueSignal.Release();
        foreach (var job in _jobs.Values)
        {
            job.PauseGate.Resume();
            job.Conflict?.TrySetResult("cancel");
            await job.Cancellation.CancelAsync().ConfigureAwait(false);
        }
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        try { await _locationWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _journalUpdates.Writer.TryComplete();
        try { await _journalWorker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        foreach (var job in _jobs.Values) job.Cancellation.Dispose();
        _queueSignal.Dispose();
        _lifetime.Dispose();
    }

    private async Task ProcessJobsAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            await _queueSignal.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            FileJob? job;
            lock (_queueGate)
            {
                job = _pendingJobs.OrderBy(candidate => candidate.Sequence).FirstOrDefault();
                if (job is not null) _pendingJobs.Remove(job);
            }
            if (job is null) continue;
            try
            {
                job.State = FileJobState.Preparing;
                job.ItemsTotal = job.Upload is null ? job.Sources.Length : 1;
                if (job.Upload is not null) job.BytesTotal = job.Upload.Size;
                if (job.Operation is "copy" or "move")
                {
                    foreach (var source in job.Sources)
                    {
                        var size = await CalculateSizeAsync(job, source).ConfigureAwait(false);
                        job.PreparedSizes[source] = size;
                        job.BytesTotal += size;
                        Publish(job);
                    }
                }
                job.Speed.Restart();
                Publish(job);
                job.State = FileJobState.Running;
                Publish(job);
                await ExecuteJobAsync(job).ConfigureAwait(false);
                if (job.ClearClipboard)
                {
                    TryRunAuthorized(job.OwnerClientId, job.AuthorizationGeneration, () => _platform.ClearFileClipboardIfMatches(job.Sources));
                }
                job.State = FileJobState.Completed;
                job.Message = $"{job.Operation} completed.";
            }
            catch (OperationCanceledException)
            {
                job.State = FileJobState.Canceled;
                job.Message = "File operation canceled.";
            }
            catch (Exception ex) when (IsFileBoundaryFailure(ex))
            {
                job.State = FileJobState.Failed;
                job.Message = IsAccessDenied(ex)
                    ? "Windows denied access. Your PC account does not have permission to change an item or destination."
                    : "A file operation failed because an item or destination was unavailable.";
            }
            finally
            {
                job.Speed.Stop();
                SaveJournalSnapshot();
                if (IsTerminalJob(job.State)) PruneTerminalJobs(job.OwnerClientId);
                Publish(job);
                job.Completion.TrySetResult(Snapshot(job));
            }
        }
    }

    private async Task ProcessLocationUpdatesAsync()
    {
        await foreach (var update in _locationUpdates.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            _locations.Save(update.ClientId, update.Panel, update.Path);
        }
    }

    private async Task ExecuteJobAsync(FileJob job)
    {
        if (job.Upload is not null)
        {
            await ExecuteUploadAsync(job, job.Upload).ConfigureAwait(false);
            return;
        }
        if (job.Operation == "delete")
        {
            foreach (var source in job.Sources)
            {
                await AwaitReadyAsync(job).ConfigureAwait(false);
                job.CurrentName = Path.GetFileName(source);
                Publish(job);
                ThrowIfAuthorizationRevoked(job);
                ExecuteAuthorized(job, () => _platform.Recycle(source));
                job.ItemsCompleted++;
            }
            return;
        }
        if (job.Operation == "rename")
        {
            var source = job.Sources[0];
            var destination = Path.Combine(Path.GetDirectoryName(source)!, job.Rename!);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, destination, StringComparison.Ordinal))
            {
                await AwaitReadyAsync(job).ConfigureAwait(false);
                ExecuteAuthorized(job, () => CommitCaseOnlyRename(job, source, destination, Directory.Exists(source)));
                job.ItemsCompleted = 1;
                return;
            }
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                var resolution = await ResolveConflictAsync(job, Path.GetFileName(destination)).ConfigureAwait(false);
                if (resolution == "skip") return;
                if (resolution == "cancel") throw new OperationCanceledException(job.Cancellation.Token);
                await AwaitReadyAsync(job).ConfigureAwait(false);
            }
            await AwaitReadyAsync(job).ConfigureAwait(false);
            ExecuteAuthorized(job, () => CommitPreparedPath(job, source, destination, Directory.Exists(source)));
            job.ItemsCompleted = 1;
            return;
        }

        foreach (var source in job.Sources)
        {
            await AwaitReadyAsync(job).ConfigureAwait(false);
            job.CurrentName = Path.GetFileName(source);
            Publish(job);
            var destination = Path.Combine(job.Destination!, Path.GetFileName(source));
            if (job.Operation == "move" && SameVolume(source, destination) && !Directory.Exists(destination) && !File.Exists(destination))
            {
                var sourceSize = job.PreparedSizes.GetValueOrDefault(source);
                ExecuteAuthorized(job, () =>
                {
                    if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
                });
                job.BytesCompleted += sourceSize;
            }
            else
            {
                var copied = await CopyEntryAsync(job, source, destination).ConfigureAwait(false);
                if (copied && job.Operation == "move") ExecuteAuthorized(job, () => DeleteSource(source));
            }
            job.ItemsCompleted++;
            Publish(job);
        }
    }

    private async Task ExecuteUploadAsync(FileJob job, FileUploadWork upload)
    {
        await AwaitReadyAsync(job).ConfigureAwait(false);
        if (!UploadDestinationMatches(job.OwnerClientId, job.Destination!, upload, ignoredPath: null))
            throw new IOException("The upload destination changed.");
        if (!HasAvailableSpace(job.Destination!, upload.Size))
            throw new IOException("The upload destination does not have enough free space.");

        var destination = Path.Combine(job.Destination!, upload.Name);
        var replaceExisting = false;
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            var resolution = await ResolveConflictAsync(job, Path.GetFileName(destination)).ConfigureAwait(false);
            if (resolution == "cancel") throw new OperationCanceledException(job.Cancellation.Token);
            if (resolution == "keep-both") destination = CreateKeepBothPath(destination);
            else replaceExisting = true;
        }
        job.CurrentName = Path.GetFileName(destination);
        Publish(job);

        var temporary = Path.Combine(job.Destination!, $".voltura-air-{Guid.NewGuid():N}.part");
        job.TemporaryPaths[temporary] = 0;
        if (!SaveJournalSnapshot())
        {
            job.TemporaryPaths.TryRemove(temporary, out _);
            throw new IOException("The upload recovery journal could not be saved.");
        }
        try
        {
            FileStream? output = null;
            ExecuteAuthorized(job, () => output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileTransferProtocol.MaximumPayloadBytes, FileOptions.Asynchronous | FileOptions.SequentialScan));
            await using (var outputStream = output ?? throw new IOException("The upload partial file could not be opened."))
            {
                await upload.Receive(outputStream, committed =>
                {
                    if (committed < job.BytesCompleted || committed > upload.Size) throw new IOException("The upload committed an invalid byte count.");
                    job.BytesCompleted = committed;
                    Publish(job);
                }, job.Cancellation.Token).ConfigureAwait(false);
                if (job.BytesCompleted != upload.Size || outputStream.Length != upload.Size)
                    throw new IOException("The upload ended before the declared file size was received.");
                ExecuteAuthorized(job, () => outputStream.Flush(flushToDisk: true));
            }
            await AwaitReadyAsync(job).ConfigureAwait(false);
            if (!UploadDestinationMatches(job.OwnerClientId, job.Destination!, upload, temporary))
                throw new IOException("The upload destination changed before commit.");
            ExecuteAuthorized(job, () => CommitPreparedPath(job, temporary, destination, directory: false, allowReplacement: replaceExisting));
            job.ItemsCompleted = 1;
        }
        finally
        {
            if (_deleteTemporary(temporary)) job.TemporaryPaths.TryRemove(temporary, out _);
            QueueJournalWrite();
        }
    }

    private async Task<bool> CopyEntryAsync(FileJob job, string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            var resolution = await ResolveConflictAsync(job, Path.GetFileName(destination)).ConfigureAwait(false);
            if (resolution == "skip") return false;
            if (resolution == "cancel") throw new OperationCanceledException(job.Cancellation.Token);
            await AwaitReadyAsync(job).ConfigureAwait(false);
        }
        if (Directory.Exists(source))
        {
            if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Reparse-point directories cannot be copied recursively.");
            var temporaryDirectory = $"{destination}.voltura-air-{Guid.NewGuid():N}.part";
            job.TemporaryPaths[temporaryDirectory] = 0;
            if (!SaveJournalSnapshot())
            {
                job.TemporaryPaths.TryRemove(temporaryDirectory, out _);
                throw new IOException("The partial-copy recovery journal could not be saved.");
            }
            ExecuteAuthorized(job, () => Directory.CreateDirectory(temporaryDirectory));
            try
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(source))
                {
                    await AwaitReadyAsync(job).ConfigureAwait(false);
                    await CopyEntryAsync(job, child, Path.Combine(temporaryDirectory, Path.GetFileName(child))).ConfigureAwait(false);
                }
                ExecuteAuthorized(job, () =>
                {
                    Directory.SetLastWriteTimeUtc(temporaryDirectory, Directory.GetLastWriteTimeUtc(source));
                    File.SetAttributes(temporaryDirectory, File.GetAttributes(source));
                });
                await AwaitReadyAsync(job).ConfigureAwait(false);
                ExecuteAuthorized(job, () => CommitPreparedPath(job, temporaryDirectory, destination, directory: true));
                return true;
            }
            finally
            {
                if (_deleteTemporary(temporaryDirectory)) job.TemporaryPaths.TryRemove(temporaryDirectory, out _);
                QueueJournalWrite();
            }
        }

        var temporary = $"{destination}.voltura-air-{Guid.NewGuid():N}.part";
        job.TemporaryPaths[temporary] = 0;
        if (!SaveJournalSnapshot())
        {
            job.TemporaryPaths.TryRemove(temporary, out _);
            throw new IOException("The partial-copy recovery journal could not be saved.");
        }
        ExecuteAuthorized(job, () => Directory.CreateDirectory(Path.GetDirectoryName(destination)!));
        try
        {
            const int bufferSize = 1024 * 1024;
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            FileStream? output = null;
            ExecuteAuthorized(job, () => output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan));
            var outputStream = output ?? throw new IOException("The temporary file could not be opened.");
            await using (outputStream)
            {
                var buffer = new byte[bufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, job.Cancellation.Token).ConfigureAwait(false)) > 0)
                {
                    await AwaitReadyAsync(job).ConfigureAwait(false);
                    await outputStream.WriteAsync(buffer.AsMemory(0, read), job.Cancellation.Token).ConfigureAwait(false);
                    job.BytesCompleted += read;
                    Publish(job);
                }
                await outputStream.FlushAsync(job.Cancellation.Token).ConfigureAwait(false);
            }
            await AwaitReadyAsync(job).ConfigureAwait(false);
            ExecuteAuthorized(job, () => CommitPreparedPath(job, temporary, destination, directory: false));
            ExecuteAuthorized(job, () =>
            {
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
                File.SetAttributes(destination, File.GetAttributes(source));
            });
            return true;
        }
        finally
        {
            if (_deleteTemporary(temporary)) job.TemporaryPaths.TryRemove(temporary, out _);
            QueueJournalWrite();
        }
    }

    private async Task<string> ResolveConflictAsync(FileJob job, string displayName)
    {
        if (job.ApplyAllResolution is { } remembered) return remembered;
        job.State = FileJobState.NeedsAttention;
        job.ConflictName = displayName;
        job.Conflict = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Publish(job);
        var result = await job.Conflict.Task.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
        job.Conflict = null;
        job.ConflictName = null;
        job.State = FileJobState.Running;
        Publish(job);
        return result;
    }

    private async Task AwaitReadyAsync(FileJob job)
    {
        job.Cancellation.Token.ThrowIfCancellationRequested();
        await job.PauseGate.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
        job.Cancellation.Token.ThrowIfCancellationRequested();
        ThrowIfAuthorizationRevoked(job);
        if (job.State == FileJobState.Paused) job.State = job.ResumeState;
    }

    private ClientAuthorizationState GetAuthorizationState(string clientId) =>
        _authorizations.GetOrAdd(clientId, static _ => new ClientAuthorizationState());

    private long CaptureAuthorizationGeneration(string clientId)
    {
        var authorization = GetAuthorizationState(clientId);
        lock (authorization.Gate) return authorization.Generation;
    }

    private bool TryRunAuthorized(string clientId, long generation, Action action)
    {
        var authorization = GetAuthorizationState(clientId);
        lock (authorization.Gate)
        {
            if (authorization.Generation != generation) return false;
            action();
            return true;
        }
    }

    private void ExecuteAuthorized(FileJob job, Action action)
    {
        if (!TryRunAuthorized(job.OwnerClientId, job.AuthorizationGeneration, action))
        {
            throw new OperationCanceledException(job.Cancellation.Token);
        }
    }

    private void ThrowIfAuthorizationRevoked(FileJob job)
    {
        if (CaptureAuthorizationGeneration(job.OwnerClientId) != job.AuthorizationGeneration)
        {
            throw new OperationCanceledException(job.Cancellation.Token);
        }
    }

    private void Publish(FileJob job)
    {
        QueueJournalWrite();
        JobChanged?.Invoke(this, job.OwnerClientId);
    }

    private void QueueJournalWrite() => _journalUpdates.Writer.TryWrite(true);

    private async Task ProcessJournalUpdatesAsync()
    {
        await foreach (var signal in _journalUpdates.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            _ = signal;
            await Task.Delay(50).ConfigureAwait(false);
            while (_journalUpdates.Reader.TryRead(out _)) { }
            SaveJournalSnapshot();
        }
    }

    private bool SaveJournalSnapshot()
    {
        var active = _jobs.Values
            .Where(job => !IsTerminalJob(job.State) || !job.TemporaryPaths.IsEmpty || !job.BackupPaths.IsEmpty)
            .Select(job => new FileJobJournalEntry(
                job.Id,
                job.OwnerClientId,
                job.Operation,
                [.. job.TemporaryPaths.Keys],
                [.. job.BackupPaths.Select(pair => new FileJobBackupEntry(pair.Value, pair.Key))]))
            .ToArray();
        lock (_journalGate) return _journal.Save([.. active, .. _unresolvedRecoveryEntries]);
    }

    private bool TryDetachRecoveryArtifacts(FileJob job)
    {
        if (job.TemporaryPaths.IsEmpty && job.BackupPaths.IsEmpty) return true;
        var detached = new FileJobJournalEntry(
            job.Id,
            job.OwnerClientId,
            job.Operation,
            [.. job.TemporaryPaths.Keys],
            [.. job.BackupPaths.Select(pair => new FileJobBackupEntry(pair.Value, pair.Key))]);
        lock (_journalGate)
        {
            var unresolved = _unresolvedRecoveryEntries.Append(detached).ToArray();
            var active = _jobs.Values
                .Where(candidate => candidate.Id != job.Id && (!IsTerminalJob(candidate.State) || !candidate.TemporaryPaths.IsEmpty || !candidate.BackupPaths.IsEmpty))
                .Select(candidate => new FileJobJournalEntry(
                    candidate.Id,
                    candidate.OwnerClientId,
                    candidate.Operation,
                    [.. candidate.TemporaryPaths.Keys],
                    [.. candidate.BackupPaths.Select(pair => new FileJobBackupEntry(pair.Value, pair.Key))]))
                .ToArray();
            if (!_journal.Save([.. active, .. unresolved])) return false;
            _unresolvedRecoveryEntries = unresolved;
            job.TemporaryPaths.Clear();
            job.BackupPaths.Clear();
            return true;
        }
    }

    private void RecoverInterruptedJobs()
    {
        var entries = _journal.Load();
        var unresolvedEntries = new List<FileJobJournalEntry>();
        foreach (var entry in entries)
        {
            var unresolvedBackups = new List<FileJobBackupEntry>();
            foreach (var backup in entry.Backups ?? [])
            {
                try
                {
                    if (!File.Exists(backup.BackupPath) && !Directory.Exists(backup.BackupPath)) continue;
                    if (File.Exists(backup.DestinationPath) || Directory.Exists(backup.DestinationPath))
                        DeleteExisting(backup.DestinationPath);
                    _movePath(backup.BackupPath, backup.DestinationPath, Directory.Exists(backup.BackupPath));
                }
                catch (Exception ex) when (IsFileBoundaryFailure(ex)) { unresolvedBackups.Add(backup); }
            }
            var unresolvedTemporaryPaths = new List<string>();
            foreach (var temporary in entry.TemporaryPaths)
            {
                try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); else File.Delete(temporary); } catch (Exception ex) when (IsFileBoundaryFailure(ex)) { unresolvedTemporaryPaths.Add(temporary); }
            }
            if (unresolvedBackups.Count > 0 || unresolvedTemporaryPaths.Count > 0)
                unresolvedEntries.Add(entry with { TemporaryPaths = [.. unresolvedTemporaryPaths], Backups = [.. unresolvedBackups] });
            var snapshot = new FileJobSnapshot(entry.JobId, entry.Operation, "interrupted", 0, 0, 0, 0, 0, null, null, null, "The PC restarted before this operation finished.", null, false, false, false);
            _interruptedJobs.AddOrUpdate(entry.ClientId, [snapshot], (_, current) => [.. current, snapshot]);
        }
        if (entries.Length > 0)
        {
            lock (_journalGate)
            {
                _unresolvedRecoveryEntries = [.. unresolvedEntries];
                _journal.Save(_unresolvedRecoveryEntries);
            }
        }
    }

    private FileJobSnapshot Snapshot(FileJob job)
    {
        var elapsed = job.Speed.Elapsed.TotalSeconds;
        var speed = elapsed > 0.5 ? job.BytesCompleted / elapsed : (double?)null;
        var remaining = Math.Max(0, job.BytesTotal - job.BytesCompleted);
        var eta = speed is > 0 ? (int?)Math.Ceiling(remaining / speed.Value) : null;
        var position = job.State == FileJobState.Queued
            ? _jobs.Values.Count(candidate => candidate.State == FileJobState.Queued && candidate.Sequence <= job.Sequence)
            : 0;
        return new FileJobSnapshot(
            job.Id,
            job.Operation,
            ToProtocol(job.State),
            position,
            job.ItemsCompleted,
            job.ItemsTotal,
            job.BytesCompleted,
            job.BytesTotal,
            speed,
            eta,
            job.CurrentName,
            job.Message,
            job.ConflictName,
            job.Upload is null && job.State is (FileJobState.Running or FileJobState.Preparing),
            job.Upload is null && job.State == FileJobState.Paused,
            job.State is not (FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled));
    }

    private static string ToProtocol(FileJobState state) => state switch
    {
        FileJobState.NeedsAttention => "needs-attention",
        _ => state.ToString().ToLowerInvariant()
    };

    private bool TryResolveSelection(string clientId, string sessionId, string panelName, string revision, FileManagerSelection selection, out string[] paths, out string code)
    {
        paths = [];
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            if (panel.Revision != revision) { code = "stale-panel"; return false; }
            if (!MatchesPanel(panel, _hideProtectedItems(clientId))) { code = "stale-panel"; return false; }
            var selected = selection.All
                ? panel.Entries.Where(entry => !selection.ExcludedEntryIds.Contains(entry.Id, StringComparer.Ordinal))
                : panel.Entries.Where(entry => selection.EntryIds.Contains(entry.Id, StringComparer.Ordinal));
            paths = [.. selected.Select(entry => entry.Path)];
            code = paths.Length == 0 ? "selection-empty" : "accepted";
            return paths.Length > 0;
        }
    }

    private bool TryResolveEntry(
        string clientId,
        string sessionId,
        string panelName,
        string revision,
        string entryId,
        out EntryState? entry,
        out string code,
        CancellationToken cancellationToken = default)
    {
        entry = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            if (panel.Revision != revision) { code = "stale-panel"; return false; }
            if (!MatchesPanel(panel, _hideProtectedItems(clientId), cancellationToken)) { code = "stale-panel"; return false; }
            entry = panel.Entries.FirstOrDefault(candidate => candidate.Id == entryId);
            code = entry is null ? "entry-unavailable" : "accepted";
            return entry is not null;
        }
    }

    private bool TryGetPanel(string clientId, string sessionId, string panelName, out ClientSession session, out PanelState panel)
    {
        panel = null!;
        if (!_sessions.TryGetValue(clientId, out session!) || session.Id != sessionId) return false;
        panel = panelName == "left" ? session.Left : panelName == "right" ? session.Right : null!;
        return panel is not null;
    }

    private void RefreshPanel(PanelState panel, bool hideProtectedItems, CancellationToken cancellationToken = default)
    {
        ReplacePanelEntries(panel, ReadPanelEntries(panel.Path, hideProtectedItems, cancellationToken));
    }

    private List<EntryState> ReadPanelEntries(string path, bool hideProtectedItems, CancellationToken cancellationToken = default)
    {
        var entries = new List<EntryState>();
        var inspected = 0;
        foreach (var childPath in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRecoveryArtifact(childPath)) continue;
            if (++inspected > FileManagerProtocol.MaxEnumeratedEntriesPerPanel) break;
            try
            {
                FileSystemInfo info = Directory.Exists(childPath) ? new DirectoryInfo(childPath) : new FileInfo(childPath);
                var attributes = info.Attributes;
                if (hideProtectedItems &&
                    attributes.HasFlag(FileAttributes.Hidden) &&
                    attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }
                var id = Guid.NewGuid().ToString("N");
                var file = info as FileInfo;
                entries.Add(new EntryState(id, childPath, new FileManagerEntry(
                    id,
                    info.Name,
                    file is null ? "folder" : "file",
                    file?.Extension.TrimStart('.') ?? string.Empty,
                    file?.Length,
                    info.LastWriteTimeUtc,
                    ToAttributes(attributes))));
            }
            catch (Exception ex) when (IsFileBoundaryFailure(ex)) { }
        }
        return entries;
    }

    private static bool IsProtectedSystemItem(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) && attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            return false;
        }
    }

    private static void ReplacePanelEntries(PanelState panel, List<EntryState> entries)
    {
        panel.Entries = entries;
        SortPanel(panel);
        panel.Signature = ComputeSignature(panel.Entries);
        panel.Revision = Guid.NewGuid().ToString("N");
        panel.Continuations.Clear();
    }

    private static FileManagerPanelPage BuildPage(ClientSession session, PanelState panel, int offset)
    {
        var next = offset + FileManagerProtocol.PageSize;
        string? continuation = null;
        if (next < panel.Entries.Count)
        {
            continuation = Guid.NewGuid().ToString("N");
            panel.Continuations[continuation] = next;
        }
        var drive = session.Targets.FirstOrDefault(pair => panel.Path.StartsWith(pair.Value, StringComparison.OrdinalIgnoreCase) && pair.Key.StartsWith("drive-", StringComparison.Ordinal)).Key;
        return new FileManagerPanelPage(
            panel.Name,
            panel.Revision,
            panel.Path,
            Directory.GetParent(panel.Path) is null ? null : "parent",
            string.IsNullOrEmpty(drive) ? null : drive,
            panel.SortBy,
            panel.Descending,
            panel.Entries.Count,
            [.. panel.Entries.Skip(offset).Take(FileManagerProtocol.PageSize).Select(entry => entry.Value)],
            continuation);
    }

    private static void AddTargets(ClientSession session)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            session.Targets[$"drive-{Guid.NewGuid():N}"] = drive.RootDirectory.FullName;
        }
        foreach (var (label, path) in KnownFolders())
        {
            session.Targets[$"shortcut-{label.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}"] = path;
        }
    }

    private static FileManagerDrive[] GetDrives(ClientSession session) => [.. session.Targets
        .Where(pair => pair.Key.StartsWith("drive-", StringComparison.Ordinal))
        .Select(pair =>
        {
            try
            {
                var drive = new DriveInfo(pair.Value);
                return new FileManagerDrive(pair.Key, $"{drive.Name} {drive.VolumeLabel}".Trim(), drive.DriveType.ToString().ToLowerInvariant(), drive.IsReady ? drive.AvailableFreeSpace : null, drive.IsReady ? drive.TotalSize : null);
            }
            catch (Exception ex) when (IsFileBoundaryFailure(ex))
            {
                return new FileManagerDrive(pair.Key, pair.Value, "unavailable", null, null);
            }
        })];

    private static FileManagerShortcut[] GetShortcuts(ClientSession session) => [.. session.Targets
        .Where(pair => pair.Key.StartsWith("shortcut-", StringComparison.Ordinal))
        .Select(pair => new FileManagerShortcut(pair.Key, KnownFolders().First(candidate => string.Equals(candidate.Path, pair.Value, StringComparison.OrdinalIgnoreCase)).Label))];

    private static IEnumerable<(string Label, string Path)> KnownFolders()
    {
        var candidates = new[]
        {
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("OneDrive", Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty)
        };
        return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Item2) && Directory.Exists(candidate.Item2));
    }

    private static string GetInitialDirectory(Environment.SpecialFolder fallback, string? child)
    {
        var root = Environment.GetFolderPath(fallback);
        var preferred = child is null ? root : Path.Combine(root, child);
        return Directory.Exists(preferred) ? preferred : Directory.Exists(root) ? root : Path.GetPathRoot(Environment.SystemDirectory)!;
    }

    private static string? FirstValidDirectory(params string?[] candidates) => candidates
        .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) is { } path
            ? Path.GetFullPath(path)
            : null;

    private static string[] ToAttributes(FileAttributes attributes)
    {
        var result = new List<string>();
        if (attributes.HasFlag(FileAttributes.Hidden)) result.Add("hidden");
        if (attributes.HasFlag(FileAttributes.System)) result.Add("system");
        if (attributes.HasFlag(FileAttributes.ReadOnly)) result.Add("read-only");
        if (attributes.HasFlag(FileAttributes.Archive)) result.Add("archive");
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) result.Add("reparse-point");
        return [.. result];
    }

    private static void SortPanel(PanelState panel)
    {
        IOrderedEnumerable<EntryState> ordered = panel.Entries.OrderBy(entry => entry.Value.Kind == "file");
        ordered = panel.SortBy switch
        {
            "size" => panel.Descending
                ? ordered.ThenByDescending(entry => entry.Value.Size ?? -1).ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(entry => entry.Value.Size ?? -1).ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase),
            "type" => panel.Descending
                ? ordered.ThenByDescending(entry => entry.Value.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(entry => entry.Value.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase),
            "modified" => panel.Descending
                ? ordered.ThenByDescending(entry => entry.Value.ModifiedUtc).ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(entry => entry.Value.ModifiedUtc).ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase),
            _ => panel.Descending
                ? ordered.ThenByDescending(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase)
        };
        panel.Entries = [.. ordered];
    }

    private bool MatchesPanel(PanelState panel, bool hideProtectedItems, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = ReadPanelEntries(panel.Path, hideProtectedItems, cancellationToken);
            return string.Equals(panel.Signature, ComputeSignature(current), StringComparison.Ordinal);
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            return false;
        }
    }

    private static string ComputeSignature(IEnumerable<EntryState> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries.OrderBy(candidate => candidate.Value.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(entry.Value.Kind).Append('\0')
                .Append(entry.Value.Name).Append('\0')
                .Append(entry.Value.Size).Append('\0')
                .Append(entry.Value.ModifiedUtc.UtcTicks).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private async Task<long> CalculateSizeAsync(FileJob job, string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.TryPop(out var directory))
            {
                await AwaitReadyAsync(job).ConfigureAwait(false);
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    await AwaitReadyAsync(job).ConfigureAwait(false);
                    try
                    {
                        if (Directory.Exists(entry)) pending.Push(entry);
                        else total += new FileInfo(entry).Length;
                    }
                    catch (Exception ex) when (IsFileBoundaryFailure(ex)) { }
                }
            }
            return total;
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex)) { return 0; }
    }

    private static bool SameVolume(string source, string destination) => string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
    private static bool IsTerminalJob(FileJobState state) => state is FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled;

    private void PruneTerminalJobs(string clientId)
    {
        foreach (var stale in _jobs.Values
                     .Where(job => job.OwnerClientId == clientId && IsTerminalJob(job.State) && job.TemporaryPaths.IsEmpty && job.BackupPaths.IsEmpty)
                     .OrderByDescending(job => job.Sequence)
                     .Skip(32))
        {
            if (_jobs.TryRemove(stale.Id, out var removed)) removed.Cancellation.Dispose();
        }
    }
    private static bool IsUnsafeDestination(string source, string destinationDirectory)
    {
        var sourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var destinationPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(destinationDirectory, Path.GetFileName(sourcePath))));
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)) return true;
        return Directory.Exists(sourcePath) && destinationPath.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool UploadDestinationMatches(string clientId, string destination, FileUploadWork upload, string? ignoredPath)
    {
        try
        {
            var current = ReadPanelEntries(destination, _hideProtectedItems(clientId))
                .Where(entry => ignoredPath is null || !string.Equals(entry.Path, ignoredPath, StringComparison.OrdinalIgnoreCase));
            return string.Equals(upload.DestinationSignature, ComputeSignature(current), StringComparison.Ordinal);
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            return false;
        }
    }

    private bool IsRecoveryArtifact(string path)
    {
        if (_jobs.Values.Any(job =>
                job.TemporaryPaths.Keys.Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase)) ||
                job.BackupPaths.Keys.Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }
        lock (_journalGate)
        {
            return _unresolvedRecoveryEntries.Any(entry =>
                entry.TemporaryPaths.Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase)) ||
                (entry.Backups ?? []).Any(candidate => string.Equals(candidate.BackupPath, path, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static bool HasAvailableSpace(string destinationDirectory, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destinationDirectory));
            return string.IsNullOrEmpty(root) || !new DriveInfo(root).IsReady || new DriveInfo(root).AvailableFreeSpace >= requiredBytes;
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            return true;
        }
    }

    private static string CreateKeepBothPath(string destination)
    {
        var directory = Path.GetDirectoryName(destination) ?? throw new IOException("The upload destination is unavailable.");
        var extension = Path.GetExtension(destination);
        var stem = Path.GetFileNameWithoutExtension(destination);
        for (var index = 2; index <= 9999; index++)
        {
            var suffix = $" ({index})";
            var maximumStemLength = FileManagerProtocol.MaxNameLength - extension.Length - suffix.Length;
            if (maximumStemLength < 1) throw new IOException("The upload file name is too long.");
            var boundedStem = stem.Length <= maximumStemLength ? stem : stem[..maximumStemLength];
            var candidate = Path.Combine(directory, $"{boundedStem}{suffix}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("A keep-both file name is unavailable.");
    }

    private void CommitCaseOnlyRename(FileJob job, string source, string destination, bool directory)
    {
        var temporary = $"{source}.voltura-air-{Guid.NewGuid():N}.rename";
        var sourceMoved = false;
        var recoveryResolved = false;
        job.BackupPaths[temporary] = source;
        if (!SaveJournalSnapshot())
        {
            job.BackupPaths.TryRemove(temporary, out _);
            throw new IOException("The rename recovery journal could not be saved.");
        }
        try
        {
            _movePath(source, temporary, directory);
            sourceMoved = true;
            _movePath(temporary, destination, directory);
            recoveryResolved = true;
        }
        catch
        {
            if ((File.Exists(temporary) || Directory.Exists(temporary)) && !File.Exists(source) && !Directory.Exists(source))
            {
                _movePath(temporary, source, directory);
                recoveryResolved = true;
            }
            else if (!sourceMoved)
            {
                recoveryResolved = true;
            }
            throw;
        }
        finally
        {
            if (recoveryResolved) job.BackupPaths.TryRemove(temporary, out _);
            QueueJournalWrite();
        }
    }

    private static bool TryDeleteTemporary(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
            return !Directory.Exists(path) && !File.Exists(path);
        }
        catch (Exception ex) when (IsFileBoundaryFailure(ex))
        {
            return false;
        }
    }

    private void CommitPreparedPath(FileJob job, string source, string destination, bool directory, bool allowReplacement = true)
    {
        if (!File.Exists(destination) && !Directory.Exists(destination))
        {
            _movePath(source, destination, directory);
            return;
        }
        if (!allowReplacement) throw new IOException("The destination changed before commit.");

        var backup = Path.Combine(Path.GetDirectoryName(destination) ?? throw new IOException("The replacement destination is unavailable."), $".voltura-air-{Guid.NewGuid():N}.backup");
        var destinationIsDirectory = Directory.Exists(destination);
        job.BackupPaths[backup] = destination;
        if (!SaveJournalSnapshot())
        {
            job.BackupPaths.TryRemove(backup, out _);
            throw new IOException("The replacement recovery journal could not be saved.");
        }
        try
        {
            _movePath(destination, backup, destinationIsDirectory);
        }
        catch
        {
            job.BackupPaths.TryRemove(backup, out _);
            QueueJournalWrite();
            throw;
        }
        try
        {
            _movePath(source, destination, directory);
        }
        catch
        {
            _movePath(backup, destination, destinationIsDirectory);
            job.BackupPaths.TryRemove(backup, out _);
            QueueJournalWrite();
            throw;
        }

        try
        {
            DeleteExisting(backup);
        }
        catch
        {
            _movePath(destination, source, directory);
            _movePath(backup, destination, destinationIsDirectory);
            job.BackupPaths.TryRemove(backup, out _);
            QueueJournalWrite();
            throw;
        }
        job.BackupPaths.TryRemove(backup, out _);
        QueueJournalWrite();
    }

    private static void MovePath(string source, string destination, bool directory)
    {
        if (directory) Directory.Move(source, destination); else File.Move(source, destination);
    }
    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > FileManagerProtocol.MaxNameLength ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." ||
            name.EndsWith(' ') || name.EndsWith('.')) return false;
        var stem = name.Split('.')[0];
        return !stem.Equals("CON", StringComparison.OrdinalIgnoreCase) &&
            !stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) &&
            !stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) &&
            !stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }
    private static void DeleteExisting(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
    private static void DeleteSource(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
    private static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException || ex.HResult == unchecked((int)0x80070005);
    private static bool IsFileBoundaryFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
