using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VolturaAir.Host;

public sealed partial class RemoteActionExecutor
{
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

    private static readonly BrowserCandidate[] BrowserCandidates =
    [
        new("chrome", "chrome.exe"),
        new("brave", "brave.exe"),
        new("opera", "opera.exe"),
        new("msedge", "msedge.exe")
    ];

    private sealed record BrowserCandidate(string ProcessName, string ExecutableName);
}
