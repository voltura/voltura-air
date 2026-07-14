using System.Diagnostics;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed record AppLaunchExecutionResult(bool Succeeded, string Code, string Message);

public interface IAppLaunchService
{
    IReadOnlyList<AppLaunchActionSummary> GetActions();

    AppLaunchExecutionResult Execute(string actionId);
}

public sealed class AppLaunchService : IAppLaunchService
{
    private const string BrowserStartUrl = "https://www.google.com";

    public IReadOnlyList<AppLaunchActionSummary> GetActions()
    {
        return AppLaunchSettings.GetActions()
            .Select(action => new AppLaunchActionSummary(action.Id, action.Label, ToProtocolKind(action.Kind)))
            .ToArray();
    }

    public AppLaunchExecutionResult Execute(string actionId)
    {
        var action = AppLaunchSettings.Find(actionId);
        if (action is null)
        {
            return new(false, "not-configured", "This launch button is no longer configured on the PC.");
        }

        if (action.Kind == AppLaunchKind.Custom && !AppLaunchSettings.TryValidateCustom(action, out _))
        {
            return new(false, "invalid-target", "The configured application path is no longer valid.");
        }

        try
        {
            var started = action.Kind switch
            {
                AppLaunchKind.Browser => StartShellTarget(BrowserStartUrl),
                AppLaunchKind.Spotify => TryStartSpotify(),
                AppLaunchKind.Vlc => TryStartRegisteredApplication("vlc.exe", GetKnownVlcPaths()),
                AppLaunchKind.PowerPoint => TryStartRegisteredApplication("powerpnt.exe", GetKnownPowerPointPaths()),
                AppLaunchKind.Custom => StartCustom(action),
                _ => false
            };

            return started
                ? new(true, "started", $"Started {action.Label}.")
                : new(false, "not-found", $"{action.Label} is not installed or could not be started.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new(false, "start-failed", $"Windows could not start {action.Label}.");
        }
    }

    private static bool StartCustom(AppLaunchAction action)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = action.ExecutablePath!,
            Arguments = action.Arguments ?? string.Empty,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(action.ExecutablePath) ?? string.Empty
        });
        return process is not null;
    }

    private static bool TryStartSpotify()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe")
        };

        return TryStartRegisteredApplication("spotify.exe", paths) || StartShellTarget("spotify:");
    }

    private static bool TryStartRegisteredApplication(string executableName, IEnumerable<string> fallbackPaths)
    {
        var registered = GetAppPath(executableName);
        if (!string.IsNullOrWhiteSpace(registered) && File.Exists(registered))
        {
            return StartExecutable(registered);
        }

        foreach (var path in fallbackPaths.Where(File.Exists))
        {
            if (StartExecutable(path))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetAppPath(string executableName)
    {
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(subKey, writable: false);
            if (key?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().Trim('"');
            }
        }

        return null;
    }

    private static IEnumerable<string> GetKnownVlcPaths()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe");
    }

    private static IEnumerable<string> GetKnownPowerPointPaths()
    {
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            yield return Path.Combine(root, "Microsoft Office", "root", "Office16", "POWERPNT.EXE");
            yield return Path.Combine(root, "Microsoft Office", "Office16", "POWERPNT.EXE");
        }
    }

    private static bool StartExecutable(string path)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = false });
        return process is not null;
    }

    private static bool StartShellTarget(string target)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        return process is not null;
    }

    private static string ToProtocolKind(AppLaunchKind kind)
    {
        return kind switch
        {
            AppLaunchKind.Browser => "browser",
            AppLaunchKind.Spotify => "spotify",
            AppLaunchKind.Vlc => "vlc",
            AppLaunchKind.PowerPoint => "powerpoint",
            _ => "custom"
        };
    }
}
