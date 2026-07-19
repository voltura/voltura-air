using System.Windows;
using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void ShutdownCoordinatorCompletesCleanupBeforeApplicationShutdown()
    {
        RunOnStaThread(() =>
        {
            var cleanupRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupStarted = false;
            var cleanupCompleted = false;
            var shutdownCalls = 0;

            async ValueTask DisposeAsync()
            {
                cleanupStarted = true;
                await cleanupRelease.Task;
                cleanupCompleted = true;
            }

            var coordinator = new WpfShutdownCoordinator(
                Dispatcher.CurrentDispatcher,
                DisposeAsync,
                () =>
                {
                    Assert.True(cleanupCompleted);
                    shutdownCalls++;
                });

            coordinator.RequestShutdown();
            coordinator.RequestShutdown();

            Assert.True(cleanupStarted);
            Assert.False(cleanupCompleted);
            Assert.Equal(0, shutdownCalls);

            cleanupRelease.SetResult();
            WaitForWpf(() => coordinator.Completion.IsCompleted, "asynchronous host shutdown");
            coordinator.Completion.GetAwaiter().GetResult();

            Assert.True(cleanupCompleted);
            Assert.Equal(1, shutdownCalls);
        });
    }

    [Fact]
    public void ShutdownCoordinatorStillShutsDownWhenCleanupFails()
    {
        RunOnStaThread(() =>
        {
            Exception? reportedFailure = null;
            var shutdownCalls = 0;
            var coordinator = new WpfShutdownCoordinator(
                Dispatcher.CurrentDispatcher,
                () => ValueTask.FromException(new InvalidOperationException("cleanup failed")),
                () => shutdownCalls++,
                exception => reportedFailure = exception);

            coordinator.RequestShutdown();
            coordinator.Completion.GetAwaiter().GetResult();

            Assert.Equal("cleanup failed", reportedFailure?.Message);
            Assert.Equal(1, shutdownCalls);
        });
    }

    [Fact]
    public void TrayExitRequestsCoordinatedShutdownWithoutStoppingDispatcherDirectly()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            var shutdownRequests = 0;
            using var trayContext = new WpfTrayApplicationContext(
                window,
                webHost,
                manager,
                webHost.AwakeService,
                () => shutdownRequests++);
            try
            {
                trayContext.RequestExit();

                Assert.Equal(1, shutdownRequests);
                Assert.False(Application.Current.Dispatcher.HasShutdownStarted);
            }
            finally
            {
                window.Close();
                var disposal = webHost.DisposeAsync().AsTask();
                WaitForWpf(() => disposal.IsCompleted, "test web host cleanup");
                disposal.GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void ClosingTheWindowNotifiesOnceAndKeepsTheTrayHostRunning()
    {
        if (ShouldSkipNativeUiLayoutTests())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var appScope = new WpfApplicationScope();
            using var store = new TempPairingStore();
            using var inputInjector = new SendInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(manager, new InputDispatcher(inputInjector), isolatedTestMode: true);
            var window = new MainWindow(manager, webHost, clientUrl: null);
            var notifications = new List<(string Title, string Message)>();
            using var trayContext = new WpfTrayApplicationContext(
                window,
                webHost,
                manager,
                webHost.AwakeService,
                static () => { },
                (title, message, _) => notifications.Add((title, message)));
            try
            {
                window.Show();
                window.Close();

                Assert.False(window.IsVisible);
                Assert.Single(notifications);
                Assert.Equal(CloseToTrayNotification.Title, notifications[0].Title);
                Assert.Equal(CloseToTrayNotification.Message, notifications[0].Message);

                window.Show();
                window.Close();
                Assert.Single(notifications);
            }
            finally
            {
                trayContext.RequestExit();
                window.Close();
                var disposal = webHost.DisposeAsync().AsTask();
                WaitForWpf(() => disposal.IsCompleted, "test web host cleanup");
                disposal.GetAwaiter().GetResult();
            }
        });
    }
}
