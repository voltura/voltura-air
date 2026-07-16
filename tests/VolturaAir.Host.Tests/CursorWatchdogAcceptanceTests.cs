using System.Diagnostics;
using System.Globalization;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class CursorWatchdogAcceptanceTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

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

        var exception = Assert.Throws<InvalidOperationException>(() => pointer.Apply(
            new CustomPointerSettings(true, 6, AppPointerSettings.DefaultCustomPointerColor)));

        Assert.Contains("watchdog", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WatchdogSurvivesForcedHostTreeTerminationAndRestoresCursor()
    {
        var watchdogPath = GetWatchdogPath();

        var readyFile = Path.Combine(Path.GetTempPath(), "VolturaAir.Tests", $"watchdog-ready-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
        var completionEventName = $"Local\\VolturaAir.CursorWatchdog.Test.{Guid.NewGuid():N}";
        using var completionEvent = new EventWaitHandle(false, EventResetMode.ManualReset, completionEventName);
        using var pointer = new CustomPointerService();
        using var dummyHost = StartDummyHost(watchdogPath, completionEventName, readyFile);
        try
        {
            Assert.True(WaitForFile(readyFile, ProcessTimeout), "The watchdog monitor did not become ready.");
            pointer.Apply(new CustomPointerSettings(true, 6, AppPointerSettings.DefaultCustomPointerColor));

            ForceTerminateProcessTree(dummyHost);

            Assert.True(
                completionEvent.WaitOne(ProcessTimeout),
                "The watchdog did not survive the forced host tree termination and restore the cursor scheme.");
            Assert.True(WaitForWatchdogExit(ProcessTimeout), "The cursor watchdog did not exit after recovery.");
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

    private static Process StartDummyHost(string watchdogPath, string completionEventName, string readyFile)
    {
        var escapedWatchdogPath = EscapePowerShellLiteral(watchdogPath);
        var escapedCompletionEventName = EscapePowerShellLiteral(completionEventName);
        var escapedReadyFile = EscapePowerShellLiteral(readyFile);
        var command = $"$watchdog = Start-Process -FilePath '{escapedWatchdogPath}' " +
            $"-ArgumentList @($PID, '--completion-event', '{escapedCompletionEventName}') " +
            "-WindowStyle Hidden -PassThru; " +
            "$watchdog.WaitForExit(); " +
            "if ($watchdog.ExitCode -le 0) { exit 1 }; " +
            $"[IO.File]::WriteAllText('{escapedReadyFile}', 'ready'); " +
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

    private static bool WaitForWatchdogExit(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var watchdog = Process.GetProcessesByName("VolturaAir.CursorWatchdog").SingleOrDefault();
            if (watchdog is null)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return Process.GetProcessesByName("VolturaAir.CursorWatchdog").Length == 0;
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
