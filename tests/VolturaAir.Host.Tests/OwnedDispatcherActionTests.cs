using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void OwnedDispatcherActionCoalescesQueuedCallbacks()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var calls = 0;
            using var action = new OwnedDispatcherAction(Dispatcher.CurrentDispatcher, () => calls += 1);

            for (var index = 0; index < 20; index += 1)
            {
                action.Queue();
            }

            Assert.True(action.IsPending);
            DoWpfEvents();

            Assert.Equal(1, calls);
            Assert.False(action.IsPending);
        });
    }

    [Fact]
    public void OwnedDispatcherActionCanBeQueuedFromBackgroundThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var calls = 0;
            using var action = new OwnedDispatcherAction(Dispatcher.CurrentDispatcher, () => calls += 1);

            Task.Run(() => action.Queue()).GetAwaiter().GetResult();
            DoWpfEvents();

            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public void DisposedOwnedDispatcherActionAbortsPendingCallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var calls = 0;
            var action = new OwnedDispatcherAction(Dispatcher.CurrentDispatcher, () => calls += 1);

            action.Queue();
            action.Dispose();
            DoWpfEvents();

            Assert.Equal(0, calls);
            Assert.False(action.IsPending);
        });
    }
}
