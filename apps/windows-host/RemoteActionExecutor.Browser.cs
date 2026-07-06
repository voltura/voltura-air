using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VolturaAir.Host;

public sealed partial class RemoteActionExecutor
{
    private static bool TryActivateYoutubeBrowserWhenReady(string processName, string youtubeUrl, TimeSpan timeout)
    {
        var youtubeHost = TryGetUriHost(youtubeUrl);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        do
        {
            var browserWindows = GetBrowserWindows(processName);
            foreach (var process in browserWindows)
            {
                if (TryActivateYoutubeBrowserWindow(process.MainWindowHandle, youtubeHost, process.MainWindowTitle, YoutubeAddressWait))
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
            var browserWindows = GetBrowserWindows(browser.ProcessName);

            foreach (var process in browserWindows)
            {
                if (IsYoutubeBrowserTabName(process.MainWindowTitle, youtubeHost)
                    && TryActivateYoutubeBrowserWindow(process.MainWindowHandle, youtubeHost, process.MainWindowTitle, YoutubeAddressWait))
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

            foreach (var process in browserWindows)
            {
                if (TryActivateYoutubeBrowserWindow(process.MainWindowHandle, youtubeHost, fallbackTitle: null, YoutubeAddressWait))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Process[] GetBrowserWindows(string processName)
    {
        return Process.GetProcessesByName(processName)
            .Where(process => process.MainWindowHandle != IntPtr.Zero)
            .OrderByDescending(GetStartTimeSafe)
            .ToArray();
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
                return TryActivateYoutubeBrowserWindow(browserWindowHandle, youtubeHost, tabItem.Current.Name, TimeSpan.FromSeconds(1));
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return false;
        }

        return false;
    }

    private static bool TryActivateYoutubeBrowserWindow(IntPtr browserWindowHandle, string? youtubeHost, string? fallbackTitle, TimeSpan addressWait)
    {
        if (!TryActivateWindow(browserWindowHandle))
        {
            return false;
        }

        var addressState = WaitForActiveYoutubeAddress(browserWindowHandle, youtubeHost, addressWait);
        if (addressState == YoutubeAddressState.Mismatch)
        {
            return false;
        }

        if (addressState == YoutubeAddressState.Unknown && !IsYoutubeBrowserTabName(fallbackTitle, youtubeHost))
        {
            return false;
        }

        EnsureBrowserFullscreen(browserWindowHandle);
        return true;
    }

    private static YoutubeAddressState WaitForActiveYoutubeAddress(IntPtr browserWindowHandle, string? youtubeHost, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var sawReadableBrowserAddress = false;

        do
        {
            var addressState = TryGetActiveYoutubeAddressState(browserWindowHandle, youtubeHost);
            if (addressState == YoutubeAddressState.Match)
            {
                return YoutubeAddressState.Match;
            }

            sawReadableBrowserAddress |= addressState == YoutubeAddressState.Mismatch;
            Thread.Sleep(75);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return sawReadableBrowserAddress ? YoutubeAddressState.Mismatch : YoutubeAddressState.Unknown;
    }

    private static YoutubeAddressState TryGetActiveYoutubeAddressState(IntPtr browserWindowHandle, string? youtubeHost)
    {
        try
        {
            var browserWindow = AutomationElement.FromHandle(browserWindowHandle);
            if (browserWindow is null)
            {
                return YoutubeAddressState.Unknown;
            }

            var editFields = browserWindow.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            var sawReadableBrowserAddress = false;
            foreach (AutomationElement editField in editFields)
            {
                if (!editField.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern valuePattern)
                {
                    continue;
                }

                var value = valuePattern.Current.Value;
                if (string.IsNullOrWhiteSpace(value) || !LooksLikeBrowserAddressValue(editField, value))
                {
                    continue;
                }

                sawReadableBrowserAddress = true;
                if (IsYoutubeAddressValue(value, youtubeHost))
                {
                    return YoutubeAddressState.Match;
                }
            }

            return sawReadableBrowserAddress ? YoutubeAddressState.Mismatch : YoutubeAddressState.Unknown;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return YoutubeAddressState.Unknown;
        }
    }

    private static bool LooksLikeBrowserAddressValue(AutomationElement editField, string value)
    {
        var name = editField.Current.Name;
        if (!string.IsNullOrWhiteSpace(name)
            && (name.Contains("address", StringComparison.OrdinalIgnoreCase)
                || name.Contains("url", StringComparison.OrdinalIgnoreCase)
                || name.Contains("omnibox", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var trimmed = value.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsYoutubeAddressValue(string value, string? youtubeHost)
    {
        var trimmed = value.Trim();
        return trimmed.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(youtubeHost) && trimmed.Contains(youtubeHost, StringComparison.OrdinalIgnoreCase));
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

    private static string QuoteProcessArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private const int YoutubeAddressWaitMilliseconds = 750;

    private static readonly TimeSpan YoutubeAddressWait = TimeSpan.FromMilliseconds(YoutubeAddressWaitMilliseconds);

    private static readonly BrowserCandidate[] BrowserCandidates =
    [
        new("chrome", "chrome.exe"),
        new("brave", "brave.exe"),
        new("opera", "opera.exe"),
        new("msedge", "msedge.exe")
    ];

    private enum YoutubeAddressState
    {
        Match,
        Mismatch,
        Unknown
    }

    private sealed record BrowserCandidate(string ProcessName, string ExecutableName)
    {
        public string BuildNewTabArguments(string url)
        {
            return $"--new-tab {QuoteProcessArgument(url)}";
        }
    }
}
