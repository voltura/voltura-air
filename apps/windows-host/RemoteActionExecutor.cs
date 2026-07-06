using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
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
        var youtubeUrl = AppRemoteSettings.GetYoutubeUrl();
        if (TryActivateExistingYoutubeTab(youtubeUrl))
        {
            return true;
        }

        foreach (var browser in BrowserCandidates)
        {
            if (TryStartProcess(browser.ExecutableName, youtubeUrl))
            {
                TryActivateBrowserWhenReady(browser.ProcessName, TimeSpan.FromSeconds(2), ensureFullscreen: true);
                return true;
            }
        }

        return false;
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

    private static bool TryActivateBrowserWhenReady(string processName, TimeSpan timeout, bool ensureFullscreen = false)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        do
        {
            var existing = Process.GetProcessesByName(processName)
                .Where(process => process.MainWindowHandle != IntPtr.Zero)
                .OrderByDescending(GetStartTimeSafe)
                .FirstOrDefault();

            if (existing is not null)
            {
                return TryActivateBrowserWindow(existing.MainWindowHandle, ensureFullscreen);
            }

            Thread.Sleep(100);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static bool TryActivateExistingYoutubeTab(string youtubeUrl)
    {
        var youtubeHost = TryGetUriHost(youtubeUrl);

        foreach (var browser in BrowserCandidates)
        {
            var browserWindows = Process.GetProcessesByName(browser.ProcessName)
                .Where(process => process.MainWindowHandle != IntPtr.Zero)
                .OrderByDescending(GetStartTimeSafe)
                .ToArray();

            foreach (var process in browserWindows)
            {
                if (IsYoutubeBrowserTabName(process.MainWindowTitle, youtubeHost) && TryActivateBrowserWindow(process.MainWindowHandle, ensureFullscreen: true))
                {
                    return true;
                }
            }

            foreach (var process in browserWindows)
            {
                if (TrySelectYoutubeBrowserTab(process.MainWindowHandle, youtubeHost))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TrySelectYoutubeBrowserTab(IntPtr browserWindowHandle, string? youtubeHost)
    {
        try
        {
            var browserWindow = AutomationElement.FromHandle(browserWindowHandle);
            if (browserWindow is null)
            {
                return false;
            }

            var tabItems = browserWindow.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

            for (var index = tabItems.Count - 1; index >= 0; index--)
            {
                if (tabItems[index] is not AutomationElement tabItem || !IsYoutubeBrowserTabName(tabItem.Current.Name, youtubeHost))
                {
                    continue;
                }

                TryActivateWindow(browserWindowHandle);
                if (tabItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && pattern is SelectionItemPattern selectionItemPattern)
                {
                    selectionItemPattern.Select();
                }
                else
                {
                    tabItem.SetFocus();
                }

                Thread.Sleep(100);
                return TryActivateBrowserWindow(browserWindowHandle, ensureFullscreen: true);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return false;
        }

        return false;
    }

    private static string? TryGetUriHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private static bool IsYoutubeBrowserTabName(string? name, string? youtubeHost)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Contains("YouTube", StringComparison.OrdinalIgnoreCase)
            || name.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(youtubeHost) && name.Contains(youtubeHost, StringComparison.OrdinalIgnoreCase));
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

    private static bool TryActivateBrowserWindow(IntPtr windowHandle, bool ensureFullscreen = false)
    {
        var activated = TryActivateWindow(windowHandle);
        if (activated && ensureFullscreen)
        {
            EnsureBrowserFullscreen(windowHandle);
        }

        return activated;
    }

    private static void EnsureBrowserFullscreen(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || IsWindowFullscreen(windowHandle))
        {
            return;
        }

        Thread.Sleep(150);
        if (GetForegroundWindow() != windowHandle)
        {
            TryActivateWindow(windowHandle);
            Thread.Sleep(100);
        }

        if (GetForegroundWindow() == windowHandle && !IsWindowFullscreen(windowHandle))
        {
            PressVirtualKey(VirtualKeyF11);
        }
    }

    private static bool IsWindowFullscreen(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var windowRect))
        {
            return false;
        }

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance
            && Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance
            && Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance
            && Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }

    private static void PressVirtualKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
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

    private static readonly BrowserCandidate[] BrowserCandidates =
    [
        new("chrome", "chrome.exe"),
        new("brave", "brave.exe"),
        new("opera", "opera.exe"),
        new("msedge", "msedge.exe")
    ];

    private const string KodiStoreAppShellId = @"shell:AppsFolder\XBMCFoundation.Kodi_4n2hpmxwrvr6p!Kodi";
    private const int ShowWindowMaximize = 3;
    private const int ShowWindowRestore = 9;
    private const int SetWindowPosNoSize = 0x0001;
    private const int SetWindowPosNoMove = 0x0002;
    private const int SetWindowPosShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint KeyEventKeyUp = 0x0002;
    private const byte VirtualKeyF11 = 0x7A;
    private static readonly nint TopMostWindow = -1;
    private static readonly nint NoTopMostWindow = -2;

    private sealed record BrowserCandidate(string ProcessName, string ExecutableName);

    private sealed record LaunchCandidate(string FileName, string? Arguments);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

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

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
