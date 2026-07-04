using System.Windows.Forms;

namespace VolturaAir.Host.Tests;

public sealed partial class HostUiLayoutTests
{
    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static bool ShouldSkipNativeUiLayoutTests()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoOwner : IWin32Window
    {
        public static readonly NoOwner Instance = new();

        public IntPtr Handle => IntPtr.Zero;
    }
}
