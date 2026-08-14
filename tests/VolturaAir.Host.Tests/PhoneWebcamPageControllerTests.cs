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
            AddPhoneWebcamButtonStyles(window);
            var root = new Grid();
            window.Content = root;
            using var toasts = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");
            var previewStarts = 0;
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature("idle"),
                toasts,
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
            Assert.Equal("Allow paired devices", view.AllowPairedDevicesCheckBox.Content);
            Assert.Equal("Remove from Windows", view.InstallationActionButton.Content);
            Assert.Same(
                view.AllowPairedDevicesCheckBox.Parent,
                ((FrameworkElement)view.InstallationActionButton.Parent).Parent);
        });
    }

    [Fact]
    public void PhoneWebcamPreviewShutdownCompletesBeforeItsCallerContinues()
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
            AddPhoneWebcamButtonStyles(window);
            var root = new Grid();
            window.Content = root;
            using var toasts = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");
            var preview = new ControlledPreviewSession();
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature(),
                toasts,
                static () => { },
                (_, _) => preview);
            _ = controller.CreateView();

            Task stop = controller.StopPreviewAsync();

            Assert.True(preview.StopStarted);
            Assert.False(stop.IsCompleted);
            preview.CompleteStop();
            WaitForWpf(() => stop.IsCompleted, "Phone webcam preview release");
            stop.GetAwaiter().GetResult();
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
            AddPhoneWebcamButtonStyles(window);
            var root = new Grid();
            window.Content = root;
            using var toasts = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");
            var previews = new Queue<ControlledPreviewSession>();
            using var controller = new PhoneWebcamPageController(
                window,
                new InstalledPhoneWebcamFeature(),
                toasts,
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
            AddPhoneWebcamButtonStyles(window);
            var root = new Grid();
            window.Content = root;
            using var toasts = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");
            var refreshes = 0;
            var feature = new PhoneWebcamFeature(new FailedPhoneWebcamSetup());
            var controller = new PhoneWebcamPageController(
                window,
                feature,
                toasts,
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
            AddPhoneWebcamButtonStyles(window);
            var root = new Grid();
            window.Content = root;
            using var toasts = new HostToastPresenter(root, new HostVisualFactory(window.Resources), static () => "Voltura Air");
            var refreshes = 0;
            var feature = new PhoneWebcamFeature(new FailedPhoneWebcamSetup());
            var controller = new PhoneWebcamPageController(window, feature, toasts, () => refreshes++);
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
    private sealed class InstalledPhoneWebcamFeature(string activityState = "streaming") : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(
            PhoneWebcamFeatureState.Installed,
            "Voltura Air Webcam is installed and ready.");

        public PhoneWebcamActivity Activity { get; } = new(activityState);
        public event EventHandler? ActivityChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }

        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);

        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);
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

    private static void AddPhoneWebcamButtonStyles(Window window)
    {
        window.Resources["PrimaryButtonStyle"] = new Style(typeof(Button));
        window.Resources["DangerButtonStyle"] = new Style(typeof(Button));
    }

    private sealed class ControlledPreviewSession(Action<PhoneWebcamPreviewFrame>? publish = null) : IPhoneWebcamPreviewSession
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool StopStarted { get; private set; }

        internal void Publish(PhoneWebcamPreviewFrame frame) =>
            (publish ?? throw new InvalidOperationException("This preview has no publisher."))(frame);

        internal void CompleteStop() => _stopped.TrySetResult();

        public Task StopAsync()
        {
            StopStarted = true;
            return _stopped.Task;
        }

        public void Dispose()
        {
            StopStarted = true;
            _stopped.TrySetResult();
        }
    }
}
