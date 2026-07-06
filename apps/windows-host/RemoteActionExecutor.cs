using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VolturaAir.Host;

public static class RemoteLaunchActions
{
    public const string OpenYoutube = "openYoutube";
    public const string StartOrActivateKodi = "startOrActivateKodi";

    public static bool IsSupported(string action)
    {
        return action is OpenYoutube or StartOrActivateKodi;
    }
}

public interface IRemoteActionExecutor
{
    bool TryExecute(string action);
}

public sealed class RemoteActionExecutor : IRemoteActionExecutor
{
    private static readonly TimeSpan KodiStartWait = TimeSpan.FromSeconds(5);

    public bool TryExecute(string action)
    {
        return action switch
        {
            RemoteLaunchActions.OpenYoutube => TryOpenYoutube(),
            RemoteLaunchActions.StartOrActivateKodi => TryStartOrActivateKodi(),
            _ => false
        };
    }

    private static bool TryOpenYoutube()
    {
        if (!TryStartProcess("chrome.exe", AppRemoteSettings.GetYoutubeUrl()))
        {
            return false;
        }

        TryActivateChromeWhenReady(TimeSpan.FromSeconds(2));
        return true;
    }

    private static bool TryStartOrActivateKodi()
    {
        if (TryActivateRunningKodi())
        {
            return true;
        }

        foreach (var candidate in GetKodiLaunchCandidates())
        {
            if (TryStartProcess(candidate.FileName, candidate.Arguments) && TryActivateKodiWhenReady(KodiStartWait))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryActivateRunningKodi()
    {
        var existing = Process.GetProcessesByName("kodi")
            .OrderByDescending(process => process.MainWindowHandle != IntPtr.Zero)
            .FirstOrDefault();

        if (existing is null)
        {
            return false;
        }

        if (existing.MainWindowHandle == IntPtr.Zero)
        {
            return TryActivateKodiWhenReady(TimeSpan.FromSeconds(2));
        }

        return TryActivateWindow(existing.MainWindowHandle, maximize: true);
    }

    private static bool TryActivateKodiWhenReady(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        do
        {
            var existing = Process.GetProcessesByName("kodi")
                .OrderByDescending(process => process.MainWindowHandle != IntPtr.Zero)
                .FirstOrDefault();

            if (existing is not null)
            {
                existing.Refresh();
                if (existing.MainWindowHandle == IntPtr.Zero)
                {
                    try
                    {
                        existing.WaitForInputIdle(milliseconds: 250);
                    }
                    catch (InvalidOperationException)
                    {
                        // Kodi may not expose an input-idle state while still starting.
                    }
                }

                existing.Refresh();
                if (existing.MainWindowHandle != IntPtr.Zero)
                {
                    return TryActivateWindow(existing.MainWindowHandle, maximize: true);
                }

                return false;
            }

            Thread.Sleep(150);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static IEnumerable<LaunchCandidate> GetKodiLaunchCandidates()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in GetKodiExecutableCandidates())
        {
            if (yielded.Add(path))
            {
                yield return new LaunchCandidate(path, null);
            }
        }

        foreach (var shortcut in GetKodiStartMenuShortcutCandidates())
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

    private static IEnumerable<string> GetKodiExecutableCandidates()
    {
        foreach (var path in GetKodiAppPathRegistryCandidates())
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
            var windowsAppsAlias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "kodi.exe");
            if (File.Exists(windowsAppsAlias))
            {
                yield return windowsAppsAlias;
            }
        }
    }

    private static IEnumerable<string> GetKodiAppPathRegistryCandidates()
    {
        const string appPathSubKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\kodi.exe";
        var registryRoots = new[]
        {
            Registry.CurrentUser,
            Registry.LocalMachine
        };

        foreach (var root in registryRoots)
        {
            using var key = root.OpenSubKey(appPathSubKey);
            foreach (var path in GetKodiPathsFromRegistryKey(key))
            {
                yield return path;
            }
        }

        using var wow6432Key = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\kodi.exe");
        foreach (var path in GetKodiPathsFromRegistryKey(wow6432Key))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> GetKodiPathsFromRegistryKey(RegistryKey? key)
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
            var expandedPath = Environment.ExpandEnvironmentVariables(pathValue);
            yield return Path.Combine(expandedPath, "kodi.exe");
        }
    }

    private static IEnumerable<string> GetKodiStartMenuShortcutCandidates()
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

    private static bool TryStartProcess(string fileName, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    private static bool TryActivateChromeWhenReady(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        do
        {
            var existing = Process.GetProcessesByName("chrome")
                .Where(process => process.MainWindowHandle != IntPtr.Zero)
                .OrderByDescending(GetStartTimeSafe)
                .FirstOrDefault();

            if (existing is not null)
            {
                return TryActivateWindow(existing.MainWindowHandle);
            }

            Thread.Sleep(100);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static DateTime GetStartTimeSafe(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return DateTime.MinValue;
        }
    }

    private static bool TryActivateWindow(IntPtr windowHandle, bool maximize = false)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        ShowWindow(windowHandle, maximize ? ShowWindowMaximize : ShowWindowRestore);
        BringWindowToTop(windowHandle);
        SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
        SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
        return SetForegroundWindow(windowHandle) || GetForegroundWindow() == windowHandle;
    }

    private const string KodiStoreAppShellId = @"shell:AppsFolder\XBMCFoundation.Kodi_4n2hpmxwrvr6p!Kodi";
    private const int ShowWindowMaximize = 3;
    private const int ShowWindowRestore = 9;
    private const int SetWindowPosNoSize = 0x0001;
    private const int SetWindowPosNoMove = 0x0002;
    private const int SetWindowPosShowWindow = 0x0040;
    private static readonly nint TopMostWindow = -1;
    private static readonly nint NoTopMostWindow = -2;

    private sealed record LaunchCandidate(string FileName, string? Arguments);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
