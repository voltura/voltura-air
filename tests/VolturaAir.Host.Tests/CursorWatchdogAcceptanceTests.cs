using System.Diagnostics;
using System.Globalization;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class CursorWatchdogAcceptanceTests : IsolatedHostSettingsTest
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void HostBootstrapFailSafeExceedsTheNativeReadinessTimeout()
    {
        Assert.True(CursorWatchdogService.BootstrapExitTimeout > TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1x")]
    [InlineData("4294967296")]
    [InlineData("18446744073709551616")]
    public void WatchdogRejectsNonCanonicalOrOverflowingProcessIds(string processId)
    {
        using var watchdog = StartWatchdog(processId);

        Assert.True(watchdog.WaitForExit(ProcessTimeout), "The watchdog did not reject the invalid process ID promptly.");
        Assert.Equal(1, watchdog.ExitCode);
    }

    [Fact]
    public void WatchdogReportsMonitorStartupFailureWithoutWaitingForReadinessTimeout()
    {
        var stopwatch = Stopwatch.StartNew();
        using var watchdog = StartWatchdog(uint.MaxValue.ToString(CultureInfo.InvariantCulture));

        Assert.True(watchdog.WaitForExit(ProcessTimeout), "The watchdog launcher did not exit.");
        stopwatch.Stop();

        Assert.Equal(-4, watchdog.ExitCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"Early monitor failure took {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    }

    [Fact]
    public void WatchdogMetadataExplainsItsRecoveryPurpose()
    {
        var versionInfo = FileVersionInfo.GetVersionInfo(GetWatchdogPath());

        Assert.Equal("Voltura Air Cursor Recovery Watchdog", versionInfo.FileDescription);
        Assert.Equal("Restores the user's Windows cursor scheme if Voltura Air stops unexpectedly.", versionInfo.Comments);
        Assert.Equal("Voltura Air", versionInfo.ProductName);
        Assert.Equal("Voltura AB", versionInfo.CompanyName);
        Assert.Equal("VolturaAir.CursorWatchdog", versionInfo.InternalName);
        Assert.Equal("VolturaAir.CursorWatchdog.exe", versionInfo.OriginalFilename);
    }

    [Fact]
    public void MissingWatchdogPreventsCustomPointerApplication()
    {
        var watchdog = new CursorWatchdogService(
            Path.Combine(Path.GetTempPath(), $"missing-watchdog-{Guid.NewGuid():N}.exe"),
            Environment.ProcessId);
        using var pointer = new CustomPointerService(
            static () => true,
            watchdog.EnsureMonitoring,
            watchdog.StopMonitoring);

        var exception = Assert.Throws<CursorWatchdogUnavailableException>(() => pointer.Apply(
            new CustomPointerSettings(true, 6, AppPointerSettings.DefaultCustomPointerColor)));

        Assert.Contains("watchdog", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureMonitoringReusesTheLiveMonitor()
    {
        using var dummyHost = StartSleepingHost();
        using var service = new CursorWatchdogService(GetWatchdogPath(), dummyHost.Id);
        try
        {
            service.EnsureMonitoring();
            var firstMonitorProcessId = Assert.IsType<int>(service.MonitorProcessId);

            service.EnsureMonitoring();

            Assert.Equal(firstMonitorProcessId, service.MonitorProcessId);
        }
        finally
        {
            service.StopMonitoring();
            StopProcess(dummyHost);
        }
    }

    [Fact]
    public async Task NewMonitorWaitsForPreviousSameSessionMonitorToRestoreAndExit()
    {
        using var previousHost = StartSleepingHost();
        var previousService = new CursorWatchdogService(GetWatchdogPath(), previousHost.Id);
        try
        {
            previousService.EnsureMonitoring();
            var previousMonitorProcessId = Assert.IsType<int>(previousService.MonitorProcessId);
            previousService.Dispose();

            var stopPreviousHost = Task.Run(async () =>
            {
                await Task.Delay(100);
                previousHost.Kill();
                Assert.True(previousHost.WaitForExit(ProcessTimeout), "The previous watchdog test host did not exit.");
            });

            CursorWatchdogService.WaitForPreviousMonitors(ProcessTimeout);

            await stopPreviousHost;
            Assert.True(
                WaitForProcessExit(previousMonitorProcessId, ProcessTimeout),
                "The previous cursor watchdog did not finish before startup continued.");
        }
        finally
        {
            previousService.Dispose();
            StopProcess(previousHost);
        }
    }

    [Fact]
    public void NewMonitorRejectsAStuckPreviousSameSessionMonitorAfterBoundedWait()
    {
        using var previousHost = StartSleepingHost();
        var previousService = new CursorWatchdogService(GetWatchdogPath(), previousHost.Id);
        int? previousMonitorProcessId = null;
        try
        {
            previousService.EnsureMonitoring();
            previousMonitorProcessId = previousService.MonitorProcessId;
            previousService.Dispose();

            var exception = Assert.Throws<CursorWatchdogUnavailableException>(
                () => CursorWatchdogService.WaitForPreviousMonitors(TimeSpan.FromMilliseconds(100)));

            Assert.Contains("previous", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            StopProcess(previousHost);
            previousService.Dispose();
            if (previousMonitorProcessId is { } monitorProcessId)
            {
                Assert.True(
                    WaitForProcessExit(monitorProcessId, ProcessTimeout),
                    "The previous cursor watchdog did not exit during test cleanup.");
            }
        }
    }

    [Fact]
    public void FinalServiceDisposalLeavesTheMonitorUntilTheHostExits()
    {
        using var dummyHost = StartSleepingHost();
        var service = new CursorWatchdogService(GetWatchdogPath(), dummyHost.Id);
        try
        {
            service.EnsureMonitoring();
            var monitorProcessId = Assert.IsType<int>(service.MonitorProcessId);

            service.Dispose();

            using (var monitor = Process.GetProcessById(monitorProcessId))
            {
                Assert.False(monitor.HasExited);
            }

            StopProcess(dummyHost);
            Assert.True(
                WaitForProcessExit(monitorProcessId, ProcessTimeout),
                "The detached watchdog did not exit after its host process stopped.");
        }
        finally
        {
            service.Dispose();
            StopProcess(dummyHost);
        }
    }

    [Fact]
    public void WatchdogSurvivesForcedHostTreeTerminationAndRestoresCursor()
    {
        var watchdogPath = GetWatchdogPath();

        var readyFile = Path.Combine(Path.GetTempPath(), "VolturaAir.Tests", $"watchdog-ready-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
        var restoreCompletedEventName = $"Local\\VolturaAir.CursorWatchdog.Test.{Guid.NewGuid():N}";
        using var restoreCompletedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, restoreCompletedEventName);
        using var pointer = new CustomPointerService();
        using var dummyHost = StartDummyHost(watchdogPath, restoreCompletedEventName, readyFile);
        try
        {
            Assert.True(WaitForFile(readyFile, ProcessTimeout), "The watchdog monitor did not become ready.");
            var monitorProcessId = int.Parse(File.ReadAllText(readyFile), CultureInfo.InvariantCulture);
            pointer.Apply(new CustomPointerSettings(true, 6, AppPointerSettings.DefaultCustomPointerColor));

            var recoveryStopwatch = Stopwatch.StartNew();
            ForceTerminateProcessTree(dummyHost);

            Assert.True(
                restoreCompletedEvent.WaitOne(ProcessTimeout),
                "The watchdog did not survive the forced host tree termination and restore the cursor scheme.");
            recoveryStopwatch.Stop();
            Assert.True(
                recoveryStopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"Cursor recovery took {recoveryStopwatch.Elapsed.TotalMilliseconds:F0} ms after forced host termination.");
            Assert.True(
                WaitForProcessExit(monitorProcessId, ProcessTimeout),
                "The cursor watchdog did not exit after recovery.");
        }
        finally
        {
            pointer.Restore();
            if (!dummyHost.HasExited)
            {
                ForceTerminateProcessTree(dummyHost);
            }

            File.Delete(readyFile);
        }
    }

    private static Process StartDummyHost(string watchdogPath, string restoreCompletedEventName, string readyFile)
    {
        var escapedWatchdogPath = EscapePowerShellLiteral(watchdogPath);
        var escapedRestoreCompletedEventName = EscapePowerShellLiteral(restoreCompletedEventName);
        var escapedReadyFile = EscapePowerShellLiteral(readyFile);
        var command = $"$watchdog = Start-Process -FilePath '{escapedWatchdogPath}' " +
            $"-ArgumentList @($PID, '--restore-completed-event', '{escapedRestoreCompletedEventName}') " +
            "-WindowStyle Hidden -PassThru; " +
            "$watchdog.WaitForExit(); " +
            "if ($watchdog.ExitCode -le 0) { exit 1 }; " +
            $"[IO.File]::WriteAllText('{escapedReadyFile}', $watchdog.ExitCode.ToString([Globalization.CultureInfo]::InvariantCulture)); " +
            "Start-Sleep -Seconds 30";
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The dummy watchdog host did not start.");
    }

    private static string GetWatchdogPath()
    {
        var watchdogPath = Path.Combine(AppContext.BaseDirectory, "VolturaAir.CursorWatchdog.exe");
        Assert.True(File.Exists(watchdogPath), $"The packaged cursor watchdog was not found at {watchdogPath}.");
        return watchdogPath;
    }

    private static Process StartWatchdog(string processId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetWatchdogPath(),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(processId);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The cursor watchdog did not start.");
    }

    private static Process StartSleepingHost()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The watchdog test host did not start.");
    }

    private static void ForceTerminateProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/PID");
        startInfo.ArgumentList.Add(process.Id.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("/T");
        startInfo.ArgumentList.Add("/F");
        using var taskkill = Process.Start(startInfo) ?? throw new InvalidOperationException("taskkill did not start.");
        Assert.True(taskkill.WaitForExit(ProcessTimeout), "taskkill did not finish.");
        Assert.Equal(0, taskkill.ExitCode);
        Assert.True(process.WaitForExit(ProcessTimeout), "The dummy watchdog host did not exit.");
    }

    private static void StopProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(ProcessTimeout), "The watchdog test host did not exit.");
        }
    }

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return File.Exists(path);
    }

    private static bool WaitForProcessExit(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
