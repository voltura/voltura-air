using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Features.AiAssistant;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void OpeningAssistantKeepsStartupOffTheWpfDispatcher()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var client = new PageAssistantClient();
            var factory = new PageAssistantClientFactory(client);
            var manager = new AiAssistantSessionManager(factory);
            try
            {
                int dispatcherThread = Environment.CurrentManagedThreadId;
                using var controller = new AiAssistantPageController(window, manager);
                AiAssistantPageView view = controller.CreateView(preserveState: false);

                Assert.True(factory.AvailabilityThread.Task.Wait(TimeSpan.FromSeconds(2)));
                Assert.NotEqual(dispatcherThread, factory.AvailabilityThread.Task.Result);
                WaitForWpf(() => view.StatusText.Text == "Ready", "Assistant open");
                Assert.Contains(
                    FindWpfDescendants<TextBlock>(view),
                    text => text.Text.StartsWith("This is a powerful, read-only tool.", StringComparison.Ordinal));
            }
            finally
            {
                manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void LeavingAssistantCancelsItsPendingOpen()
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var client = new PageAssistantClient { BlockInitialRead = true };
            var manager = new AiAssistantSessionManager(new PageAssistantClientFactory(client));
            try
            {
                using var controller = new AiAssistantPageController(window, manager);
                _ = controller.CreateView(preserveState: false);
                Assert.True(client.ReadStarted.Wait(TimeSpan.FromSeconds(2)));

                controller.Stop();

                Assert.True(client.ReadCancelled.Wait(TimeSpan.FromSeconds(2)));
                WaitForWpf(() => client.Disposed && !manager.IsActive, "Assistant open cancellation");
            }
            finally
            {
                manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncertainAssistantOperationRetiresTheLease(bool reset)
    {
        if (ShouldSkipNativeUiLayoutTests()) return;
        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            var window = new Window();
            WpfTheme.Apply(window);
            var client = new PageAssistantClient
            {
                FailTurnStart = !reset,
                FailReplacementRead = reset
            };
            var manager = new AiAssistantSessionManager(new PageAssistantClientFactory(client));
            try
            {
                using var controller = new AiAssistantPageController(window, manager);
                AiAssistantPageView view = controller.CreateView(preserveState: false);
                WaitForWpf(() => view.StatusText.Text == "Ready", "Assistant open");

                if (reset)
                {
                    view.NewConversationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else
                {
                    view.QuestionTextBox.Text = "Keep this draft";
                    view.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

                WaitForWpf(
                    () => view.RetryButton.Visibility == Visibility.Visible && client.Disposed && !manager.IsActive,
                    "uncertain Assistant cleanup");
                Assert.Equal("Retry required", view.StateTitleText.Text);
                if (!reset) Assert.Equal("Keep this draft", view.QuestionTextBox.Text);
            }
            finally
            {
                manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private sealed class PageAssistantClientFactory(PageAssistantClient client) : IAiAssistantClientFactory
    {
        internal TaskCompletionSource<int> AvailabilityThread { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable
        {
            get
            {
                AvailabilityThread.TrySetResult(Environment.CurrentManagedThreadId);
                return true;
            }
        }
        public Task<IAiAssistantClient> ConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAiAssistantClient>(client);
    }

    private sealed class PageAssistantClient : IAiAssistantClient
    {
        private bool _replacementCreated;
        internal bool BlockInitialRead { get; init; }
        internal bool FailTurnStart { get; init; }
        internal bool FailReplacementRead { get; init; }
        internal bool Disposed { get; private set; }
        internal ManualResetEventSlim ReadStarted { get; } = new();
        internal ManualResetEventSlim ReadCancelled { get; } = new();
        public event Action<string, string, string, string>? AgentMessageCompleted { add { } remove { } }
        public event Action<string, string, string>? TurnCompleted { add { } remove { } }
        public event Action? ConnectionClosed { add { } remove { } }

        public Task<CodexThreadSummary?> FindAssistantAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CodexThreadSummary?>(new("thread", AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot));

        public Task<CodexThreadSummary> StartAssistantAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test Assistant already exists.");

        public Task<CodexThreadSummary> ReplaceAssistantAsync(string previousThreadId, CancellationToken cancellationToken)
        {
            _replacementCreated = true;
            return Task.FromResult(new CodexThreadSummary("replacement", AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot));
        }

        public Task ResumeAssistantAsync(string threadId, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<CodexThreadDetail> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            if (BlockInitialRead && !_replacementCreated)
            {
                ReadStarted.Set();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    ReadCancelled.Set();
                    throw;
                }
            }
            if (FailReplacementRead && _replacementCreated)
                throw new CodexCompatibilityException("The replacement transcript could not be read.");
            return new CodexThreadDetail(
                new(threadId, AiAssistantProfile.ThreadName, AiAssistantProfile.KnowledgeRoot),
                []);
        }

        public Task<CodexTurnHandle> StartTurnAsync(string threadId, string question, CancellationToken cancellationToken) =>
            FailTurnStart
                ? throw new CodexCompatibilityException("The turn result was uncertain.")
                : Task.FromResult(new CodexTurnHandle(threadId, "turn"));

        public void ReleaseTurnNotifications(string threadId) { }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
