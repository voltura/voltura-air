using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VolturaAir.Host;

internal sealed class YoutubeRemoteAction(
    IWindowsWindowActivator windows,
    IRemoteProcessLauncher processLauncher) : IRemoteLaunchAction
{
    private const int YoutubeAddressWaitMilliseconds = 750;
    private static readonly TimeSpan YoutubeAddressWait = TimeSpan.FromMilliseconds(YoutubeAddressWaitMilliseconds);
    private static readonly Lock LastWindowGate = new();
    private static readonly BrowserCandidate[] BrowserCandidates =
    [
        new("chrome", "chrome.exe"),
        new("brave", "brave.exe"),
        new("opera", "opera.exe"),
        new("msedge", "msedge.exe")
    ];
    private static IntPtr _lastWindowHandle;

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        var youtubeUrl = AppRemoteSettings.GetYoutubeUrl();
        if (await TryActivateExistingTabAsync(youtubeUrl, cancellationToken))
        {
            return true;
        }

        foreach (var browser in BrowserCandidates)
        {
            if (!processLauncher.TryStart(browser.ExecutableName, BrowserCandidate.BuildNewTabArguments(youtubeUrl)))
            {
                continue;
            }

            return await TryActivateWhenReadyAsync(browser.ProcessName, youtubeUrl, TimeSpan.FromSeconds(5), cancellationToken) ||
                await TryActivateMostRecentWindowAsync(browser.ProcessName, ensureFullscreen: true, cancellationToken);
        }

        return false;
    }

    private async Task<bool> TryActivateWhenReadyAsync(
        string processName,
        string youtubeUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var youtubeHost = TryGetUriHost(youtubeUrl);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        do
        {
            var browserWindows = GetBrowserWindows(processName);
            foreach (var window in browserWindows)
            {
                if (await TryActivateWindowAsync(window.Handle, youtubeHost, window.Title, YoutubeAddressWait, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var window in browserWindows)
            {
                if (await TrySelectTabAsync(window.Handle, youtubeHost, cancellationToken))
                {
                    return true;
                }
            }

            await Task.Delay(100, cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private async Task<bool> TryActivateExistingTabAsync(string youtubeUrl, CancellationToken cancellationToken)
    {
        if (await TryActivateRememberedFullscreenWindowAsync(cancellationToken))
        {
            return true;
        }

        var youtubeHost = TryGetUriHost(youtubeUrl);
        foreach (var browser in BrowserCandidates)
        {
            var browserWindows = GetBrowserWindows(browser.ProcessName);
            foreach (var window in browserWindows)
            {
                if (IsYoutubeTabName(window.Title, youtubeHost) &&
                    await TryActivateWindowAsync(window.Handle, youtubeHost, window.Title, YoutubeAddressWait, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var window in browserWindows)
            {
                if (await TrySelectTabAsync(window.Handle, youtubeHost, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var window in browserWindows)
            {
                if (await TryActivateWindowAsync(window.Handle, youtubeHost, fallbackTitle: null, YoutubeAddressWait, cancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<bool> TryActivateRememberedFullscreenWindowAsync(CancellationToken cancellationToken)
    {
        var windowHandle = GetRememberedWindowHandle();
        if (!windows.IsWindowHandleAvailable(windowHandle))
        {
            ForgetRememberedWindow(windowHandle);
            return false;
        }

        if (!windows.IsBrowserFullscreen(windowHandle))
        {
            return false;
        }

        windows.TryBringWindowForwardPreservingState(windowHandle);
        await Task.Delay(150, cancellationToken);
        return true;
    }

    private async Task<bool> TryActivateMostRecentWindowAsync(
        string processName,
        bool ensureFullscreen,
        CancellationToken cancellationToken)
    {
        foreach (var window in GetBrowserWindows(processName))
        {
            if (!windows.TryActivateWindow(window.Handle))
            {
                continue;
            }

            if (ensureFullscreen)
            {
                await windows.EnsureBrowserFullscreenAsync(window.Handle, cancellationToken);
            }

            RememberWindow(window.Handle);
            return true;
        }

        return false;
    }

    private async Task<bool> TrySelectTabAsync(
        IntPtr browserWindowHandle,
        string? youtubeHost,
        CancellationToken cancellationToken)
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
                if (tabItems[index] is not AutomationElement tabItem || !IsYoutubeTabName(tabItem.Current.Name, youtubeHost))
                {
                    continue;
                }

                windows.TryActivateWindow(browserWindowHandle);
                if (tabItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && pattern is SelectionItemPattern selectionItemPattern)
                {
                    selectionItemPattern.Select();
                }
                else
                {
                    tabItem.SetFocus();
                }

                await Task.Delay(100, cancellationToken);
                return await TryActivateWindowAsync(
                    browserWindowHandle,
                    youtubeHost,
                    tabItem.Current.Name,
                    TimeSpan.FromSeconds(1),
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return false;
        }

        return false;
    }

    private async Task<bool> TryActivateWindowAsync(
        IntPtr browserWindowHandle,
        string? youtubeHost,
        string? fallbackTitle,
        TimeSpan addressWait,
        CancellationToken cancellationToken)
    {
        if (!windows.TryActivateWindow(browserWindowHandle))
        {
            return false;
        }

        var addressState = await WaitForActiveAddressAsync(browserWindowHandle, youtubeHost, addressWait, cancellationToken);
        if (addressState == YoutubeAddressState.Mismatch ||
            addressState == YoutubeAddressState.Unknown && !IsYoutubeTabName(fallbackTitle, youtubeHost))
        {
            return false;
        }

        RememberWindow(browserWindowHandle);
        await windows.EnsureBrowserFullscreenAsync(browserWindowHandle, cancellationToken);
        return true;
    }

    private static async Task<YoutubeAddressState> WaitForActiveAddressAsync(
        IntPtr browserWindowHandle,
        string? youtubeHost,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var sawReadableBrowserAddress = false;
        do
        {
            var addressState = TryGetActiveAddressState(browserWindowHandle, youtubeHost);
            if (addressState == YoutubeAddressState.Match)
            {
                return YoutubeAddressState.Match;
            }

            sawReadableBrowserAddress |= addressState == YoutubeAddressState.Mismatch;
            await Task.Delay(75, cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return sawReadableBrowserAddress ? YoutubeAddressState.Mismatch : YoutubeAddressState.Unknown;
    }

    private static YoutubeAddressState TryGetActiveAddressState(IntPtr browserWindowHandle, string? youtubeHost)
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
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return YoutubeAddressState.Unknown;
        }
    }

    private static BrowserWindow[] GetBrowserWindows(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return [.. processes
                .Select(TryCreateBrowserWindow)
                .Where(window => window is not null)
                .Select(window => window!.Value)
                .OrderByDescending(window => window.StartTime)];
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static BrowserWindow? TryCreateBrowserWindow(Process process)
    {
        try
        {
            var handle = process.MainWindowHandle;
            return handle == IntPtr.Zero
                ? null
                : new BrowserWindow(handle, process.MainWindowTitle, GetStartTimeSafe(process));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool LooksLikeBrowserAddressValue(AutomationElement editField, string value)
    {
        var name = editField.Current.Name;
        if (!string.IsNullOrWhiteSpace(name) &&
            (name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("adress", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("url", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("omnibox", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsYoutubeAddressValue(string value, string? youtubeHost) =>
        value.Trim().Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(youtubeHost) && value.Contains(youtubeHost, StringComparison.OrdinalIgnoreCase);

    private static bool IsYoutubeTabName(string? name, string? youtubeHost) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
         !string.IsNullOrWhiteSpace(youtubeHost) && name.Contains(youtubeHost, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetUriHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static DateTime GetStartTimeSafe(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return DateTime.MinValue;
        }
    }

    private static void RememberWindow(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            lock (LastWindowGate)
            {
                _lastWindowHandle = windowHandle;
            }
        }
    }

    private static IntPtr GetRememberedWindowHandle()
    {
        lock (LastWindowGate)
        {
            return _lastWindowHandle;
        }
    }

    private static void ForgetRememberedWindow(IntPtr windowHandle)
    {
        lock (LastWindowGate)
        {
            if (_lastWindowHandle == windowHandle)
            {
                _lastWindowHandle = IntPtr.Zero;
            }
        }
    }

    private static string QuoteProcessArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private enum YoutubeAddressState
    {
        Match,
        Mismatch,
        Unknown
    }

    private sealed record BrowserCandidate(string ProcessName, string ExecutableName)
    {
        public static string BuildNewTabArguments(string url) =>
            $"--new-tab --start-fullscreen {QuoteProcessArgument(url)}";
    }

    private readonly record struct BrowserWindow(IntPtr Handle, string Title, DateTime StartTime);
}
