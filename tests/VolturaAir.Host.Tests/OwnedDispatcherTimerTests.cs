using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    [Fact]
    public void OwnedDispatcherTimerFiresOnceAndReleasesItsCallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var calls = 0;
            using var timer = new OwnedDispatcherTimer(
                Dispatcher.CurrentDispatcher,
                TimeSpan.FromMilliseconds(1),
                () => calls += 1);

            timer.Start();
            WaitForWpf(() => calls == 1, "owned one-shot timer callback");
            DoWpfEvents();

            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public void DisposedOwnedDispatcherTimerDoesNotInvokeItsCallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            var calls = 0;
            var timer = new OwnedDispatcherTimer(
                Dispatcher.CurrentDispatcher,
                TimeSpan.FromMilliseconds(1),
                () => calls += 1);

            timer.Start();
            timer.Dispose();
            DoWpfEvents();

            Assert.Equal(0, calls);
        });
    }
}
