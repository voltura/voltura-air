using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;

namespace VolturaAir.Host.Features.PhoneWebcam;

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "StopPreview atomically exchanges and disposes the dynamically replaceable preview session.")]
internal sealed class PhoneWebcamPageController : IDisposable
{
    private readonly Window _owner;
    private readonly IPhoneWebcamFeature _phoneWebcam;
    private readonly Action _refresh;
    private readonly Func<Action<PhoneWebcamPreviewFrame>, Action<string>, IPhoneWebcamPreviewSession> _previewFactory;
    private readonly UrlOpenService _urlOpenService;
    private readonly Func<Action<string>, Task<IPhoneWebcamAudioMonitor>> _audioMonitorFactory;
    private readonly Lock _previewLock = new();
    private readonly Lock _audioWorkLock = new();
    private IPhoneWebcamPreviewSession? _preview;
    private IPhoneWebcamAudioMonitor? _audioMonitor;
    private QueuedPreviewFrame? _pendingFrame;
    private PhoneWebcamPageView? _currentView;
    private int _renderQueued;
    private int _previewGeneration;
    private int _audioMonitorGeneration;
    private readonly HashSet<Task> _audioOperations = [];
    private Task _audioRetirement = Task.CompletedTask;
    private bool _disposed;

    internal PhoneWebcamPageController(
        Window owner,
        IPhoneWebcamFeature phoneWebcam,
        Action refresh,
        Func<Action<PhoneWebcamPreviewFrame>, Action<string>, IPhoneWebcamPreviewSession>? previewFactory = null,
        IUrlShellLauncher? urlLauncher = null,
        Func<Action<string>, Task<IPhoneWebcamAudioMonitor>>? audioMonitorFactory = null)
    {
        _owner = owner;
        _phoneWebcam = phoneWebcam;
        _refresh = refresh;
        _previewFactory = previewFactory ?? ((publish, failure) => new PhoneWebcamPreviewSession(publish, failure));
        _urlOpenService = new UrlOpenService(urlLauncher);
        _audioMonitorFactory = audioMonitorFactory ?? (failure => PhoneWebcamAudioMonitor.CreateAsync(failure));
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
        string sessionStatus = status.IsInstalled ? DescribeActivity() : status.Message;
        view.SessionStatusText.Text = sessionStatus;
        view.SessionStatusText.Visibility = string.IsNullOrWhiteSpace(sessionStatus)
            ? Visibility.Collapsed
            : Visibility.Visible;
        RenderMicrophoneSetup(view);
        view.GetVbCableButton.Click += (_, _) =>
        {
            UrlOpenExecutionResult result = _urlOpenService.Execute("https://vb-audio.com/Cable/");
            if (!result.Succeeded)
            {
                view.AudioTestStatusText.Text = result.Message;
                view.AudioTestStatusText.Visibility = Visibility.Visible;
            }
        };
        view.CheckMicrophoneAgainButton.Click += async (_, _) =>
        {
            view.CheckMicrophoneAgainButton.IsEnabled = false;
            await _phoneWebcam.RefreshAudioTargetAsync();
            if (_currentView == view)
            {
                RenderMicrophoneSetup(view);
                view.CheckMicrophoneAgainButton.IsEnabled = true;
            }
        };
        view.AudioTestButton.Click += (_, _) => TrackAudioOperation(ToggleAudioMonitorAsync(view));
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

    private void RenderMicrophoneSetup(PhoneWebcamPageView view)
    {
        PhoneWebcamAudioTargetStatus status = _phoneWebcam.AudioTargetStatus;
        view.MicrophoneSetupText.Text = status.State switch
        {
            PhoneWebcamAudioTargetState.Ready =>
                "Optional phone microphone is ready. Select CABLE Output as the microphone in the receiving app.",
            PhoneWebcamAudioTargetState.InstalledButUnavailable =>
                "VB-CABLE is installed but unavailable. Enable its CABLE Input endpoint in Windows Sound settings or restart Windows.",
            PhoneWebcamAudioTargetState.NotInstalled =>
                "Optional phone microphone requires VB-CABLE, third-party donationware not included with Voltura Air. Obtain it directly from VB-Audio and follow the licence applicable to your use.",
            _ => "Voltura Air could not check optional phone microphone support. Check again or review troubleshooting guidance."
        };
        view.GetVbCableButton.Visibility = status.State == PhoneWebcamAudioTargetState.NotInstalled
            ? Visibility.Visible
            : Visibility.Collapsed;
        bool canTestAudio = CanTestAudio();
        bool audioOperationPending = HasPendingAudioOperation();
        view.AudioTestButton.Visibility = canTestAudio ? Visibility.Visible : Visibility.Collapsed;
        view.AudioTestHintText.Visibility = canTestAudio ? Visibility.Visible : Visibility.Collapsed;
        view.AudioTestButton.IsEnabled = !audioOperationPending;
        view.AudioTestButton.Content = _audioMonitor is not null
            ? "Stop audio test"
            : audioOperationPending
                ? "Opening audio…"
                : "Test audio";
        if (canTestAudio && audioOperationPending)
        {
            view.AudioTestStatusText.Text = "Windows is still opening the audio devices. Another audio test cannot start yet.";
            view.AudioTestStatusText.Visibility = Visibility.Visible;
        }
    }

    internal void StopPreview()
    {
        StopAudioMonitor();
        IPhoneWebcamPreviewSession? preview = DetachPreview();
        preview?.Dispose();
    }

    internal async Task StopPreviewAsync()
    {
        StopAudioMonitor();
        IPhoneWebcamPreviewSession? preview = DetachPreview();
        if (preview is not null)
        {
            await preview.StopAsync();
        }
        await AwaitAudioQuiescenceAsync();
    }

    private async Task ToggleAudioMonitorAsync(PhoneWebcamPageView view)
    {
        view.AudioTestButton.IsEnabled = false;
        int generation = Volatile.Read(ref _audioMonitorGeneration);
        try
        {
            if (_audioMonitor is null && HasPendingAudioOperation())
            {
                RenderMicrophoneSetup(view);
                return;
            }

            if (_audioMonitor is not null)
            {
                StopAudioMonitor();
                if (_currentView == view)
                {
                    view.AudioTestButton.Content = "Test audio";
                    view.AudioTestStatusText.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (!CanTestAudio())
            {
                RenderMicrophoneSetup(view);
                view.AudioTestStatusText.Text = "Audio testing is available only while the active phone session is streaming microphone audio.";
                view.AudioTestStatusText.Visibility = Visibility.Visible;
                return;
            }

            generation = Interlocked.Increment(ref _audioMonitorGeneration);
            Task priorRetirement = GetAudioRetirement();
            IPhoneWebcamAudioMonitor monitor = await Task.Run(async () =>
            {
                await priorRetirement.ConfigureAwait(false);
                IPhoneWebcamAudioMonitor created = await _audioMonitorFactory(
                    message => ReportAudioMonitorFailure(generation, message)).ConfigureAwait(false);
                try
                {
                    created.Start();
                    return created;
                }
                catch
                {
                    await created.DisposeAsync();
                    throw;
                }
            });

            if (generation != Volatile.Read(ref _audioMonitorGeneration) ||
                _currentView != view ||
                !CanTestAudio())
            {
                RetireAudioMonitor(monitor);
                return;
            }

            _audioMonitor = monitor;
            view.AudioTestButton.Content = "Stop audio test";
            view.AudioTestStatusText.Text = "Playing CABLE Output through the default Windows speakers.";
            view.AudioTestStatusText.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == Volatile.Read(ref _audioMonitorGeneration) && _currentView == view)
            {
                view.AudioTestStatusText.Text = exception.Message;
                view.AudioTestStatusText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (_currentView == view)
            {
                view.AudioTestButton.IsEnabled = true;
            }
        }
    }

    private bool CanTestAudio() =>
        _phoneWebcam.AudioTargetStatus.IsReady &&
        string.Equals(_phoneWebcam.Activity.State, "streaming", StringComparison.Ordinal) &&
        _phoneWebcam.Activity.HasMicrophone;

    private bool HasPendingAudioOperation()
    {
        lock (_audioWorkLock)
        {
            return _audioOperations.Count > 0;
        }
    }

    private Task GetAudioRetirement()
    {
        lock (_audioWorkLock)
        {
            return _audioRetirement;
        }
    }

    private void StopAudioMonitor()
    {
        Interlocked.Increment(ref _audioMonitorGeneration);
        IPhoneWebcamAudioMonitor? monitor = Interlocked.Exchange(ref _audioMonitor, null);
        if (monitor is not null)
        {
            RetireAudioMonitor(monitor);
        }
    }

    private void RetireAudioMonitor(IPhoneWebcamAudioMonitor monitor)
    {
        lock (_audioWorkLock)
        {
            _audioRetirement = RetireAudioMonitorAsync(_audioRetirement, monitor);
        }
    }

    private static async Task RetireAudioMonitorAsync(
        Task previous,
        IPhoneWebcamAudioMonitor monitor)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        await Task.Run(async () => await monitor.DisposeAsync()).ConfigureAwait(false);
    }

    private void TrackAudioOperation(Task operation)
    {
        lock (_audioWorkLock)
        {
            _audioOperations.Add(operation);
        }
        _ = ClearAudioOperationAsync(operation);
    }

    private async Task ClearAudioOperationAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        lock (_audioWorkLock)
        {
            _audioOperations.Remove(operation);
        }

        if (!_owner.Dispatcher.HasShutdownStarted)
        {
            _ = _owner.Dispatcher.BeginInvoke(() =>
            {
                if (_currentView is not null)
                {
                    RenderMicrophoneSetup(_currentView);
                }
            });
        }
    }

    private async Task AwaitAudioQuiescenceAsync()
    {
        while (true)
        {
            Task[] operations;
            Task retirement;
            lock (_audioWorkLock)
            {
                operations = [.. _audioOperations];
                retirement = _audioRetirement;
            }

            try
            {
                await Task.WhenAll([.. operations, retirement]).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
            lock (_audioWorkLock)
            {
                if (_audioOperations.Count == 0 && _audioRetirement.IsCompleted)
                {
                    return;
                }
            }
        }
    }

    private void ReportAudioMonitorFailure(int generation, string message)
    {
        if (_owner.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _owner.Dispatcher.BeginInvoke(async () =>
        {
            if (generation != Volatile.Read(ref _audioMonitorGeneration))
            {
                return;
            }

            PhoneWebcamPageView? failedView = _currentView;
            StopAudioMonitor();
            await AwaitAudioQuiescenceAsync();
            if (failedView is not null &&
                ReferenceEquals(_currentView, failedView) &&
                generation + 1 == Volatile.Read(ref _audioMonitorGeneration))
            {
                failedView.AudioTestButton.Content = "Test audio";
                failedView.AudioTestStatusText.Text = message;
                failedView.AudioTestStatusText.Visibility = Visibility.Visible;
            }
        });
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
