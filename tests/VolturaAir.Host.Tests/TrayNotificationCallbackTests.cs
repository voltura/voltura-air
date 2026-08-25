using System.Windows.Threading;
using VolturaAir.Host;
using VolturaAir.Host.Features.Updates;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host.Tests;

public sealed class TrayNotificationPresenterTests
{
    [Fact]
    public void ReadyUpdateNotificationProvidesTheHostOpeningAction()
    {
        var opens = 0;
        var action = TrayUpdateController.NotificationAction(UpdateNotificationKind.Ready, () => opens++);

        action?.Invoke();

        Assert.Equal(1, opens);
        Assert.Null(TrayUpdateController.NotificationAction(UpdateNotificationKind.UpToDate, () => opens++));
    }

    [Fact]
    public void ClickRunsTheCurrentActionThenShowsTheDeferredNotification()
    {
        using var trayIcon = new Forms.NotifyIcon();
        var shown = new List<string>();
        var clicks = 0;
        using var presenter = new TrayNotificationPresenter(
            Dispatcher.CurrentDispatcher,
            trayIcon,
            (title, _, _) => shown.Add(title));
        presenter.Enqueue("First", "First message", Forms.ToolTipIcon.Info, () => clicks++);
        presenter.Enqueue("Second", "Second message", Forms.ToolTipIcon.Warning, action: null);

        presenter.OnClicked();
        presenter.OnClosed();
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.SystemIdle);

        Assert.Equal(1, clicks);
        Assert.Equal(["First", "Second"], shown);
    }

    [Fact]
    public void AvailableHandlerCanPresentMandatoryNoticeBeforeDeferredOrdinaryNotice()
    {
        using var trayIcon = new Forms.NotifyIcon();
        var shown = new List<string>();
        using var presenter = new TrayNotificationPresenter(
            Dispatcher.CurrentDispatcher,
            trayIcon,
            (title, _, _) => shown.Add(title));
        var mandatoryPending = true;
        presenter.Available += () =>
        {
            if (mandatoryPending)
            {
                mandatoryPending = false;
                Assert.True(presenter.TryShowNow(
                    "Mandatory",
                    "Device uses My device access.",
                    Forms.ToolTipIcon.Info,
                    static () => { }));
            }
        };
        presenter.Enqueue("Current", "Current message", Forms.ToolTipIcon.Info, action: null);
        presenter.Enqueue("Deferred", "Deferred message", Forms.ToolTipIcon.Warning, action: null);

        presenter.OnClosed();
        Assert.Equal(["Current", "Mandatory"], shown);

        presenter.OnClosed();
        Assert.Equal(["Current", "Mandatory", "Deferred"], shown);
    }

    [Fact]
    public void OnlyTheLatestOrdinaryReplacementIsDeferred()
    {
        using var trayIcon = new Forms.NotifyIcon();
        var shown = new List<string>();
        using var presenter = new TrayNotificationPresenter(
            Dispatcher.CurrentDispatcher,
            trayIcon,
            (title, _, _) => shown.Add(title));
        presenter.Enqueue("Current", "Current message", Forms.ToolTipIcon.Info, action: null);
        presenter.Enqueue("Superseded", "Old pending message", Forms.ToolTipIcon.Info, action: null);
        presenter.Enqueue("Latest", "Latest pending message", Forms.ToolTipIcon.Warning, action: null);

        presenter.OnClosed();

        Assert.Equal(["Current", "Latest"], shown);
    }

    [Fact]
    public void CloseAndDisposalNeverInvokeAClickAction()
    {
        using var trayIcon = new Forms.NotifyIcon();
        var clicks = 0;
        var presenter = new TrayNotificationPresenter(
            Dispatcher.CurrentDispatcher,
            trayIcon,
            static (_, _, _) => { });
        presenter.Enqueue("Current", "Current message", Forms.ToolTipIcon.Info, () => clicks++);

        presenter.OnClosed();
        presenter.Enqueue("Next", "Next message", Forms.ToolTipIcon.Info, () => clicks++);
        presenter.Dispose();
        presenter.OnClicked();

        Assert.Equal(0, clicks);
    }
}
