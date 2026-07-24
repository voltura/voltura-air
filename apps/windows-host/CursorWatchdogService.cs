using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

internal sealed class CursorRecoveryUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

internal sealed partial class CursorWatchdogService : IDisposable
{
    internal static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private readonly Lock _gate = new();
    private readonly string _watchdogPath;
    private readonly int _hostProcessId;
    private Process? _monitor;
    private EventWaitHandle? _readyEvent;
    private bool _ready;
    private bool _disposed;

    internal event EventHandler? MonitoringLost;

    internal bool IsReady
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _ready && _monitor is { HasExited: false };
            }
        }
    }

    internal int? MonitorProcessId
    {
        get
        {
            lock (_gate)
            {
                return _monitor is { HasExited: false } monitor ? monitor.Id : null;
            }
        }
    }

    public CursorWatchdogService()
        : this(
            Path.Combine(AppContext.BaseDirectory, "VolturaAir.CursorWatchdog.exe"),
            Environment.ProcessId)
    {
    }

    internal CursorWatchdogService(string watchdogPath, int hostProcessId)
    {
        _watchdogPath = watchdogPath;
        _hostProcessId = hostProcessId;
    }

    internal bool TryEnsureMonitoring(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? ReadyTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThan(effectiveTimeout, TimeSpan.Zero);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ready && _monitor is { HasExited: false })
            {
                return true;
            }

            if (_monitor is null || _monitor.HasExited)
            {
                ReleaseMonitorCore();
                StartMonitorCore();
            }

            var deadline = Stopwatch.GetTimestamp() +
                (long)(effectiveTimeout.TotalSeconds * Stopwatch.Frequency);
            while (_monitor is { HasExited: false } && _readyEvent is not null)
            {
                var remainingTicks = deadline - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0)
                {
                    return false;
                }

                var remaining = TimeSpan.FromSeconds(remainingTicks / (double)Stopwatch.Frequency);
                if (_readyEvent.WaitOne(remaining < TimeSpan.FromMilliseconds(50)
                        ? remaining
                        : TimeSpan.FromMilliseconds(50)))
                {
                    _ready = true;
                    return true;
                }
            }

            ReleaseMonitorCore();
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseMonitorCore();
        }
    }

    private void StartMonitorCore()
    {
        if (!File.Exists(_watchdogPath))
        {
            throw new CursorRecoveryUnavailableException("Cursor recovery is unavailable.");
        }

        var readyEventName =
            $"Local\\VolturaAir.CursorRecovery.Ready.{_hostProcessId.ToString(CultureInfo.InvariantCulture)}.{Guid.NewGuid():N}";
        _readyEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            readyEventName);

        try
        {
            _monitor = IndependentProcessLauncher.Start(
                _watchdogPath,
                [
                    _hostProcessId.ToString(CultureInfo.InvariantCulture),
                    readyEventName
                ]);
            _monitor.EnableRaisingEvents = true;
            _monitor.Exited += OnMonitorExited;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            ReleaseMonitorCore();
            throw new CursorRecoveryUnavailableException("Cursor recovery is unavailable.", ex);
        }
    }

    private void ReleaseMonitorCore()
    {
        var monitor = _monitor;
        _monitor = null;
        _ready = false;
        if (monitor is not null)
        {
            monitor.Exited -= OnMonitorExited;
            monitor.Dispose();
        }

        _readyEvent?.Dispose();
        _readyEvent = null;
    }

    private void OnMonitorExited(object? sender, EventArgs eventArgs)
    {
        bool notify;
        lock (_gate)
        {
            if (_disposed || sender is not Process monitor || !ReferenceEquals(monitor, _monitor))
            {
                return;
            }

            notify = _ready;
            ReleaseMonitorCore();
        }

        if (notify)
        {
            MonitoringLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private static partial class IndependentProcessLauncher
    {
        private const uint ProcessCreateProcess = 0x0080;
        private const uint CreateNoWindow = 0x08000000;
        private const uint ExtendedStartupInfoPresent = 0x00080000;
        private static readonly nuint ParentProcessAttribute = (nuint)0x00020000;

        internal static unsafe Process Start(string executablePath, IReadOnlyList<string> arguments)
        {
            var shellWindow = GetShellWindow();
            if (shellWindow == nint.Zero)
            {
                throw new Win32Exception("The Windows desktop shell is unavailable.");
            }

            _ = GetWindowThreadProcessId(shellWindow, out var shellProcessId);
            if (shellProcessId == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var shellProcess = OpenProcess(ProcessCreateProcess, inheritHandle: false, shellProcessId);
            if (shellProcess == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            nint attributeList = nint.Zero;
            nint parentProcessValue = nint.Zero;
            try
            {
                nuint attributeListSize = 0;
                _ = InitializeProcThreadAttributeList(
                    nint.Zero,
                    attributeCount: 1,
                    flags: 0,
                    ref attributeListSize);
                attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
                if (!InitializeProcThreadAttributeList(
                        attributeList,
                        attributeCount: 1,
                        flags: 0,
                        ref attributeListSize))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                parentProcessValue = Marshal.AllocHGlobal(nint.Size);
                Marshal.WriteIntPtr(parentProcessValue, shellProcess);
                if (!UpdateProcThreadAttribute(
                        attributeList,
                        flags: 0,
                        ParentProcessAttribute,
                        parentProcessValue,
                        (nuint)nint.Size,
                        nint.Zero,
                        nint.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var startupInfo = new StartupInfoEx
                {
                    StartupInfo = new StartupInfo
                    {
                        Size = (uint)Marshal.SizeOf<StartupInfoEx>()
                    },
                    AttributeList = attributeList
                };
                var commandLine = $"{BuildCommandLine(executablePath, arguments)}\0".ToCharArray();
                ProcessInformation processInformation;
                fixed (char* commandLinePointer = commandLine)
                {
                    if (!CreateProcess(
                            executablePath,
                            commandLinePointer,
                            nint.Zero,
                            nint.Zero,
                            inheritHandles: false,
                            CreateNoWindow | ExtendedStartupInfoPresent,
                            nint.Zero,
                            null,
                            ref startupInfo,
                            out processInformation))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }

                try
                {
                    return Process.GetProcessById(checked((int)processInformation.ProcessId));
                }
                finally
                {
                    _ = CloseHandle(processInformation.Thread);
                    _ = CloseHandle(processInformation.Process);
                }
            }
            finally
            {
                if (attributeList != nint.Zero)
                {
                    DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }

                if (parentProcessValue != nint.Zero)
                {
                    Marshal.FreeHGlobal(parentProcessValue);
                }

                _ = CloseHandle(shellProcess);
            }
        }

        private static string BuildCommandLine(string executablePath, IEnumerable<string> arguments) =>
            string.Join(' ', new[] { Quote(executablePath) }.Concat(arguments.Select(Quote)));

        private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            internal uint Size;
            internal nint Reserved;
            internal nint Desktop;
            internal nint Title;
            internal uint X;
            internal uint Y;
            internal uint XSize;
            internal uint YSize;
            internal uint XCountChars;
            internal uint YCountChars;
            internal uint FillAttribute;
            internal uint Flags;
            internal ushort ShowWindow;
            internal ushort Reserved2;
            internal nint Reserved2Pointer;
            internal nint StandardInput;
            internal nint StandardOutput;
            internal nint StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx
        {
            internal StartupInfo StartupInfo;
            internal nint AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            internal nint Process;
            internal nint Thread;
            internal uint ProcessId;
            internal uint ThreadId;
        }

        [LibraryImport("user32.dll")]
        private static partial nint GetShellWindow();

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool InitializeProcThreadAttributeList(
            nint attributeList,
            int attributeCount,
            uint flags,
            ref nuint size);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UpdateProcThreadAttribute(
            nint attributeList,
            uint flags,
            nuint attribute,
            nint value,
            nuint size,
            nint previousValue,
            nint returnSize);

        [LibraryImport("kernel32.dll")]
        private static partial void DeleteProcThreadAttributeList(nint attributeList);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool CreateProcess(
            string applicationName,
            char* commandLine,
            nint processAttributes,
            nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            nint environment,
            string? currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);
    }
}
