using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    private static readonly BlockingCollection<WpfTestWorkItem> WpfTestQueue = [];
    private static readonly Lazy<bool> WpfTestThread = new(StartWpfTestThread);

    private static void RunOnStaThread(Action action)
    {
        _ = WpfTestThread.Value;
        using var workItem = new WpfTestWorkItem(action);
        WpfTestQueue.Add(workItem);
        workItem.Wait();
    }

    private static bool StartWpfTestThread()
    {
        Exception? startupException = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                _ = new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }
            catch (Exception ex)
            {
                startupException = ex;
            }
            finally
            {
                ready.Set();
            }

            if (startupException is not null)
            {
                return;
            }

            foreach (var workItem in WpfTestQueue.GetConsumingEnumerable())
            {
                workItem.Execute();
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (startupException is not null)
        {
            throw new InvalidOperationException("The WPF test thread could not start.", startupException);
        }

        return true;
    }

    private static bool ShouldSkipNativeUiLayoutTests()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void DisposeWebHost(WebHostService webHost)
    {
        var disposal = webHost.DisposeAsync().AsTask();
        WaitForWpf(() => disposal.IsCompleted, "test web host cleanup");
        disposal.GetAwaiter().GetResult();
    }

    private sealed class NoOwner : IWin32Window
    {
        public static readonly NoOwner Instance = new();

        public IntPtr Handle => IntPtr.Zero;
    }

    private sealed class WpfTestWorkItem(Action action) : IDisposable
    {
        private readonly ManualResetEventSlim _completed = new();
        private Exception? _exception;

        public void Dispose() => _completed.Dispose();

        public void Execute()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _exception = ex;
            }
            finally
            {
                _completed.Set();
            }
        }

        public void Wait()
        {
            _completed.Wait();
            if (_exception is not null)
            {
                ExceptionDispatchInfo.Capture(_exception).Throw();
            }
        }
    }
}
