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

internal sealed record FileJobJournalEntry(string JobId, string ClientId, string Operation, string[] TemporaryPaths);

internal interface IFileJobJournal
{
    FileJobJournalEntry[] Load();
    void Save(FileJobJournalEntry[] entries);
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

    public void Save(FileJobJournalEntry[] entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = $"{_path}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
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

    private sealed class FileJob(long sequence, string ownerClientId, string operation, string[] sources, string? destination, string? rename, bool clearClipboard)
    {
        public long Sequence { get; set; } = sequence;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string OwnerClientId { get; } = ownerClientId;
        public string Operation { get; } = operation;
        public string[] Sources { get; } = sources;
        public string? Destination { get; } = destination;
        public string? Rename { get; } = rename;
        public bool ClearClipboard { get; } = clearClipboard;
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
        public ConcurrentDictionary<string, long> PreparedSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly string? _initialLeftPath;
    private readonly string? _initialRightPath;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileJobSnapshot[]> _interruptedJobs = new(StringComparer.Ordinal);
    private readonly Lock _queueGate = new();
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

    public FileManagerService(IFileManagerPlatform? platform = null, string? initialLeftPath = null, string? initialRightPath = null, IFileManagerLocationStore? locations = null, IFileJobJournal? journal = null)
    {
        _platform = platform ?? new WindowsFileManagerPlatform();
        _locations = locations ?? new RegistryFileManagerLocationStore();
        _journal = journal ?? new LocalFileJobJournal();
        _initialLeftPath = initialLeftPath;
        _initialRightPath = initialRightPath;
        _worker = Task.Run(ProcessJobsAsync);
        _locationWorker = Task.Run(ProcessLocationUpdatesAsync);
        RecoverInterruptedJobs();
        _journalWorker = Task.Run(ProcessJournalUpdatesAsync);
    }

    public event EventHandler<string>? JobChanged;
    public string[] SessionClientIds => [.. _sessions.Keys];

    public FileManagerSessionSnapshot OpenSession(string clientId)
    {
        var savedLeft = _locations.Load(clientId, "left");
        var savedRight = _locations.Load(clientId, "right");
        var leftPath = FirstValidDirectory(savedLeft, _initialLeftPath) ?? GetInitialDirectory(Environment.SpecialFolder.UserProfile, "Downloads");
        var rightPath = FirstValidDirectory(savedRight, _initialRightPath) ?? GetInitialDirectory(Environment.SpecialFolder.MyDocuments, null);
        var session = new ClientSession(
            clientId,
            Guid.NewGuid().ToString("N"),
            new PanelState("left", leftPath),
            new PanelState("right", rightPath));
        AddTargets(session);
        RefreshPanel(session.Left);
        RefreshPanel(session.Right);
        _sessions[clientId] = session;
        return new FileManagerSessionSnapshot(
            session.Id,
            GetDrives(session),
            GetShortcuts(session),
            BuildPage(session, session.Left, 0),
            BuildPage(session, session.Right, 0));
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

    public bool TryNavigate(string clientId, string sessionId, string panelName, string revision, string targetId, out FileManagerPanelPage? page, out string code)
    {
        page = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            if (panel.Revision != revision) { code = "stale-panel"; return false; }
            if (!MatchesPanel(panel)) { code = "stale-panel"; return false; }
            string? target = null;
            if (targetId == "parent") target = Directory.GetParent(panel.Path)?.FullName;
            else if (session.Targets.TryGetValue(targetId, out var known)) target = known;
            else target = panel.Entries.FirstOrDefault(entry => entry.Id == targetId && entry.Value.Kind == "folder")?.Path;
            if (target is null || !Directory.Exists(target)) { code = "target-unavailable"; return false; }
            panel.Path = Path.GetFullPath(target);
            RefreshPanel(panel);
            _locationUpdates.Writer.TryWrite((clientId, panel.Name, panel.Path));
            page = BuildPage(session, panel, 0);
            code = "accepted";
            return true;
        }
    }

    public bool TryRefresh(string clientId, string sessionId, string panelName, out FileManagerPanelPage? page, out string code)
    {
        page = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            try
            {
                RefreshPanel(panel);
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
        if (!TryResolveEntry(clientId, sessionId, panelName, revision, entryId, out var entry, out code)) return false;
        try
        {
            FileSystemInfo info = entry!.Value.Kind == "folder" ? new DirectoryInfo(entry.Path) : new FileInfo(entry.Path);
            properties = new FileManagerProperties(
                entry.Id,
                entry.Value.Name,
                info.FullName,
                entry.Value.Kind,
                entry.Value.Extension,
                info is FileInfo file ? file.Length : null,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                info.LastAccessTimeUtc,
                ToAttributes(info.Attributes));
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
        if (!TryResolveSelection(clientId, sessionId, panelName, revision, selection, out var paths, out var code))
            return (false, code, code == "stale-panel" ? "The folder changed. Refresh it and try again." : "The selection is unavailable.");
        return _platform.SetFileClipboard(paths, move);
    }

    public (bool Succeeded, string? Code, string Message) Open(string clientId, string sessionId, string panelName, string revision, string entryId)
    {
        return TryResolveEntry(clientId, sessionId, panelName, revision, entryId, out var entry, out var code)
            ? _platform.OpenWithShell(entry!.Path)
            : (false, code, "The selected item is unavailable.");
    }

    public (bool Succeeded, string? Code, string Message, FileJobSnapshot? Job) CreateJob(
        string clientId,
        string sessionId,
        string sourcePanel,
        string revision,
        FileManagerSelection selection,
        string operation,
        string? destinationPanel,
        string? newName)
    {
        string[] paths;
        string? destination = null;
        var clearClipboard = false;
        if (operation == "paste")
        {
            if (!TryGetPanel(clientId, sessionId, sourcePanel, out var session, out var panel)) return (false, "session-expired", "Files must be reopened.", null);
            var clipboard = _platform.GetFileClipboard();
            if (!clipboard.Succeeded) return (false, clipboard.Code, clipboard.Message, null);
            paths = clipboard.Paths;
            destination = panel.Path;
            operation = clipboard.Move ? "move" : "copy";
            clearClipboard = clipboard.Move;
        }
        else
        {
            if (!TryResolveSelection(clientId, sessionId, sourcePanel, revision, selection, out paths, out var code))
                return (false, code, "The folder changed. Refresh it and try again.", null);
            if (operation is "copy" or "move")
            {
                if (string.IsNullOrEmpty(destinationPanel) || !TryGetPanel(clientId, sessionId, destinationPanel, out _, out var destinationState))
                    return (false, "destination-unavailable", "The destination panel is unavailable.", null);
                destination = destinationState.Path;
            }
        }

        if (operation == "rename")
        {
            if (paths.Length != 1 || !IsValidName(newName)) return (false, "invalid-name", "Enter one valid Windows file name.", null);
        }
        if (operation == "delete" && paths.Any(path => !_platform.CanRecycle(path)))
            return (false, "cannot-recycle", "Every selected item must support the Windows Recycle Bin.", null);
        if (paths.Length == 0) return (false, "selection-empty", "Select at least one item.", null);

        var job = new FileJob(Interlocked.Increment(ref _jobSequence), clientId, operation, paths, destination, newName, clearClipboard);
        lock (_queueGate)
        {
            if (_pendingJobs.Count >= 64)
            {
                job.Cancellation.Dispose();
                return (false, "queue-full", "The file-operation queue is full. Try again later.", null);
            }
            _jobs[job.Id] = job;
            _pendingJobs.Add(job);
        }
        _queueSignal.Release();
        Publish(job);
        return (true, null, "File operation queued.", Snapshot(job));
    }

    public bool ControlJob(string clientId, string jobId, string action)
    {
        if (!_jobs.TryGetValue(jobId, out var job) || job.OwnerClientId != clientId) return false;
        if (action == "pause" && job.State is FileJobState.Running or FileJobState.Preparing)
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
            job.State = FileJobState.Canceling;
            job.PauseGate.Resume();
            job.Conflict?.TrySetResult("cancel");
            _ = job.Cancellation.CancelAsync();
        }
        else return false;
        Publish(job);
        return true;
    }

    public bool ResolveConflict(string clientId, string jobId, string resolution, bool applyToAll)
    {
        if (!_jobs.TryGetValue(jobId, out var job) || job.OwnerClientId != clientId || job.State != FileJobState.NeedsAttention ||
            resolution is not ("replace" or "skip" or "cancel")) return false;
        if (applyToAll) job.ApplyAllResolution = resolution;
        job.Conflict?.TrySetResult(resolution);
        return true;
    }

    public bool ReorderJob(string clientId, string jobId, string direction)
    {
        if (direction is not ("up" or "down")) return false;
        lock (_queueGate)
        {
            var owned = _pendingJobs.Where(job => job.OwnerClientId == clientId && job.State == FileJobState.Queued).OrderBy(job => job.Sequence).ToList();
            var index = owned.FindIndex(job => job.Id == jobId);
            var otherIndex = direction == "up" ? index - 1 : index + 1;
            if (index < 0 || otherIndex < 0 || otherIndex >= owned.Count) return false;
            (owned[index].Sequence, owned[otherIndex].Sequence) = (owned[otherIndex].Sequence, owned[index].Sequence);
            Publish(owned[index]);
            Publish(owned[otherIndex]);
            return true;
        }
    }

    public FileJobSnapshot[] GetJobs(string clientId) => [.. _jobs.Values
        .Where(job => job.OwnerClientId == clientId)
        .OrderByDescending(job => job.State is not (FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled))
        .ThenBy(job => job.Sequence)
        .Take(32)
        .Select(Snapshot)
        .Concat(_interruptedJobs.TryGetValue(clientId, out var interrupted) ? interrupted : [])
        .Take(32)];

    public void RevokeClient(string clientId, bool closeSession)
    {
        if (closeSession) _sessions.TryRemove(clientId, out _);
        foreach (var job in _jobs.Values.Where(candidate => candidate.OwnerClientId == clientId && candidate.State is not (FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled)))
        {
            job.State = FileJobState.Canceling;
            job.PauseGate.Resume();
            job.Conflict?.TrySetResult("cancel");
            _ = job.Cancellation.CancelAsync();
            Publish(job);
        }
    }

    public async ValueTask DisposeAsync()
    {
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
        _journal.Save([]);
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
                job.ItemsTotal = job.Sources.Length;
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
                if (job.ClearClipboard) _platform.ClearFileClipboardIfMatches(job.Sources);
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
                job.Message = ex is IOException ? "A file operation failed because an item or destination was unavailable." : "Windows denied the file operation.";
            }
            finally
            {
                job.Speed.Stop();
                Publish(job);
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
        if (job.Operation == "delete")
        {
            foreach (var source in job.Sources)
            {
                await AwaitReadyAsync(job).ConfigureAwait(false);
                job.CurrentName = Path.GetFileName(source);
                Publish(job);
                _platform.Recycle(source);
                job.ItemsCompleted++;
            }
            return;
        }
        if (job.Operation == "rename")
        {
            var source = job.Sources[0];
            var destination = Path.Combine(Path.GetDirectoryName(source)!, job.Rename!);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                var resolution = await ResolveConflictAsync(job, Path.GetFileName(destination)).ConfigureAwait(false);
                if (resolution == "skip") return;
                if (resolution == "cancel") throw new OperationCanceledException(job.Cancellation.Token);
                DeleteExisting(destination);
            }
            if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
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
                if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
                job.BytesCompleted += sourceSize;
            }
            else
            {
                var copied = await CopyEntryAsync(job, source, destination).ConfigureAwait(false);
                if (copied && job.Operation == "move") DeleteSource(source);
            }
            job.ItemsCompleted++;
            Publish(job);
        }
    }

    private async Task<bool> CopyEntryAsync(FileJob job, string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            var resolution = await ResolveConflictAsync(job, Path.GetFileName(destination)).ConfigureAwait(false);
            if (resolution == "skip") return false;
            if (resolution == "cancel") throw new OperationCanceledException(job.Cancellation.Token);
            DeleteExisting(destination);
        }
        if (Directory.Exists(source))
        {
            if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Reparse-point directories cannot be copied recursively.");
            Directory.CreateDirectory(destination);
            var complete = true;
            foreach (var child in Directory.EnumerateFileSystemEntries(source))
            {
                await AwaitReadyAsync(job).ConfigureAwait(false);
                if (!await CopyEntryAsync(job, child, Path.Combine(destination, Path.GetFileName(child))).ConfigureAwait(false)) complete = false;
            }
            return complete;
        }

        var temporary = $"{destination}.voltura-air-{Guid.NewGuid():N}.part";
        job.TemporaryPaths[temporary] = 0;
        QueueJournalWrite();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            const int bufferSize = 1024 * 1024;
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer, job.Cancellation.Token).ConfigureAwait(false)) > 0)
            {
                await AwaitReadyAsync(job).ConfigureAwait(false);
                await output.WriteAsync(buffer.AsMemory(0, read), job.Cancellation.Token).ConfigureAwait(false);
                job.BytesCompleted += read;
                Publish(job);
            }
            await output.FlushAsync(job.Cancellation.Token).ConfigureAwait(false);
            File.Move(temporary, destination);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            File.SetAttributes(destination, File.GetAttributes(source));
            return true;
        }
        finally
        {
            try { File.Delete(temporary); } catch (Exception ex) when (IsFileBoundaryFailure(ex)) { }
            job.TemporaryPaths.TryRemove(temporary, out _);
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

    private static async Task AwaitReadyAsync(FileJob job)
    {
        job.Cancellation.Token.ThrowIfCancellationRequested();
        await job.PauseGate.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
        job.Cancellation.Token.ThrowIfCancellationRequested();
        if (job.State == FileJobState.Paused) job.State = job.ResumeState;
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
            var active = _jobs.Values
                .Where(job => job.State is not (FileJobState.Completed or FileJobState.Failed or FileJobState.Canceled))
                .Select(job => new FileJobJournalEntry(job.Id, job.OwnerClientId, job.Operation, [.. job.TemporaryPaths.Keys]))
                .ToArray();
            _journal.Save(active);
        }
    }

    private void RecoverInterruptedJobs()
    {
        var entries = _journal.Load();
        foreach (var entry in entries)
        {
            foreach (var temporary in entry.TemporaryPaths)
            {
                try { File.Delete(temporary); } catch (Exception ex) when (IsFileBoundaryFailure(ex)) { }
            }
            var snapshot = new FileJobSnapshot(entry.JobId, entry.Operation, "interrupted", 0, 0, 0, 0, 0, null, null, null, "The PC restarted before this operation finished.", null, false, false, false);
            _interruptedJobs.AddOrUpdate(entry.ClientId, [snapshot], (_, current) => [.. current, snapshot]);
        }
        if (entries.Length > 0) _journal.Save([]);
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
            job.State is FileJobState.Running or FileJobState.Preparing,
            job.State == FileJobState.Paused,
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
            if (!MatchesPanel(panel)) { code = "stale-panel"; return false; }
            var selected = selection.All
                ? panel.Entries.Where(entry => !selection.ExcludedEntryIds.Contains(entry.Id, StringComparer.Ordinal))
                : panel.Entries.Where(entry => selection.EntryIds.Contains(entry.Id, StringComparer.Ordinal));
            paths = [.. selected.Select(entry => entry.Path)];
            code = paths.Length == 0 ? "selection-empty" : "accepted";
            return paths.Length > 0;
        }
    }

    private bool TryResolveEntry(string clientId, string sessionId, string panelName, string revision, string entryId, out EntryState? entry, out string code)
    {
        entry = null;
        if (!TryGetPanel(clientId, sessionId, panelName, out var session, out var panel)) { code = "session-expired"; return false; }
        lock (session.Gate)
        {
            if (panel.Revision != revision) { code = "stale-panel"; return false; }
            if (!MatchesPanel(panel)) { code = "stale-panel"; return false; }
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

    private static void RefreshPanel(PanelState panel)
    {
        var items = new List<(string Path, FileSystemInfo Info)>();
        foreach (var path in Directory.EnumerateFileSystemEntries(panel.Path))
        {
            try
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                items.Add((path, info));
            }
            catch (Exception ex) when (IsFileBoundaryFailure(ex)) { }
        }
        panel.Entries = [.. items.Select(item =>
            {
                var id = Guid.NewGuid().ToString("N");
                var file = item.Info as FileInfo;
                return new EntryState(id, item.Path, new FileManagerEntry(
                    id,
                    item.Info.Name,
                    file is null ? "folder" : "file",
                    file?.Extension.TrimStart('.') ?? string.Empty,
                    file?.Length,
                    item.Info.LastWriteTimeUtc,
                    ToAttributes(item.Info.Attributes)));
            })];
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

    private static bool MatchesPanel(PanelState panel)
    {
        try
        {
            var current = new List<EntryState>();
            foreach (var path in Directory.EnumerateFileSystemEntries(panel.Path))
            {
                try
                {
                    FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                    var file = info as FileInfo;
                    current.Add(new EntryState(string.Empty, path, new FileManagerEntry(
                        string.Empty,
                        info.Name,
                        file is null ? "folder" : "file",
                        file?.Extension.TrimStart('.') ?? string.Empty,
                        file?.Length,
                        info.LastWriteTimeUtc,
                        [])));
                }
                catch (Exception ex) when (IsFileBoundaryFailure(ex)) { }
            }
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

    private static async Task<long> CalculateSizeAsync(FileJob job, string path)
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
    private static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= FileManagerProtocol.MaxNameLength && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not "." and not "..";
    private static void DeleteExisting(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
    private static void DeleteSource(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
    private static bool IsFileBoundaryFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
