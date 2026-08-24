using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed class TrayNotificationPresenter(
    Dispatcher dispatcher,
    Forms.NotifyIcon trayIcon,
    Action<string, string, Forms.ToolTipIcon>? notificationSink) : IDisposable
{
    private TrayNotification? _current;
    private TrayNotification? _deferred;
    private bool _completingClick;
    private bool _disposed;

    public event Action? Available;

    public bool IsAvailable => !_disposed && _current is null && !_completingClick;

    public void Enqueue(
        string title,
        string message,
        Forms.ToolTipIcon icon,
        Action? action)
    {
        if (_disposed)
        {
            return;
        }

        var notification = new TrayNotification(title, message, icon, action);
        if (IsAvailable)
        {
            Show(notification);
        }
        else
        {
            _deferred = notification;
        }
    }

    public bool TryShowNow(
        string title,
        string message,
        Forms.ToolTipIcon icon,
        Action? action)
    {
        if (!IsAvailable)
        {
            return false;
        }

        Show(new TrayNotification(title, message, icon, action));
        return true;
    }

    public void OnClicked()
    {
        if (_disposed || _current is not { } notification)
        {
            return;
        }

        _current = null;
        _completingClick = true;
        _ = dispatcher.BeginInvoke(() =>
        {
            try
            {
                notification.Action?.Invoke();
            }
            finally
            {
                _completingClick = false;
                CompleteCurrent();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    public void OnClosed()
    {
        if (_disposed || _current is null || _completingClick)
        {
            return;
        }

        _current = null;
        CompleteCurrent();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current = null;
        _deferred = null;
        Available = null;
    }

    private void CompleteCurrent()
    {
        if (_disposed)
        {
            return;
        }

        Available?.Invoke();
        if (IsAvailable && _deferred is { } deferred)
        {
            _deferred = null;
            Show(deferred);
        }
    }

    private void Show(TrayNotification notification)
    {
        _current = notification;
        if (notificationSink is not null)
        {
            notificationSink(notification.Title, notification.Message, notification.Icon);
            return;
        }

        trayIcon.ShowBalloonTip(3000, notification.Title, notification.Message, notification.Icon);
    }

    private sealed record TrayNotification(
        string Title,
        string Message,
        Forms.ToolTipIcon Icon,
        Action? Action);
}
