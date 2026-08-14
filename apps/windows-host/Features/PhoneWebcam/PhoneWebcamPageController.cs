using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;

namespace VolturaAir.Host.Features.PhoneWebcam;

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "StopPreview atomically exchanges and disposes the dynamically replaceable preview session.")]
internal sealed class PhoneWebcamPageController : IDisposable
{
    private readonly Window _owner;
    private readonly IPhoneWebcamFeature _phoneWebcam;
    private readonly HostToastPresenter _toasts;
    private readonly Action _refresh;
    private readonly Func<Action<PhoneWebcamPreviewFrame>, Action<string>, IPhoneWebcamPreviewSession> _previewFactory;
    private readonly Lock _previewLock = new();
    private IPhoneWebcamPreviewSession? _preview;
    private QueuedPreviewFrame? _pendingFrame;
    private PhoneWebcamPageView? _currentView;
    private int _renderQueued;
    private int _previewGeneration;
    private bool _disposed;

    internal PhoneWebcamPageController(
        Window owner,
        IPhoneWebcamFeature phoneWebcam,
        HostToastPresenter toasts,
        Action refresh,
        Func<Action<PhoneWebcamPreviewFrame>, Action<string>, IPhoneWebcamPreviewSession>? previewFactory = null)
    {
        _owner = owner;
        _phoneWebcam = phoneWebcam;
        _toasts = toasts;
        _refresh = refresh;
        _previewFactory = previewFactory ?? ((publish, failure) => new PhoneWebcamPreviewSession(publish, failure));
        _phoneWebcam.ActivityChanged += OnActivityChanged;
        _phoneWebcam.StatusChanged += OnStatusChanged;
    }

    internal PhoneWebcamPageView CreateView()
    {
        StopPreview();
        var view = new PhoneWebcamPageView();
        _currentView = view;
        PhoneWebcamFeatureStatus status = _phoneWebcam.Status;
        view.InstallationStatusText.Text = DescribeInstallation(status);
        string sessionStatus = status.HasError ? status.Message : DescribeActivity();
        view.SessionStatusText.Text = sessionStatus;
        view.SessionStatusText.Visibility = string.IsNullOrWhiteSpace(sessionStatus)
            ? Visibility.Collapsed
            : Visibility.Visible;
        view.AllowPairedDevicesCheckBox.IsChecked = AppPermissionSettings.Load().AllowPhoneWebcam;
        view.AllowPairedDevicesCheckBox.Click += (_, _) =>
        {
            HostPermissionSet current = AppPermissionSettings.Load();
            AppPermissionSettings.Save(current with
            {
                AllowPhoneWebcam = view.AllowPairedDevicesCheckBox.IsChecked == true
            });
        };
        ConfigureInstallationAction(view, status);

        if (status.IsInstalled)
        {
            switch (_phoneWebcam.Activity.State)
            {
                case "streaming":
                    view.ShowOpeningPreview();
                    view.RetryPreviewButton.Click += (_, _) => RestartPreview();
                    StartPreview();
                    break;
                case "connecting":
                    view.ShowEmptyState(
                        "Connecting to your phone",
                        "The live preview will appear when the phone starts sending video.");
                    break;
                default:
                    view.ShowEmptyState(
                        "Start from your phone",
                        "Connect to Voltura Air, then open Settings → Tools → Phone webcam.");
                    break;
            }
        }
        else
        {
            view.ShowEmptyState(
                status.State == PhoneWebcamFeatureState.NeedsCleanup
                    ? "Finish setup"
                    : "Enable in Windows",
                status.State == PhoneWebcamFeatureState.NeedsCleanup
                    ? "Remove the incomplete installation before enabling it again."
                    : "Enable the camera before using it in Windows apps.");
        }

        return view;
    }

    internal void StopPreview()
    {
        IPhoneWebcamPreviewSession? preview = DetachPreview();
        preview?.Dispose();
    }

    internal async Task StopPreviewAsync()
    {
        IPhoneWebcamPreviewSession? preview = DetachPreview();
        if (preview is not null)
        {
            await preview.StopAsync();
        }
    }

    private IPhoneWebcamPreviewSession? DetachPreview()
    {
        Interlocked.Increment(ref _previewGeneration);
        _currentView = null;
        IPhoneWebcamPreviewSession? preview = Interlocked.Exchange(ref _preview, null);
        lock (_previewLock)
        {
            _pendingFrame?.Dispose();
            _pendingFrame = null;
        }
        return preview;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _phoneWebcam.ActivityChanged -= OnActivityChanged;
        _phoneWebcam.StatusChanged -= OnStatusChanged;
        StopPreview();
    }

    private void ConfigureInstallationAction(PhoneWebcamPageView view, PhoneWebcamFeatureStatus status)
    {
        Button action = view.InstallationActionButton;
        bool remove = status.ShouldRemove;
        action.Content = status.State == PhoneWebcamFeatureState.UpdateRequired
            ? "Remove old version"
            : status.State == PhoneWebcamFeatureState.Removing
                ? "Removing…"
            : remove ? "Remove from Windows" : "Enable in Windows";
        action.Style = _owner.FindResource(remove ? "DangerButtonStyle" : "PrimaryButtonStyle") as Style;
        action.IsEnabled = status.State is not PhoneWebcamFeatureState.Unavailable and not PhoneWebcamFeatureState.Removing;
        action.Click += async (_, _) => await ChangeInstallationAsync(remove, action);
    }

    private async Task ChangeInstallationAsync(bool remove, Button action)
    {
        if (remove && !ThemedConfirmationDialog.Show(
                _owner,
                "Remove from Windows",
                "Windows apps will no longer be able to select Voltura Air Webcam. You can enable it again later.",
                "Remove",
                "Cancel",
                ConfirmationTone.Warning))
        {
            return;
        }

        action.IsEnabled = false;
        await StopPreviewAsync();
        PhoneWebcamFeatureStatus result = remove
            ? await _phoneWebcam.RemoveAsync()
            : await _phoneWebcam.EnableAsync();
        _toasts.Show(
            result.Message,
            "Phone webcam",
            result.HasError || result.State is PhoneWebcamFeatureState.Failed or PhoneWebcamFeatureState.Unavailable
                ? HostToastTone.Failure
                : HostToastTone.Success);
        _refresh();
    }

    private void RestartPreview()
    {
        StopPreview();
        _refresh();
    }

    private void StartPreview()
    {
        if (_disposed || _currentView is null)
        {
            return;
        }

        int generation = Volatile.Read(ref _previewGeneration);
        _preview = _previewFactory(
            frame => QueueFrame(generation, frame),
            message => ReportPreviewFailure(generation, message));
    }

    private void QueueFrame(int generation, PhoneWebcamPreviewFrame frame)
    {
        if (generation != Volatile.Read(ref _previewGeneration))
        {
            frame.Dispose();
            return;
        }

        PhoneWebcamPreviewFrame? rejected = null;
        lock (_previewLock)
        {
            if (generation != Volatile.Read(ref _previewGeneration))
            {
                rejected = frame;
            }
            else
            {
                QueuedPreviewFrame? displaced = _pendingFrame;
                _pendingFrame = new QueuedPreviewFrame(generation, frame);
                displaced?.Dispose();
            }
        }
        rejected?.Dispose();
        if (rejected is not null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _renderQueued, 1) == 0)
        {
            if (_owner.Dispatcher.HasShutdownStarted)
            {
                Interlocked.Exchange(ref _renderQueued, 0);
                return;
            }
            _owner.Dispatcher.BeginInvoke(RenderLatestFrame, DispatcherPriority.Render);
        }
    }

    private void RenderLatestFrame()
    {
        QueuedPreviewFrame? queued;
        lock (_previewLock)
        {
            queued = _pendingFrame;
            _pendingFrame = null;
        }

        try
        {
            if (queued is not null &&
                queued.Generation == Volatile.Read(ref _previewGeneration) &&
                _currentView is not null)
            {
                _currentView.SetPreviewFrame(queued.Frame);
            }
        }
        finally
        {
            queued?.Dispose();
            Interlocked.Exchange(ref _renderQueued, 0);
        }

        lock (_previewLock)
        {
            if (_pendingFrame is not null && Interlocked.Exchange(ref _renderQueued, 1) == 0)
            {
                if (_owner.Dispatcher.HasShutdownStarted)
                {
                    Interlocked.Exchange(ref _renderQueued, 0);
                }
                else
                {
                    _owner.Dispatcher.BeginInvoke(RenderLatestFrame, DispatcherPriority.Render);
                }
            }
        }
    }

    private void ReportPreviewFailure(int generation, string message)
    {
        if (_owner.Dispatcher.HasShutdownStarted)
        {
            return;
        }
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (_currentView is null || generation != Volatile.Read(ref _previewGeneration))
            {
                return;
            }

            _currentView.ShowPreviewFailure(message);
        });
    }

    private void OnActivityChanged(object? sender, EventArgs args)
    {
        if (_owner.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (_currentView is not null)
            {
                _refresh();
            }
        });
    }

    private void OnStatusChanged(object? sender, EventArgs args)
    {
        if (_owner.Dispatcher.HasShutdownStarted || _currentView is null)
        {
            return;
        }

        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (_currentView is not null)
            {
                _refresh();
            }
        });
    }

    private string DescribeActivity()
    {
        PhoneWebcamActivity activity = _phoneWebcam.Activity;
        return activity.State switch
        {
            "connecting" => "Connecting",
            "streaming" when activity.Width.HasValue && activity.Height.HasValue =>
                $"Streaming · {activity.Width}×{activity.Height}",
            "streaming" => "Streaming",
            _ => string.Empty
        };
    }

    private static string DescribeInstallation(PhoneWebcamFeatureStatus status) => status.State switch
    {
        PhoneWebcamFeatureState.Installed when status.HasError => "Needs attention",
        PhoneWebcamFeatureState.Installed => "Ready",
        PhoneWebcamFeatureState.NotInstalled => "Not enabled",
        PhoneWebcamFeatureState.NeedsCleanup => "Needs attention",
        PhoneWebcamFeatureState.UpdateRequired => "Update available",
        PhoneWebcamFeatureState.Removing => "Removing…",
        PhoneWebcamFeatureState.Failed => "Needs attention",
        _ => "Unavailable"
    };

    private sealed record QueuedPreviewFrame(int Generation, PhoneWebcamPreviewFrame Frame) : IDisposable
    {
        public void Dispose() => Frame.Dispose();
    }
}
