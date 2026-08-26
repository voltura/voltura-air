using System.Net.WebSockets;

namespace VolturaAir.Host;

internal enum FileTransferSourceKind
{
    FileEntry,
    ScreenCapture,
    Upload
}

internal sealed class FileTransferSession(
    string id,
    string clientId,
    string operationId,
    WebSocket socket,
    string direction,
    string fileName,
    long declaredSize,
    FileTransferSourceKind sourceKind = FileTransferSourceKind.FileEntry)
{
    private sealed class CancellationOwner
    {
        public CancellationTokenSource Source { get; } = new();
        public CancellationToken Token { get; }

        public CancellationOwner() => Token = Source.Token;
    }

    private readonly CancellationOwner _cancellation = new();
    public string Id { get; } = id;
    public string ClientId { get; } = clientId;
    public string OperationId { get; } = operationId;
    public WebSocket Socket { get; } = socket;
    public string Direction { get; } = direction;
    public string FileName { get; } = fileName;
    public long DeclaredSize { get; } = declaredSize;
    public FileTransferSourceKind SourceKind { get; } = sourceKind;
    public CancellationTokenSource Cancellation => _cancellation.Source;
    public CancellationToken CancellationToken => _cancellation.Token;
    public TaskCompletionSource StartPublished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AnswerApplied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Lock Gate { get; } = new();
    public IFileTransferWebRtcPeer? Peer { get; set; }
    public string? OfferHash { get; set; }
    public string? JobId { get; set; }
    public FileTransferDownloadSource? DownloadSource { get; set; }
    public Task? RunTask { get; set; }
    public bool SlotHeld { get; set; }
    public long LastStatusTimestamp { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public int DisposeStarted;

    public bool TryCancel(string code, string message)
    {
        lock (Gate)
        {
            if (Volatile.Read(ref DisposeStarted) != 0) return false;
            FailureCode ??= code;
            FailureMessage ??= message;
            Cancellation.Cancel();
            return true;
        }
    }
}

internal sealed class FileTransferPendingStart(
    string clientId,
    string direction,
    WebSocket socket,
    CancellationTokenSource cancellation,
    FileTransferSourceKind sourceKind = FileTransferSourceKind.FileEntry) : IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation = cancellation;
    private string? _failureCode;
    private string? _failureMessage;

    public string ClientId { get; } = clientId;
    public string Direction { get; } = direction;
    public FileTransferSourceKind SourceKind { get; } = sourceKind;
    public WebSocket Socket { get; } = socket;
    public CancellationToken Token { get { lock (_gate) return _cancellation?.Token ?? new(canceled: true); } }
    public Task Task { get; set; } = System.Threading.Tasks.Task.CompletedTask;

    public bool TryCancel(string code, string message)
    {
        lock (_gate)
        {
            if (_cancellation is null) return false;
            _failureCode ??= code;
            _failureMessage ??= message;
            _cancellation.Cancel();
            return true;
        }
    }

    public (string? Code, string? Message) Failure
    {
        get { lock (_gate) return (_failureCode, _failureMessage); }
    }

    public void Dispose()
    {
        CancellationTokenSource? owned;
        lock (_gate)
        {
            owned = _cancellation;
            _cancellation = null;
        }
        owned?.Dispose();
    }
}
