using System.Diagnostics;

namespace VolturaAir.Host;

public static class RemoteLaunchActions
{
    public const string OpenYoutube = "openYoutube";
    public const string StartOrActivateKodi = "startOrActivateKodi";

    public static bool IsSupported(string action) =>
        action is OpenYoutube or StartOrActivateKodi;
}

public interface IRemoteActionExecutor
{
    Task<bool> TryExecuteAsync(string action, CancellationToken cancellationToken);
}

internal interface IRemoteLaunchAction
{
    Task<bool> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class RemoteActionExecutor : IRemoteActionExecutor
{
    private readonly IRemoteLaunchAction _youtube;
    private readonly IRemoteLaunchAction _kodi;

    public RemoteActionExecutor()
    {
        var windowActivator = new WindowsWindowActivator();
        var processLauncher = new ShellProcessLauncher();
        _youtube = new YoutubeRemoteAction(windowActivator, processLauncher);
        _kodi = new KodiRemoteAction(windowActivator, processLauncher);
    }

    internal RemoteActionExecutor(IRemoteLaunchAction youtube, IRemoteLaunchAction kodi)
    {
        _youtube = youtube;
        _kodi = kodi;
    }

    public Task<bool> TryExecuteAsync(string action, CancellationToken cancellationToken)
    {
        return action switch
        {
            RemoteLaunchActions.OpenYoutube => _youtube.ExecuteAsync(cancellationToken),
            RemoteLaunchActions.StartOrActivateKodi => _kodi.ExecuteAsync(cancellationToken),
            _ => Task.FromResult(false)
        };
    }
}

internal interface IRemoteProcessLauncher
{
    bool TryStart(string fileName, string? arguments);
}

internal sealed class ShellProcessLauncher : IRemoteProcessLauncher
{
    public bool TryStart(string fileName, string? arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}
