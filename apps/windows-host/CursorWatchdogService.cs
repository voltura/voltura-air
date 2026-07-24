using System.Diagnostics;
using System.Globalization;

namespace VolturaAir.Host;

internal sealed class CursorWatchdogUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

internal sealed class CursorWatchdogService : IDisposable
{
    internal static readonly TimeSpan BootstrapExitTimeout = TimeSpan.FromSeconds(7);
    internal static readonly TimeSpan PreviousMonitorExitTimeout = TimeSpan.FromSeconds(5);
    private const string WatchdogProcessName = "VolturaAir.CursorWatchdog";
    private readonly Lock _gate = new();
    private readonly string _watchdogPath;
    private readonly int _hostProcessId;
    private Process? _monitor;
    private bool _disposed;

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

    public void EnsureMonitoring()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_monitor is { HasExited: false })
            {
                return;
            }

            _monitor?.Dispose();
            _monitor = null;
            WaitForPreviousMonitors();
            if (!File.Exists(_watchdogPath))
            {
                throw new CursorWatchdogUnavailableException("The cursor recovery watchdog is missing.");
            }

            try
            {
                using var bootstrap = Process.Start(CreateStartInfo(_watchdogPath, _hostProcessId)) ??
                    throw new CursorWatchdogUnavailableException("Windows did not start the cursor recovery watchdog.");
                if (!bootstrap.WaitForExit(BootstrapExitTimeout))
                {
                    bootstrap.Kill(entireProcessTree: true);
                    bootstrap.WaitForExit();
                    throw new CursorWatchdogUnavailableException("The cursor recovery watchdog did not become ready.");
                }

                if (bootstrap.ExitCode <= 0)
                {
                    throw new CursorWatchdogUnavailableException(
                        $"The cursor recovery watchdog failed to start (exit code {bootstrap.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
                }

                _monitor = Process.GetProcessById(bootstrap.ExitCode);
                if (_monitor.HasExited)
                {
                    _monitor.Dispose();
                    _monitor = null;
                    throw new CursorWatchdogUnavailableException("The cursor recovery watchdog exited during startup.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                throw new CursorWatchdogUnavailableException("Windows could not start the cursor recovery watchdog.", ex);
            }
        }
    }

    public void StopMonitoring()
    {
        lock (_gate)
        {
            StopMonitoringCore();
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

            DetachMonitoringCore();
            _disposed = true;
        }
    }

    internal static void WaitForPreviousMonitors(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? PreviousMonitorExitTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThan(effectiveTimeout, TimeSpan.Zero);

        using var currentProcess = Process.GetCurrentProcess();
        var currentSessionId = currentProcess.SessionId;
        var deadline = Stopwatch.GetTimestamp() + (long)(effectiveTimeout.TotalSeconds * Stopwatch.Frequency);

        while (true)
        {
            var monitors = Process.GetProcessesByName(WatchdogProcessName);
            try
            {
                var foundSameSessionMonitor = false;
                foreach (var monitor in monitors)
                {
                    if (!IsInSession(monitor, currentSessionId))
                    {
                        continue;
                    }

                    foundSameSessionMonitor = true;
                    var remaining = TimeSpan.FromSeconds(
                        Math.Max(0, deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
                    if (remaining == TimeSpan.Zero || !WaitForExit(monitor, remaining))
                    {
                        throw new CursorWatchdogUnavailableException(
                            "A previous cursor recovery watchdog did not finish restoring the Windows cursor scheme.");
                    }
                }

                if (!foundSameSessionMonitor)
                {
                    return;
                }
            }
            finally
            {
                foreach (var monitor in monitors)
                {
                    monitor.Dispose();
                }
            }
        }
    }

    private void StopMonitoringCore()
    {
        var monitor = _monitor;
        _monitor = null;
        if (monitor is null)
        {
            return;
        }

        try
        {
            if (!monitor.HasExited)
            {
                monitor.Kill();
                _ = monitor.WaitForExit(BootstrapExitTimeout);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A concurrently exiting monitor has already released its native resources.
        }
        finally
        {
            monitor.Dispose();
        }
    }

    private static bool IsInSession(Process process, int sessionId)
    {
        try
        {
            return !process.HasExited && process.SessionId == sessionId;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool WaitForExit(Process process, TimeSpan timeout)
    {
        try
        {
            return process.HasExited || process.WaitForExit(timeout);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void DetachMonitoringCore()
    {
        var monitor = _monitor;
        _monitor = null;
        monitor?.Dispose();
    }

    internal static ProcessStartInfo CreateStartInfo(string watchdogPath, int hostProcessId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = watchdogPath,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(hostProcessId.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }
}
