using System.Buffers;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.PhoneWebcam;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void PhoneWebcamPageKeepsAFixedPreviewWithoutAnInnerScrollbar()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var view = new PhoneWebcamPageView();

            Assert.IsType<Grid>(view.Content);
            Assert.Equal(3, ((Grid)view.Content).RowDefinitions.Count);
            Assert.Equal(360, view.PreviewSurface.Height);
            Assert.Equal(VerticalAlignment.Top, view.PreviewSurface.VerticalAlignment);
        });
    }

    [Fact]
    public void IdlePhoneWebcamShowsConnectionInstructionsWithoutOpeningPreview()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            var previewStarts = 0;
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("idle"),
                static () => { },
                (_, _) =>
                {
                    previewStarts++;
                    return new ControlledPreviewSession();
                });

            PhoneWebcamPageView view = controller.CreateView();

            Assert.Equal(0, previewStarts);
            Assert.Equal("Start from your phone", view.PreviewEmptyTitle.Text);
            Assert.Contains("Settings → Tools → Phone webcam", view.PreviewEmptyMessage.Text, StringComparison.Ordinal);
            Assert.Equal(Visibility.Collapsed, view.PreviewImage.Visibility);
            Assert.Equal(
                "Choose each device's Phone webcam permission in Devices.",
                view.AccessProfileHintText.Text);
            Assert.Equal(Visibility.Collapsed, view.SessionStatusText.Visibility);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhoneWebcamPreviewShutdownCompletesBeforeItsCallerContinues(bool wasDetached)
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            var preview = new ControlledPreviewSession(blockStop: true);
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature(),
                static () => { },
                (_, _) => preview);
            _ = controller.CreateView();

            if (wasDetached) controller.StopPreview();
            Task stop = controller.StopPreviewAsync();

            Assert.True(preview.StopStarted);
            Assert.False(stop.IsCompleted);
            preview.CompleteStop();
            WaitForWpf(() => stop.IsCompleted, "Phone webcam preview release");
            stop.GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void PhoneWebcamPreviewOpensOnlyTheLatestPageAfterAPreviousReaderStops()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var previews = new List<ControlledPreviewSession>();
            using var controller = new PhoneWebcamPageController(
                window, new InstalledPhoneWebcamFeature(), static () => { }, (publish, _) =>
                {
                    var preview = new ControlledPreviewSession(publish, blockStop: previews.Count == 0);
                    previews.Add(preview);
                    return preview;
                });

            _ = controller.CreateView();
            PhoneWebcamPageView replaced = controller.CreateView();
            PhoneWebcamPageView current = controller.CreateView();
            Assert.True(Assert.Single(previews).StopStarted);
            previews[0].CompleteStop();
            WaitForWpf(() => previews.Count == 2, "latest Phone webcam preview");

            var frame = new PhoneWebcamPreviewFrame(ArrayPool<byte>.Shared.Rent(
                PhoneWebcamPreviewSession.PreviewStride * PhoneWebcamPreviewSession.PreviewHeight));
            previews[1].Publish(frame);
            WaitForWpf(() => current.PreviewImage.Source is not null, "latest Phone webcam preview frame");
            Assert.Null(replaced.PreviewImage.Source);
            Assert.Throws<ObjectDisposedException>(() => _ = frame.Buffer);
        });
    }

    [Fact]
    public void PhoneWebcamPreviewKeepsItsReaderAndBitmapAcrossAQualityUpdate()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var feature = new InstalledPhoneWebcamFeature();
            var refreshes = 0;
            ControlledPreviewSession? active = null;
            using var controller = new PhoneWebcamPageController(
                window, feature, () => refreshes++, (publish, _) => active = new ControlledPreviewSession(publish));
            PhoneWebcamPageView view = controller.CreateView();
            using var frame = new PhoneWebcamPreviewFrame(ArrayPool<byte>.Shared.Rent(
                PhoneWebcamPreviewSession.PreviewStride * PhoneWebcamPreviewSession.PreviewHeight));
            Assert.NotNull(active);
            active.Publish(frame);
            WaitForWpf(() => view.PreviewImage.Source is not null, "first Phone webcam quality frame");
            var bitmap = view.PreviewImage.Source;

            feature.ReportActivity(new("streaming", Width: 1280, Height: 720));
            WaitForWpf(() => view.SessionStatusText.Text == "Streaming · 1280×720", "Phone webcam quality label");
            Assert.Equal(0, refreshes);
            Assert.Same(bitmap, view.PreviewImage.Source);
            Assert.False(active.StopStarted);
        });
    }

    [Fact]
    public void PhoneWebcamPreviewFailureRetiresFramesThatAreAlreadyQueued()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            ControlledPreviewSession? active = null;
            Action<string>? fail = null;
            using var controller = new PhoneWebcamPageController(
                window, new InstalledPhoneWebcamFeature(), static () => { }, (publish, report) =>
                {
                    fail = report;
                    return active = new ControlledPreviewSession(publish);
                });
            PhoneWebcamPageView view = controller.CreateView();
            using var frame = new PhoneWebcamPreviewFrame(ArrayPool<byte>.Shared.Rent(
                PhoneWebcamPreviewSession.PreviewStride * PhoneWebcamPreviewSession.PreviewHeight));
            Assert.NotNull(active);
            Assert.NotNull(fail);
            active.Publish(frame);
            fail("The test reader closed.");
            WaitForWpf(() => view.RetryPreviewButton.Visibility == Visibility.Visible, "Phone webcam preview failure");
            DoWpfEvents();
            Assert.Null(view.PreviewImage.Source);
            Assert.Equal("The test reader closed.", view.PreviewEmptyMessage.Text);
            Assert.Throws<ObjectDisposedException>(() => _ = frame.Buffer);
        });
    }

    [Fact]
    public void StoppedPhoneWebcamPreviewCannotPublishIntoANewerPage()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            var previews = new Queue<ControlledPreviewSession>();
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature(),
                static () => { },
                (publish, _) =>
                {
                    var preview = new ControlledPreviewSession(publish);
                    previews.Enqueue(preview);
                    return preview;
                });
            _ = controller.CreateView();
            ControlledPreviewSession stopped = previews.Dequeue();
            controller.StopPreview();
            PhoneWebcamPageView current = controller.CreateView();
            _ = previews.Dequeue();
            var frame = new PhoneWebcamPreviewFrame(ArrayPool<byte>.Shared.Rent(
                PhoneWebcamPreviewSession.PreviewStride * PhoneWebcamPreviewSession.PreviewHeight));

            stopped.Publish(frame);
            DoWpfEvents();

            Assert.Null(current.PreviewImage.Source);
            Assert.Throws<ObjectDisposedException>(() => _ = frame.Buffer);
        });
    }

    [Fact]
    public void RuntimePhoneWebcamStatusChangeRefreshesAnOpenPage()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            var refreshes = 0;
            var feature = new PhoneWebcamFeature(new FailedPhoneWebcamSetup());
            var controller = new PhoneWebcamPageController(
                window,
                feature,
                () => refreshes++);
            try
            {
                _ = controller.CreateView();
                _ = feature.EnableAsync().GetAwaiter().GetResult();
                WaitForWpf(() => refreshes == 1, "Phone webcam status refresh");
            }
            finally
            {
                controller.Dispose();
                feature.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void RuntimePhoneWebcamStatusChangeDoesNotReopenAClosedPage()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            var refreshes = 0;
            var feature = new PhoneWebcamFeature(new FailedPhoneWebcamSetup());
            var controller = new PhoneWebcamPageController(window, feature, () => refreshes++);
            try
            {
                _ = controller.CreateView();
                controller.StopPreview();
                _ = feature.EnableAsync().GetAwaiter().GetResult();
                DoWpfEvents();

                Assert.Equal(0, refreshes);
            }
            finally
            {
                controller.Dispose();
                feature.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void MissingPhoneWebcamShowsInstallerGuidanceAsPlainText()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var root = new Grid();
            window.Content = root;
            using var controller = new PhoneWebcamPageController(window, new MissingPhoneWebcamFeature(), static () => { });
            PhoneWebcamPageView view = controller.CreateView();

            Assert.Equal(Visibility.Visible, view.SessionStatusText.Visibility);
            Assert.Equal(
                "Phone Webcam is not installed. Run Voltura Air installer maintenance to add it.",
                view.SessionStatusText.Text);
            Assert.DoesNotContain(
                FindWpfDescendants<Button>(view),
                button => button.Content?.ToString() == "Use installer maintenance");
        });
    }

    [Fact]
    public void PhoneMicrophoneWebsiteAppearsOnlyForConfirmedAbsence()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var launcher = new RecordingUrlLauncher();
            var absent = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.NotInstalled, "Absent.");
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("idle", absent),
                static () => { },
                urlLauncher: launcher);

            PhoneWebcamPageView view = controller.CreateView();
            Assert.Equal(Visibility.Visible, view.GetVbCableButton.Visibility);
            view.GetVbCableButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://vb-audio.com/Cable/", launcher.Opened?.AbsoluteUri);
        });
    }

    [Fact]
    public void ReadyPhoneMicrophoneHidesWebsiteAndNamesReceivingEndpoint()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("idle", ready),
                static () => { });

            PhoneWebcamPageView view = controller.CreateView();
            Assert.Equal(Visibility.Collapsed, view.GetVbCableButton.Visibility);
            Assert.Contains("CABLE Output", view.MicrophoneSetupText.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ActivePhoneMicrophoneCanBeMonitoredAndStopsWithThePage()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            var monitor = new ControlledAudioMonitor();
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true),
                static () => { },
                audioMonitorFactory: _ => Task.FromResult<IPhoneWebcamAudioMonitor>(monitor));

            PhoneWebcamPageView view = controller.CreateView();
            Assert.Equal(Visibility.Visible, view.AudioTestButton.Visibility);
            Assert.Equal(Visibility.Visible, view.AudioTestHintText.Visibility);

            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForWpf(() => monitor.Started && Equals("Stop audio test", view.AudioTestButton.Content), "audio monitor start");
            Assert.Equal("Stop audio test", view.AudioTestButton.Content);
            Assert.Equal(Visibility.Visible, view.AudioTestStatusText.Visibility);

            controller.StopPreview();
            WaitForWpf(() => monitor.Disposed, "audio monitor disposal");
        });
    }

    [Fact]
    public void AudioTestIsHiddenWithoutAnActivePhoneMicrophoneTrack()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready),
                static () => { });

            PhoneWebcamPageView view = controller.CreateView();
            Assert.Equal(Visibility.Collapsed, view.AudioTestButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.AudioTestHintText.Visibility);
        });
    }

    [Fact]
    public void AudioTestStartFailureIsReportedAndReleasesTheMonitor()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            var monitor = new ControlledAudioMonitor(throwOnStart: true);
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true),
                static () => { },
                audioMonitorFactory: _ => Task.FromResult<IPhoneWebcamAudioMonitor>(monitor));

            PhoneWebcamPageView view = controller.CreateView();
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            WaitForWpf(() => monitor.Disposed && view.AudioTestButton.IsEnabled, "failed audio monitor cleanup");
            Assert.Equal("Test audio", view.AudioTestButton.Content);
            Assert.Contains("Injected audio monitor failure", view.AudioTestStatusText.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AudioTestRevalidatesTheActiveMicrophoneAtClickTime()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            var feature = new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true);
            var monitorCreates = 0;
            using var controller = new PhoneWebcamPageController(
                window,
                feature,
                static () => { },
                audioMonitorFactory: _ =>
                {
                    monitorCreates++;
                    return Task.FromResult<IPhoneWebcamAudioMonitor>(new ControlledAudioMonitor());
                });

            PhoneWebcamPageView view = controller.CreateView();
            feature.Activity = new PhoneWebcamActivity("idle");
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            WaitForWpf(() => view.AudioTestButton.IsEnabled, "stale audio test rejection");
            Assert.Equal(0, monitorCreates);
            Assert.Equal(Visibility.Collapsed, view.AudioTestButton.Visibility);
            Assert.Contains("only while", view.AudioTestStatusText.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FailureFromAnOldAudioMonitorDoesNotStopItsReplacement()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            var monitors = new List<ControlledAudioMonitor>();
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true),
                static () => { },
                audioMonitorFactory: failure =>
                {
                    var monitor = new ControlledAudioMonitor(failure: failure);
                    monitors.Add(monitor);
                    return Task.FromResult<IPhoneWebcamAudioMonitor>(monitor);
                });

            PhoneWebcamPageView view = controller.CreateView();
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForWpf(
                () => monitors.Count == 1 &&
                    monitors[0].Started &&
                    view.AudioTestButton.IsEnabled &&
                    Equals("Stop audio test", view.AudioTestButton.Content),
                "first audio monitor start");
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForWpf(() => monitors[0].Disposed && Equals("Test audio", view.AudioTestButton.Content), "first audio monitor stop");
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForWpf(
                () => monitors.Count == 2 &&
                    monitors[1].Started &&
                    view.AudioTestButton.IsEnabled &&
                    Equals("Stop audio test", view.AudioTestButton.Content),
                "replacement audio monitor start");

            monitors[0].Fail("Stale monitor failure.");
            DoWpfEvents();

            Assert.False(monitors[1].Disposed);
            Assert.Equal("Stop audio test", view.AudioTestButton.Content);
            Assert.DoesNotContain("Stale", view.AudioTestStatusText.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SlowAudioDriverStartupDoesNotBlockPageShutdown()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            using var startGate = new ManualResetEventSlim();
            var monitor = new ControlledAudioMonitor(startGate: startGate);
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true),
                static () => { },
                audioMonitorFactory: _ => Task.FromResult<IPhoneWebcamAudioMonitor>(monitor));

            PhoneWebcamPageView view = controller.CreateView();
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(monitor.StartEntered.Wait(TimeSpan.FromSeconds(2)));

            Task stop = controller.StopPreviewAsync();
            Assert.False(stop.IsCompleted);
            startGate.Set();
            WaitForWpf(() => stop.IsCompleted, "slow audio startup shutdown");
            stop.GetAwaiter().GetResult();
            Assert.True(monitor.Disposed);
        });
    }

    [Fact]
    public void AudioMonitorRestartWaitsForPriorMonitorDisposal()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            var disposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var monitors = new List<ControlledAudioMonitor>();
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true),
                static () => { },
                audioMonitorFactory: _ =>
                {
                    var monitor = new ControlledAudioMonitor(
                        disposeGate: monitors.Count == 0 ? disposeGate.Task : null);
                    monitors.Add(monitor);
                    return Task.FromResult<IPhoneWebcamAudioMonitor>(monitor);
                });

            PhoneWebcamPageView view = controller.CreateView();
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForWpf(() => view.AudioTestButton.IsEnabled && monitors.Count == 1, "initial audio monitor start");
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForWpf(() => monitors[0].DisposeEntered.IsSet, "initial audio monitor retirement");
            view.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DoWpfEvents();
            Assert.Single(monitors);

            disposeGate.SetResult();
            WaitForWpf(() => monitors.Count == 2 && monitors[1].Started, "audio monitor restart after retirement");
        });
    }

    [Fact]
    public void PendingAudioDriverStartPreventsAnotherStartAfterPageRecreation()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var ready = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.Ready, "Ready.", "endpoint");
            using var startGate = new ManualResetEventSlim();
            var monitorCreates = 0;
            var monitor = new ControlledAudioMonitor(startGate: startGate);
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("streaming", ready, hasMicrophone: true),
                static () => { },
                audioMonitorFactory: _ =>
                {
                    monitorCreates++;
                    return Task.FromResult<IPhoneWebcamAudioMonitor>(monitor);
                });

            PhoneWebcamPageView first = controller.CreateView();
            first.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(monitor.StartEntered.Wait(TimeSpan.FromSeconds(2)));

            PhoneWebcamPageView replacement = controller.CreateView();
            Assert.False(replacement.AudioTestButton.IsEnabled);
            Assert.Equal("Opening audio…", replacement.AudioTestButton.Content);
            replacement.AudioTestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DoWpfEvents();
            Assert.Equal(1, monitorCreates);

            startGate.Set();
            WaitForWpf(() => monitor.Disposed, "stale pending audio monitor cleanup");
        });
    }

    [Fact]
    public void PhoneMicrophoneWebsiteLaunchFailureIsReportedWithoutEscapingTheClick()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var absent = new PhoneWebcamAudioTargetStatus(PhoneWebcamAudioTargetState.NotInstalled, "Absent.");
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("idle", absent),
                static () => { },
                urlLauncher: new ThrowingUrlLauncher());

            PhoneWebcamPageView view = controller.CreateView();
            view.GetVbCableButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Visibility.Visible, view.AudioTestStatusText.Visibility);
            Assert.Contains("default browser", view.AudioTestStatusText.Text, StringComparison.Ordinal);
        });
    }

    private sealed class InstalledPhoneWebcamFeature(
        string activityState = "streaming",
        PhoneWebcamAudioTargetStatus? audioStatus = null,
        bool hasMicrophone = false) : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(
            PhoneWebcamFeatureState.Installed,
            "Voltura Air Webcam is installed and ready.");

        public PhoneWebcamActivity Activity { get; set; } = new(activityState, HasMicrophone: hasMicrophone);
        public PhoneWebcamAudioTargetStatus AudioTargetStatus { get; } = audioStatus ?? new(
            PhoneWebcamAudioTargetState.DetectionFailed,
            "Unavailable.");
        public event EventHandler? ActivityChanged;
        public event EventHandler? StatusChanged { add { } remove { } }

        internal void ReportActivity(PhoneWebcamActivity activity)
        {
            Activity = activity;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);
    }

    private sealed class RecordingUrlLauncher : IUrlShellLauncher
    {
        internal Uri? Opened { get; private set; }
        public void Open(Uri uri) => Opened = uri;
    }

    private sealed class ThrowingUrlLauncher : IUrlShellLauncher
    {
        public void Open(Uri uri) => throw new System.ComponentModel.Win32Exception("Injected URL launch failure.");
    }

    private sealed class MissingPhoneWebcamFeature : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(
            PhoneWebcamFeatureState.NotInstalled,
            "Phone Webcam is not installed. Run Voltura Air installer maintenance to add it.");
        public PhoneWebcamActivity Activity { get; } = new("idle");
        public event EventHandler? ActivityChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }

    private sealed class FailedPhoneWebcamSetup : IPhoneWebcamSetup
    {
        private static readonly PhoneWebcamFeatureStatus Failed = new(
            PhoneWebcamFeatureState.Failed,
            "Injected failure.",
            HasError: true);
        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Failed);
        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(Failed);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken) => Task.FromResult(Failed);
    }

    private sealed class ControlledPreviewSession(Action<PhoneWebcamPreviewFrame>? publish = null, bool blockStop = false) : IPhoneWebcamPreviewSession
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool StopStarted { get; private set; }

        internal void Publish(PhoneWebcamPreviewFrame frame) =>
            (publish ?? throw new InvalidOperationException("This preview has no publisher."))(frame);

        internal void CompleteStop() => _stopped.TrySetResult();

        public Task StopAsync()
        {
            StopStarted = true;
            if (!blockStop) CompleteStop();
            return _stopped.Task;
        }

        public void Dispose()
        {
            StopStarted = true;
            _stopped.TrySetResult();
        }
    }

    private sealed class ControlledAudioMonitor(
        bool throwOnStart = false,
        Action<string>? failure = null,
        ManualResetEventSlim? startGate = null,
        Task? disposeGate = null) : IPhoneWebcamAudioMonitor
    {
        internal ManualResetEventSlim StartEntered { get; } = new();
        internal ManualResetEventSlim DisposeEntered { get; } = new();
        internal bool Started { get; private set; }
        internal bool Disposed { get; private set; }

        public void Start()
        {
            StartEntered.Set();
            startGate?.Wait();
            Started = true;
            if (throwOnStart)
            {
                throw new InvalidOperationException("Injected audio monitor failure.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.Set();
            if (disposeGate is not null)
            {
                await disposeGate;
            }
            Disposed = true;
        }

        internal void Fail(string message) =>
            (failure ?? throw new InvalidOperationException("This monitor has no failure callback."))(message);
    }
}
