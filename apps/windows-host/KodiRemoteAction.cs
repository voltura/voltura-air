using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal sealed partial class KodiRemoteAction(
    IWindowsWindowActivator windows,
    IRemoteProcessLauncher processLauncher) : IRemoteLaunchAction, IDisposable
{
    private const string KodiProcessName = "kodi";

    private const string KodiUninstallRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Kodi";

    private const string KodiStoreAppUserModelId =
        @"XBMCFoundation.Kodi_4n2hpmxwrvr6p!Kodi";

    private static readonly Guid ApplicationActivationManagerClassId =
        new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    private static readonly TimeSpan WindowWaitTimeout =
        TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim executionGate = new(1, 1);

    private LaunchTarget? cachedLaunchTarget;
    private bool disposed;

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await executionGate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var existingWindow = FindKodiWindow();

            if (existingWindow != IntPtr.Zero)
            {
                return windows.TryActivateWindow(
                    existingWindow,
                    maximize: true);
            }

            var cachedTarget = GetValidCachedTarget();

            if (cachedTarget is not null)
            {
                var cachedResult = await TryLaunchAndActivateAsync(
                    cachedTarget,
                    cancellationToken);

                if (cachedResult == LaunchAttemptResult.Activated)
                {
                    return true;
                }

                if (cachedResult != LaunchAttemptResult.LaunchFailed)
                {
                    return false;
                }

                cachedLaunchTarget = null;
            }

            foreach (var target in DiscoverLaunchTargets())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (target == cachedTarget)
                {
                    continue;
                }

                var result = await TryLaunchAndActivateAsync(
                    target,
                    cancellationToken);

                if (result == LaunchAttemptResult.LaunchFailed)
                {
                    continue;
                }

                cachedLaunchTarget = target;

                return result == LaunchAttemptResult.Activated;
            }

            return false;
        }
        finally
        {
            executionGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        executionGate.Dispose();

        GC.SuppressFinalize(this);
    }

    private async Task<LaunchAttemptResult> TryLaunchAndActivateAsync(
        LaunchTarget target,
        CancellationToken cancellationToken)
    {
        if (!target.IsValid())
        {
            return LaunchAttemptResult.LaunchFailed;
        }

        using var windowWaiter = KodiWindowWaiter.TryCreate();

        if (windowWaiter is null)
        {
            return LaunchAttemptResult.WindowMonitoringFailed;
        }

        var launchResult = TryLaunch(target);

        if (!launchResult.Started)
        {
            return LaunchAttemptResult.LaunchFailed;
        }

        var windowHandle = await windowWaiter.WaitAsync(
            launchResult.ProcessId,
            WindowWaitTimeout,
            cancellationToken);

        if (windowHandle == IntPtr.Zero)
        {
            return LaunchAttemptResult.WindowNotFound;
        }

        return windows.TryActivateWindow(windowHandle, maximize: true)
            ? LaunchAttemptResult.Activated
            : LaunchAttemptResult.ActivationFailed;
    }

    private LaunchResult TryLaunch(LaunchTarget target)
    {
        return target.Kind switch
        {
            LaunchKind.Desktop => new LaunchResult(
                processLauncher.TryStart(
                    target.Value,
                    arguments: null),
                ProcessId: null),

            LaunchKind.Store => TryActivateStoreApplication(
                target.Value),

            _ => new LaunchResult(
                Started: false,
                ProcessId: null)
        };
    }

    private LaunchTarget? GetValidCachedTarget()
    {
        if (cachedLaunchTarget?.IsValid() == true)
        {
            return cachedLaunchTarget;
        }

        cachedLaunchTarget = null;
        return null;
    }

    private static IEnumerable<LaunchTarget> DiscoverLaunchTargets()
    {
        var desktopPath = GetNsisKodiExecutablePath();

        if (desktopPath is not null)
        {
            yield return LaunchTarget.Desktop(desktopPath);
        }

        yield return LaunchTarget.Store(KodiStoreAppUserModelId);
    }

    private static string? GetNsisKodiExecutablePath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            KodiUninstallRegistryPath,
            writable: false);

        var displayIcon = key?.GetValue("DisplayIcon") as string;

        if (string.IsNullOrEmpty(displayIcon))
        {
            return null;
        }

        var executablePath = displayIcon.Split(',')[0];

        return File.Exists(executablePath)
            ? executablePath
            : null;
    }

    private static LaunchResult TryActivateStoreApplication(
        string appUserModelId)
    {
        object? activationObject = null;

        try
        {
            var activationType = Type.GetTypeFromCLSID(
                ApplicationActivationManagerClassId,
                throwOnError: false);

            if (activationType is null)
            {
                return new LaunchResult(
                    Started: false,
                    ProcessId: null);
            }

            activationObject = Activator.CreateInstance(activationType);

            if (activationObject is not IApplicationActivationManager manager)
            {
                return new LaunchResult(
                    Started: false,
                    ProcessId: null);
            }

            var hresult = manager.ActivateApplication(
                appUserModelId,
                arguments: null,
                ActivateOptions.None,
                out var processId);

            return hresult >= 0
                ? new LaunchResult(
                    Started: true,
                    ProcessId: processId)
                : new LaunchResult(
                    Started: false,
                    ProcessId: null);
        }
        catch (COMException)
        {
            return new LaunchResult(
                Started: false,
                ProcessId: null);
        }
        catch (TypeLoadException)
        {
            return new LaunchResult(
                Started: false,
                ProcessId: null);
        }
        finally
        {
            if (activationObject is not null &&
                Marshal.IsComObject(activationObject))
            {
                Marshal.FinalReleaseComObject(activationObject);
            }
        }
    }

    private static IntPtr FindKodiWindow(
        uint? expectedProcessId = null)
    {
        var result = IntPtr.Zero;

        NativeMethods.EnumWindowsCallback callback =
            (windowHandle, _) =>
            {
                if (!IsKodiWindow(
                        windowHandle,
                        expectedProcessId))
                {
                    return 1;
                }

                result = windowHandle;
                return 0;
            };

        var callbackPointer =
            Marshal.GetFunctionPointerForDelegate(callback);

        _ = NativeMethods.EnumWindows(
            callbackPointer,
            IntPtr.Zero);

        GC.KeepAlive(callback);

        return result;
    }

    private static bool IsKodiWindow(
        IntPtr windowHandle,
        uint? expectedProcessId)
    {
        if (windowHandle == IntPtr.Zero ||
            NativeMethods.IsWindowVisible(windowHandle) == 0 ||
            NativeMethods.GetWindow(
                windowHandle,
                NativeMethods.GwOwner) != IntPtr.Zero)
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(
            windowHandle,
            out var processId);

        if (threadId == 0 || processId == 0)
        {
            return false;
        }

        if (expectedProcessId.HasValue &&
            processId != expectedProcessId.Value)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(
                checked((int)processId));

            return string.Equals(
                process.ProcessName,
                KodiProcessName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private sealed class KodiWindowWaiter : IDisposable
    {
        private readonly TaskCompletionSource<IntPtr> windowFound =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly NativeMethods.WinEventCallback callback;

        private IntPtr hookHandle;
        private uint? expectedProcessId;
        private bool disposed;

        private KodiWindowWaiter()
        {
            callback = OnWindowEvent;

            var callbackPointer =
                Marshal.GetFunctionPointerForDelegate(callback);

            hookHandle = NativeMethods.SetWinEventHook(
                NativeMethods.EventObjectCreate,
                NativeMethods.EventObjectShow,
                IntPtr.Zero,
                callbackPointer,
                processId: 0,
                threadId: 0,
                flags:
                    NativeMethods.WinEventOutOfContext |
                    NativeMethods.WinEventSkipOwnProcess);
        }

        public static KodiWindowWaiter? TryCreate()
        {
            var waiter = new KodiWindowWaiter();

            if (waiter.hookHandle != IntPtr.Zero)
            {
                return waiter;
            }

            waiter.Dispose();
            return null;
        }

        public async Task<IntPtr> WaitAsync(
            uint? processId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            expectedProcessId = processId;

            var existingWindow = FindKodiWindow(expectedProcessId);

            if (existingWindow != IntPtr.Zero)
            {
                return existingWindow;
            }

            using var timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            var timeoutTask = Task.Delay(
                timeout,
                timeoutCancellation.Token);

            var completedTask = await Task.WhenAny(
                windowFound.Task,
                timeoutTask);

            if (completedTask == windowFound.Task)
            {
                await timeoutCancellation.CancelAsync();
                return await windowFound.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (hookHandle != IntPtr.Zero)
            {
                _ = NativeMethods.UnhookWinEvent(hookHandle);
                hookHandle = IntPtr.Zero;
            }
        }

        private void OnWindowEvent(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime)
        {
            if (eventType is not (
                    NativeMethods.EventObjectCreate or
                    NativeMethods.EventObjectShow) ||
                objectId != NativeMethods.ObjIdWindow ||
                childId != NativeMethods.ChildIdSelf ||
                !IsKodiWindow(
                    windowHandle,
                    expectedProcessId))
            {
                return;
            }

            windowFound.TrySetResult(windowHandle);
        }
    }

    private sealed record LaunchTarget(
        LaunchKind Kind,
        string Value)
    {
        public static LaunchTarget Desktop(
            string executablePath)
        {
            return new LaunchTarget(
                LaunchKind.Desktop,
                executablePath);
        }

        public static LaunchTarget Store(
            string appUserModelId)
        {
            return new LaunchTarget(
                LaunchKind.Store,
                appUserModelId);
        }

        public bool IsValid()
        {
            return Kind switch
            {
                LaunchKind.Desktop => File.Exists(Value),
                LaunchKind.Store => !string.IsNullOrEmpty(Value),
                _ => false
            };
        }
    }

    private sealed record LaunchResult(
        bool Started,
        uint? ProcessId);

    private enum LaunchKind
    {
        Desktop,
        Store
    }

    private enum LaunchAttemptResult
    {
        Activated,
        ActivationFailed,
        LaunchFailed,
        WindowNotFound,
        WindowMonitoringFailed
    }

    [Flags]
    private enum ActivateOptions
    {
        None = 0
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)]
            string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)]
            string? arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)]
            string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)]
            string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)]
            string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    private static partial class NativeMethods
    {
        internal const uint EventObjectCreate = 0x8000;
        internal const uint EventObjectShow = 0x8002;

        internal const uint WinEventOutOfContext = 0x0000;
        internal const uint WinEventSkipOwnProcess = 0x0002;

        internal const int ObjIdWindow = 0;
        internal const int ChildIdSelf = 0;

        internal const uint GwOwner = 4;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int EnumWindowsCallback(
            IntPtr windowHandle,
            IntPtr parameter);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void WinEventCallback(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime);

        [LibraryImport(
            "user32.dll",
            SetLastError = true)]
        internal static partial int EnumWindows(
            IntPtr callback,
            IntPtr parameter);

        [LibraryImport("user32.dll")]
        internal static partial int IsWindowVisible(
            IntPtr windowHandle);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetWindow(
            IntPtr windowHandle,
            uint command);

        [LibraryImport(
            "user32.dll",
            SetLastError = true)]
        internal static partial uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [LibraryImport(
            "user32.dll",
            SetLastError = true)]
        internal static partial IntPtr SetWinEventHook(
            uint eventMinimum,
            uint eventMaximum,
            IntPtr eventHookModule,
            IntPtr callback,
            uint processId,
            uint threadId,
            uint flags);

        [LibraryImport(
            "user32.dll",
            SetLastError = true)]
        internal static partial int UnhookWinEvent(
            IntPtr hook);
    }
}
