using System.Diagnostics;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal sealed class KodiRemoteAction(
    IWindowsWindowActivator windows,
    IRemoteProcessLauncher processLauncher) : IRemoteLaunchAction
{
    private const string KodiStoreAppShellId = @"shell:AppsFolder\XBMCFoundation.Kodi_4n2hpmxwrvr6p!Kodi";
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(5);

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (await TryActivateRunningAsync(cancellationToken))
        {
            return true;
        }

        foreach (var candidate in GetLaunchCandidates())
        {
            if (processLauncher.TryStart(candidate.FileName, candidate.Arguments) &&
                await TryActivateWhenReadyAsync(StartWait, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryActivateRunningAsync(CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName("kodi");
        try
        {
            var existing = processes
                .OrderByDescending(process => GetMainWindowHandleSafe(process) != IntPtr.Zero)
                .FirstOrDefault();
            if (existing is null)
            {
                return false;
            }

            var windowHandle = GetMainWindowHandleSafe(existing);
            return windowHandle == IntPtr.Zero
                ? await TryActivateWhenReadyAsync(TimeSpan.FromSeconds(2), cancellationToken)
                : windows.TryActivateWindow(windowHandle, maximize: true);
        }
        finally
        {
            DisposeProcesses(processes);
        }
    }

    private async Task<bool> TryActivateWhenReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        do
        {
            var processes = Process.GetProcessesByName("kodi");
            try
            {
                var existing = processes
                    .OrderByDescending(process => GetMainWindowHandleSafe(process) != IntPtr.Zero)
                    .FirstOrDefault();
                if (existing is not null)
                {
                    existing.Refresh();
                    if (GetMainWindowHandleSafe(existing) == IntPtr.Zero)
                    {
                        try
                        {
                            existing.WaitForInputIdle(milliseconds: 250);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }

                    existing.Refresh();
                    var windowHandle = GetMainWindowHandleSafe(existing);
                    return windowHandle != IntPtr.Zero && windows.TryActivateWindow(windowHandle, maximize: true);
                }
            }
            finally
            {
                DisposeProcesses(processes);
            }

            await Task.Delay(150, cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static IEnumerable<LaunchCandidate> GetLaunchCandidates()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetExecutableCandidates())
        {
            if (yielded.Add(path))
            {
                yield return new LaunchCandidate(path, null);
            }
        }

        foreach (var shortcut in GetStartMenuShortcutCandidates())
        {
            if (yielded.Add(shortcut))
            {
                yield return new LaunchCandidate(shortcut, null);
            }
        }

        if (yielded.Add(KodiStoreAppShellId))
        {
            yield return new LaunchCandidate("explorer.exe", KodiStoreAppShellId);
        }

        if (yielded.Add("kodi.exe"))
        {
            yield return new LaunchCandidate("kodi.exe", null);
        }
    }

    private static IEnumerable<string> GetExecutableCandidates()
    {
        foreach (var path in GetAppPathRegistryCandidates())
        {
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        foreach (var folder in GetDistinctExistingFolders(
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
        {
            var path = Path.Combine(folder, "Kodi", "kodi.exe");
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "kodi.exe");
            if (File.Exists(alias))
            {
                yield return alias;
            }
        }
    }

    private static IEnumerable<string> GetAppPathRegistryCandidates()
    {
        const string appPathSubKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\kodi.exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(appPathSubKey);
            foreach (var path in GetPathsFromRegistryKey(key))
            {
                yield return path;
            }
        }

        using var wow6432Key = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\kodi.exe");
        foreach (var path in GetPathsFromRegistryKey(wow6432Key))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> GetPathsFromRegistryKey(RegistryKey? key)
    {
        if (key is null)
        {
            yield break;
        }

        if (key.GetValue(null) is string defaultValue && !string.IsNullOrWhiteSpace(defaultValue))
        {
            yield return Environment.ExpandEnvironmentVariables(defaultValue);
        }

        if (key.GetValue("Path") is string pathValue && !string.IsNullOrWhiteSpace(pathValue))
        {
            yield return Path.Combine(Environment.ExpandEnvironmentVariables(pathValue), "kodi.exe");
        }
    }

    private static IEnumerable<string> GetStartMenuShortcutCandidates()
    {
        foreach (var folder in GetDistinctExistingFolders(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)))
        {
            foreach (var shortcut in Directory.EnumerateFiles(folder, "Kodi*.lnk", SearchOption.AllDirectories))
            {
                yield return shortcut;
            }
        }
    }

    private static IEnumerable<string> GetDistinctExistingFolders(params string?[] folders)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && yielded.Add(folder))
            {
                yield return folder;
            }
        }
    }

    private static IntPtr GetMainWindowHandleSafe(Process process)
    {
        try
        {
            return process.MainWindowHandle;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }

    private sealed record LaunchCandidate(string FileName, string? Arguments);
}
