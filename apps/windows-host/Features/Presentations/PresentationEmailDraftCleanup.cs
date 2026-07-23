using System.Security;

namespace VolturaAir.Host.Features.Presentations;

internal sealed class PresentationEmailDraftCleanup : IAsyncDisposable
{
    private readonly IAppLogWriter _appLog;
    private readonly Action _cleanup;
    private readonly System.Threading.Timer _timer;
    private int _cleanupRunning;

    public PresentationEmailDraftCleanup(IAppLogWriter appLog)
        : this(
            appLog,
            PresentationReportSharing.DeleteExpiredDraftArtifacts,
            TimeSpan.Zero,
            TimeSpan.FromHours(1))
    {
    }

    internal PresentationEmailDraftCleanup(
        IAppLogWriter appLog,
        Action cleanup,
        TimeSpan dueTime,
        TimeSpan period)
    {
        _appLog = appLog;
        _cleanup = cleanup;
        _timer = new System.Threading.Timer(RunCleanup, null, dueTime, period);
    }

    public ValueTask DisposeAsync() => _timer.DisposeAsync();

    private void RunCleanup(object? state)
    {
        if (Interlocked.Exchange(ref _cleanupRunning, 1) != 0)
        {
            return;
        }

        try
        {
            _cleanup();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            try
            {
                _appLog.Write(new AppLogEntry(
                    Event: "host_maintenance",
                    Source: "windows_host",
                    Action: "presentation_email_draft_cleanup",
                    Outcome: "failed",
                    Detail: "Presentation email draft cleanup could not complete."));
            }
            catch (Exception logException) when (logException is not OutOfMemoryException)
            {
                // A maintenance timer must not terminate the host because an injected logger failed.
            }
        }
        finally
        {
            Volatile.Write(ref _cleanupRunning, 0);
        }
    }
}
