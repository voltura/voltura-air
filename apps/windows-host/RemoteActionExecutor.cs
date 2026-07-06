using System.Diagnostics;

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

public sealed partial class RemoteActionExecutor : IRemoteActionExecutor
{
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
            if (!TryStartProcess(browser.ExecutableName, browser.BuildNewTabArguments(youtubeUrl)))
            {
                continue;
            }

            // The browser accepted the launch request. Wait until the YouTube tab is
            // visible before focusing/fullscreening so we do not fullscreen an old tab.
            TryActivateYoutubeBrowserWhenReady(browser.ProcessName, youtubeUrl, TimeSpan.FromSeconds(5));
            return true;
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

}
