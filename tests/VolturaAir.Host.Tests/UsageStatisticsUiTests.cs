using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Features.Diagnostics;
using VolturaAir.Host.Features.UsageTelemetry;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void UsageStatisticsViewShowsTheClosedCatalogAndPortableOffState()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var control = new FakeUsageStatisticsControl(
                enabled: false,
                UsageStatisticsDistribution.Portable);
            var view = new UsageStatisticsController(control).CreateView();
            var copy = string.Join(
                '\n',
                FindWpfDescendants<TextBlock>(view).Select(text => text.Text));

            Assert.Equal("Off", view.StateText.Text);
            Assert.Equal("Enable", view.ChangeStateButton.Content);
            Assert.Equal("Portable version", view.ProfileText.Text);
            foreach (var category in new[]
            {
                "Trackpad", "Keyboard", "Dictation", "Media controls", "Presentation",
                "Custom screens", "Files", "Screen viewing", "Phone webcam", "Gyro mouse"
            })
            {
                Assert.Contains(category, copy, StringComparison.Ordinal);
            }
            Assert.Contains("Text, files, URLs", copy, StringComparison.Ordinal);
            Assert.Equal("Privacy", view.PrivacyButton.Content);
            Assert.DoesNotContain("0c99c983", copy, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void UsageStatisticsTransitionIsSerializedAndUpdatesTheVisibleState()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var control = new FakeUsageStatisticsControl(
                enabled: false,
                UsageStatisticsDistribution.Installed);
            var view = new UsageStatisticsController(control).CreateView();

            view.ChangeStateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal([true], control.Requests);
            Assert.False(view.ChangeStateButton.IsEnabled);
            view.ChangeStateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(control.Requests);

            control.Complete(new UsageStatisticsTransitionResult(true, true, true));
            DrainDispatcher();

            Assert.True(view.ChangeStateButton.IsEnabled);
            Assert.Equal("On", view.StateText.Text);
            Assert.Equal("Disable", view.ChangeStateButton.Content);
            Assert.Equal(Visibility.Collapsed, view.TransitionStatusText.Visibility);
        });
    }

    [Fact]
    public void UsageStatisticsDisableFailureDoesNotClaimDurableSuccess()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var control = new FakeUsageStatisticsControl(
                enabled: true,
                UsageStatisticsDistribution.Installed);
            var view = new UsageStatisticsController(control).CreateView();

            view.ChangeStateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            control.Complete(new UsageStatisticsTransitionResult(false, false, false));
            DrainDispatcher();

            Assert.Equal("Off", view.StateText.Text);
            Assert.Equal("Retry disable", view.ChangeStateButton.Content);
            Assert.Equal(Visibility.Visible, view.TransitionStatusText.Visibility);
            Assert.Contains("Off for now", view.TransitionStatusText.Text, StringComparison.Ordinal);
            Assert.Contains("Retry before restarting", view.TransitionStatusText.Text, StringComparison.Ordinal);

            view.ChangeStateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([false, false], control.Requests);
        });
    }

    [Fact]
    public void UsageStatisticsShowsStartupIdentityCleanupFailure()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var control = new FakeUsageStatisticsControl(
                enabled: false,
                UsageStatisticsDistribution.Installed)
            { State = UsageStatisticsRuntimeState.OffIdentityCleanupPending };
            var view = new UsageStatisticsController(control).CreateView();

            Assert.Equal("Off", view.StateText.Text);
            Assert.Equal(Visibility.Visible, view.TransitionStatusText.Visibility);
            Assert.Contains("old local ID could not be removed", view.TransitionStatusText.Text, StringComparison.Ordinal);
            Assert.Contains("Retry before enabling", view.TransitionStatusText.Text, StringComparison.Ordinal);
        });
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class FakeUsageStatisticsControl(
        bool enabled,
        UsageStatisticsDistribution distribution) : IUsageStatisticsControl
    {
        private TaskCompletionSource<UsageStatisticsTransitionResult>? _pending;

        public UsageStatisticsRuntimeState State { get; set; } = enabled
            ? UsageStatisticsRuntimeState.On
            : UsageStatisticsRuntimeState.Off;

        public UsageStatisticsDistribution Distribution { get; } = distribution;

        public List<bool> Requests { get; } = [];

        public event EventHandler? StateChanged;

        public Task<UsageStatisticsTransitionResult> SetEnabledAsync(
            bool nextEnabled,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(nextEnabled);
            _pending = new TaskCompletionSource<UsageStatisticsTransitionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => _pending.TrySetCanceled(cancellationToken));
            return _pending.Task;
        }

        public void Complete(UsageStatisticsTransitionResult result)
        {
            var wasEnableRequest = Requests.Count != 0 && Requests[^1];
            State = result.EffectiveEnabled
                ? UsageStatisticsRuntimeState.On
                : !wasEnableRequest && !result.Saved
                    ? UsageStatisticsRuntimeState.OffChoiceNotSaved
                    : !wasEnableRequest && !result.IdentityRemoved
                        ? UsageStatisticsRuntimeState.OffIdentityCleanupPending
                        : UsageStatisticsRuntimeState.Off;
            StateChanged?.Invoke(this, EventArgs.Empty);
            Assert.NotNull(_pending);
            _pending.SetResult(result);
        }
    }
}
