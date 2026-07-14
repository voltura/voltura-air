using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private const string SingleInstanceMutexName = @"Local\VolturaAir.CursorWatchdog";
    private const uint SpiSetCursors = 0x0057;

    private static int Main(string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var hostProcessId))
        {
            return 1;
        }

        using var singleInstance = new Mutex(false, SingleInstanceMutexName);
        var ownsSingleInstance = false;
        try
        {
            ownsSingleInstance = singleInstance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Ownership was transferred after the previous watchdog ended unexpectedly.
            ownsSingleInstance = true;
        }

        if (!ownsSingleInstance)
        {
            return 3;
        }

        try
        {
            using var hostProcess = Process.GetProcessById(hostProcessId);
            hostProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The host already ended.
        }
        catch (InvalidOperationException)
        {
            // The host ended while it was being obtained or inspected.
        }

        var restored = SystemParametersInfo(SpiSetCursors, 0, 0, 0);
        singleInstance.ReleaseMutex();
        return restored ? 0 : 2;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);
}
