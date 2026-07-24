using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed partial class CursorWatchdogAcceptanceTests : IsolatedHostSettingsTest
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

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
    public void RecoveryRejectsInvalidProcessIds(string processId)
    {
        using var recovery = StartRecovery(processId, $"Local\\VolturaAir.Tests.Ready.{Guid.NewGuid():N}");

        Assert.True(recovery.WaitForExit(ProcessTimeout));
        Assert.Equal(1, recovery.ExitCode);
    }

    [Fact]
    public void RecoveryMetadataIdentifiesTheInternalArtifact()
    {
        var versionInfo = FileVersionInfo.GetVersionInfo(GetRecoveryPath());

        Assert.Equal("Voltura Air Cursor Recovery Watchdog", versionInfo.FileDescription);
        Assert.Equal("Voltura Air", versionInfo.ProductName);
        Assert.Equal("Voltura AB", versionInfo.CompanyName);
        Assert.Equal("VolturaAir.CursorWatchdog", versionInfo.InternalName);
        Assert.Equal("VolturaAir.CursorWatchdog.exe", versionInfo.OriginalFilename);
    }

    [Fact]
    public void MissingRecoveryPreventsReadiness()
    {
        using var recovery = new CursorWatchdogService(
            Path.Combine(Path.GetTempPath(), $"missing-recovery-{Guid.NewGuid():N}.exe"),
            Environment.ProcessId);

        var exception = Assert.Throws<CursorRecoveryUnavailableException>(
            () => recovery.TryEnsureMonitoring(TimeSpan.Zero));

        Assert.Equal("Cursor recovery is unavailable.", exception.Message);
    }

    [Fact]
    public async Task MissingRecoveryKeepsTheHostUsableAndCustomPointerOff()
    {
        var enabled = new CustomPointerSettings(true, 6, 0x12A894);
        AppPointerSettings.SetCustomPointer(enabled);
        using var recovery = new CursorWatchdogService(
            Path.Combine(Path.GetTempPath(), $"missing-recovery-{Guid.NewGuid():N}.exe"),
            Environment.ProcessId);
        var pointer = new CustomPointerService();
        using var coordinator = new CursorOverrideCoordinator(recovery, pointer, NullAppLog.Instance);

        await coordinator.StartAsync();

        Assert.False(coordinator.IsRecoveryReady);
        Assert.False(AppPointerSettings.GetCustomPointer().Enabled);
        var exception = Assert.Throws<CursorRecoveryUnavailableException>(
            () => coordinator.ApplyCustomPointer(enabled));
        Assert.Equal("Cursor overrides are temporarily unavailable.", exception.Message);
    }

    [Fact]
    public void ReadyRecoveryIsReusedAndParentedOutsideTheHost()
    {
        using var host = StartSleepingHost();
        using var recovery = new CursorWatchdogService(GetRecoveryPath(), host.Id);
        try
        {
            Assert.True(recovery.TryEnsureMonitoring());
            var processId = Assert.IsType<int>(recovery.MonitorProcessId);

            Assert.True(recovery.TryEnsureMonitoring());
            Assert.Equal(processId, recovery.MonitorProcessId);

            using var monitor = Process.GetProcessById(processId);
            Assert.NotEqual(Environment.ProcessId, GetParentProcessId(monitor));
            Assert.NotEqual(host.Id, GetParentProcessId(monitor));
        }
        finally
        {
            StopProcess(host);
        }
    }

    [Fact]
    public async Task RecoveryLossIsReported()
    {
        using var host = StartSleepingHost();
        using var recovery = new CursorWatchdogService(GetRecoveryPath(), host.Id);
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recovery.MonitoringLost += (_, _) => lost.TrySetResult();
        try
        {
            Assert.True(recovery.TryEnsureMonitoring());
            using var monitor = Process.GetProcessById(Assert.IsType<int>(recovery.MonitorProcessId));

            monitor.Kill();

            await lost.Task.WaitAsync(ProcessTimeout);
            Assert.False(recovery.IsReady);
            Assert.Null(recovery.MonitorProcessId);
        }
        finally
        {
            StopProcess(host);
        }
    }

    [Fact]
    public void ReplacementWaitsForThePreviousSessionRecovery()
    {
        using var firstHost = StartSleepingHost();
        using var secondHost = StartSleepingHost();
        var firstRecovery = new CursorWatchdogService(GetRecoveryPath(), firstHost.Id);
        using var secondRecovery = new CursorWatchdogService(GetRecoveryPath(), secondHost.Id);
        var firstMonitorProcessId = 0;
        try
        {
            Assert.True(firstRecovery.TryEnsureMonitoring());
            firstMonitorProcessId = Assert.IsType<int>(firstRecovery.MonitorProcessId);
            firstRecovery.Dispose();

            Assert.False(secondRecovery.TryEnsureMonitoring(TimeSpan.FromMilliseconds(100)));

            StopProcess(firstHost);

            Assert.True(secondRecovery.TryEnsureMonitoring(ProcessTimeout));
            Assert.True(WaitForProcessExit(firstMonitorProcessId, ProcessTimeout));
        }
        finally
        {
            firstRecovery.Dispose();
            StopProcess(firstHost);
            StopProcess(secondHost);
            if (firstMonitorProcessId != 0)
            {
                Assert.True(WaitForProcessExit(firstMonitorProcessId, ProcessTimeout));
            }
        }
    }

    [Fact]
    public void ForcedHostTreeTerminationRestoresTheCursorAndDoesNotKillRecovery()
    {
        Assert.True(CustomPointerService.RestoreWindowsCursorScheme());
        var originalCursor = GetCurrentCursorFingerprint();
        using var host = StartSleepingHost();
        var recovery = new CursorWatchdogService(GetRecoveryPath(), host.Id);
        using var pointer = new CustomPointerService();
        var monitorProcessId = 0;
        try
        {
            Assert.True(recovery.TryEnsureMonitoring());
            monitorProcessId = Assert.IsType<int>(recovery.MonitorProcessId);
            pointer.Apply(new CustomPointerSettings(true, 6, 0x12A894));
            Assert.NotEqual(originalCursor, GetCurrentCursorFingerprint());

            recovery.Dispose();
            ForceTerminateProcessTree(host);

            Assert.True(
                WaitForCursorFingerprint(originalCursor, ProcessTimeout),
                "The independent recovery process did not restore the Windows cursor scheme.");
            Assert.True(
                WaitForProcessExit(monitorProcessId, ProcessTimeout),
                "The recovery process did not exit after restoring the cursor.");
        }
        finally
        {
            pointer.Restore();
            recovery.Dispose();
            StopProcess(host);
            if (monitorProcessId != 0)
            {
                Assert.True(WaitForProcessExit(monitorProcessId, ProcessTimeout));
            }
        }
    }

    [Fact]
    public async Task KillingRecoveryRevokesCustomPointerAndStartsAReplacement()
    {
        Assert.True(CustomPointerService.RestoreWindowsCursorScheme());
        var originalCursor = GetCurrentCursorFingerprint();
        using var host = StartSleepingHost();
        var recovery = new CursorWatchdogService(GetRecoveryPath(), host.Id);
        var pointer = new CustomPointerService();
        using var coordinator = new CursorOverrideCoordinator(recovery, pointer, NullAppLog.Instance);
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.OverridesRevoked += (_, _) => revoked.TrySetResult();
        try
        {
            await coordinator.StartAsync();
            Assert.True(coordinator.IsRecoveryReady);
            var firstMonitorProcessId = Assert.IsType<int>(recovery.MonitorProcessId);
            var custom = new CustomPointerSettings(true, 6, 0x12A894);
            coordinator.ApplyCustomPointer(custom);
            AppPointerSettings.SetCustomPointer(custom);
            Assert.NotEqual(originalCursor, GetCurrentCursorFingerprint());

            using (var monitor = Process.GetProcessById(firstMonitorProcessId))
            {
                monitor.Kill();
            }

            await revoked.Task.WaitAsync(ProcessTimeout);
            Assert.True(WaitForCursorFingerprint(originalCursor, ProcessTimeout));
            Assert.False(AppPointerSettings.GetCustomPointer().Enabled);
            Assert.True(WaitUntil(
                () => recovery.IsReady && recovery.MonitorProcessId is int next && next != firstMonitorProcessId,
                ProcessTimeout));
        }
        finally
        {
            StopProcess(host);
        }
    }

    [Fact]
    public async Task KillingRecoveryStopsPresentationLaser()
    {
        Assert.True(CustomPointerService.RestoreWindowsCursorScheme());
        var originalCursor = GetCurrentCursorFingerprint();
        using var host = StartSleepingHost();
        var recovery = new CursorWatchdogService(GetRecoveryPath(), host.Id);
        var pointer = new CustomPointerService();
        using var coordinator = new CursorOverrideCoordinator(recovery, pointer, NullAppLog.Instance);
        using var laser = new PresentationLaserPointerController(coordinator.SetPresentationLaserPointer);
        coordinator.OverridesRevoked += (_, _) => laser.Revoke();
        try
        {
            await coordinator.StartAsync();
            laser.SetEnabled("client-a", enabled: true);
            Assert.True(laser.IsEnabled);
            Assert.NotEqual(originalCursor, GetCurrentCursorFingerprint());
            using var monitor = Process.GetProcessById(Assert.IsType<int>(recovery.MonitorProcessId));

            monitor.Kill();

            Assert.True(WaitUntil(() => !laser.IsEnabled, ProcessTimeout));
            Assert.True(WaitForCursorFingerprint(originalCursor, ProcessTimeout));
            Assert.False(AppPointerSettings.GetCustomPointer().Enabled);
        }
        finally
        {
            StopProcess(host);
        }
    }

    private static string GetRecoveryPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "VolturaAir.CursorWatchdog.exe");
        Assert.True(File.Exists(path), $"The packaged cursor recovery executable was not found at {path}.");
        return path;
    }

    private static Process StartRecovery(string processId, string readyEventName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetRecoveryPath(),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(processId);
        startInfo.ArgumentList.Add(readyEventName);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Cursor recovery did not start.");
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
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The recovery test host did not start.");
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
        Assert.True(taskkill.WaitForExit(ProcessTimeout));
        Assert.Equal(0, taskkill.ExitCode);
        Assert.True(process.WaitForExit(ProcessTimeout));
    }

    private static void StopProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(ProcessTimeout));
        }
    }

    private static bool WaitForProcessExit(int processId, TimeSpan timeout) =>
        WaitUntil(() =>
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }, timeout);

    private static bool WaitForCursorFingerprint(string expected, TimeSpan timeout) =>
        WaitUntil(() => string.Equals(GetCurrentCursorFingerprint(), expected, StringComparison.Ordinal), timeout);

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return condition();
    }

    private static string GetCurrentCursorFingerprint()
    {
        var cursor = LoadCursor(nint.Zero, (nint)32512);
        Assert.NotEqual(nint.Zero, cursor);
        var cursorCopy = CopyIcon(cursor);
        Assert.NotEqual(nint.Zero, cursorCopy);
        try
        {
            using var icon = Icon.FromHandle(cursorCopy);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
        }
        finally
        {
            Assert.True(DestroyIcon(cursorCopy));
        }
    }

    private static int GetParentProcessId(Process process)
    {
        var information = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(
            process.Handle,
            processInformationClass: 0,
            ref information,
            (uint)Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        Assert.Equal(0, status);
        return checked((int)information.InheritedFromUniqueProcessId);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        internal nint Reserved1;
        internal nint PebBaseAddress;
        internal nint Reserved2A;
        internal nint Reserved2B;
        internal nint UniqueProcessId;
        internal nint InheritedFromUniqueProcessId;
    }

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CopyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        nint process,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        uint processInformationLength,
        out uint returnLength);
}
