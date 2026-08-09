using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VolturaAir.Host;

public sealed record AppLaunchExecutionResult(bool Succeeded, string Code, string Message);

public interface IAppLaunchService
{
    IReadOnlyList<AppLaunchActionSummary> GetActions();

    AppLaunchExecutionResult Execute(string actionId);

    AppLaunchExecutionResult ExecutePowerPointFile(string path);

    IReadOnlyList<KnownAppProfileSummary> GetKnownApplications() =>
        [.. KnownAppProfiles.All.Select(profile => new KnownAppProfileSummary(profile.Id, profile.Label, false))];

    AppLaunchExecutionResult ExecuteKnown(string profileId) =>
        new(false, "not-found", "This known application is unavailable on the PC.");
}

public sealed record KnownAppProfile(string Id, string Label, string ProcessName);

public sealed record KnownAppProfileSummary(string Id, string Label, bool Available);

public static class KnownAppProfiles
{
    private static readonly KnownAppProfile[] Items =
    [
        new("browser", "Browser", ""),
        new("spotify", "Spotify", "Spotify"),
        new("vlc", "VLC", "vlc"),
        new("zoom", "Zoom", "Zoom"),
        new("plex", "Plex", "Plex"),
        new("windowsPhotos", "Windows Photos", "Photos"),
        new("blender", "Blender", "blender")
    ];

    public static IReadOnlyList<KnownAppProfile> All => Items;

    public static KnownAppProfile? Find(string? id) =>
        Items.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.Ordinal));

    public static bool IsSupported(string? id) => Find(id) is not null;
}

public sealed partial class AppLaunchService : IAppLaunchService
{
    private const string BrowserStartUrl = "https://www.google.com";
    private static readonly TimeSpan KnownApplicationRefreshInterval = TimeSpan.FromSeconds(30);
    private KnownAppProfileSummary[] _knownApplications;
    private long _knownApplicationsRefreshedAt = Environment.TickCount64;
    private int _knownApplicationRefreshQueued;

    public AppLaunchService()
    {
        _knownApplications = [.. KnownAppProfiles.All.Select(profile => new KnownAppProfileSummary(
            profile.Id,
            profile.Label,
            IsKnownAvailable(profile.Id)))];
    }

    public IReadOnlyList<AppLaunchActionSummary> GetActions()
    {
        return [.. AppLaunchSettings.GetActions().Select(action => new AppLaunchActionSummary(action.Id, action.Label, ToProtocolKind(action.Kind)))];
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

    public AppLaunchExecutionResult ExecutePowerPointFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return new(false, "invalid-target", "The selected PowerPoint file is no longer available.");
        }

        try
        {
            var executable = FindRegisteredApplication("powerpnt.exe", GetKnownPowerPointPaths());
            if (executable is null)
            {
                return new(false, "not-found", "PowerPoint is not installed or could not be found.");
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                ArgumentList = { path }
            });
            return process is null
                ? new(false, "start-failed", "Windows could not start PowerPoint.")
                : new(true, "started", "PowerPoint is opening the selected presentation.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new(false, "start-failed", "Windows could not start PowerPoint.");
        }
    }

    public IReadOnlyList<KnownAppProfileSummary> GetKnownApplications()
    {
        QueueKnownApplicationRefreshIfStale();
        return Volatile.Read(ref _knownApplications);
    }

    public AppLaunchExecutionResult ExecuteKnown(string profileId)
    {
        var profile = KnownAppProfiles.Find(profileId);
        if (profile is null)
        {
            return new(false, "unsupported", "This known application profile is unsupported.");
        }

        try
        {
            if (TryFocusExisting(profile.ProcessName))
            {
                MarkKnownAvailable(profile.Id);
                return new(true, "focused", $"Focused {profile.Label}.");
            }

            var started = profile.Id switch
            {
                "browser" => StartShellTarget(BrowserStartUrl),
                "spotify" => TryStartSpotify(),
                "vlc" => TryStartRegisteredApplication("vlc.exe", GetKnownVlcPaths()),
                "zoom" => TryStartRegisteredApplication("Zoom.exe", GetKnownZoomPaths()),
                "plex" => TryStartRegisteredApplication("Plex.exe", GetKnownPlexPaths()),
                "windowsPhotos" => TryStartUriScheme("ms-photos"),
                "blender" => TryStartRegisteredApplication("blender.exe", GetKnownBlenderPaths()),
                _ => false
            };
            if (started)
            {
                MarkKnownAvailable(profile.Id);
                return new(true, "started", $"Started {profile.Label}.");
            }
            return new(false, "not-found", $"{profile.Label} is not installed or could not be started.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new(false, "start-failed", $"Windows could not start {profile.Label}.");
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

        return TryStartRegisteredApplication("spotify.exe", paths) || TryStartUriScheme("spotify");
    }

    private static bool TryStartRegisteredApplication(string executableName, IEnumerable<string> fallbackPaths)
    {
        var executable = FindRegisteredApplication(executableName, fallbackPaths);
        return executable is not null && StartExecutable(executable);
    }

    private static string? FindRegisteredApplication(string executableName, IEnumerable<string> fallbackPaths)
    {
        var registered = GetAppPath(executableName);
        if (!string.IsNullOrWhiteSpace(registered) && File.Exists(registered))
        {
            return registered;
        }

        return fallbackPaths.FirstOrDefault(File.Exists);
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

    private static IEnumerable<string> GetKnownZoomPaths()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zoom", "bin", "Zoom.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zoom", "bin", "Zoom.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Zoom", "bin", "Zoom.exe");
    }

    private static IEnumerable<string> GetKnownPlexPaths()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plex", "Plex.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Plex", "Plex.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Plex", "Plex.exe");
    }

    private static List<string> GetKnownBlenderPaths()
    {
        var paths = new List<string>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var root in roots)
        {
            try
            {
                var foundation = Path.Combine(root, "Blender Foundation");
                if (!Directory.Exists(foundation))
                {
                    continue;
                }

                foreach (var versionFolder in Directory.EnumerateDirectories(foundation))
                {
                    paths.Add(Path.Combine(versionFolder, "blender.exe"));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
        return paths;
    }

    private static bool IsKnownAvailable(string id)
    {
        try
        {
            return id switch
            {
                "browser" => true,
                "windowsPhotos" => IsUriSchemeRegistered("ms-photos"),
                "spotify" => Process.GetProcessesByName("Spotify").Length > 0 ||
                    GetKnownSpotifyPaths().Any(File.Exists) ||
                    IsUriSchemeRegistered("spotify"),
                "vlc" => Process.GetProcessesByName("vlc").Length > 0 ||
                    FindRegisteredApplication("vlc.exe", GetKnownVlcPaths()) is not null,
                "zoom" => Process.GetProcessesByName("Zoom").Length > 0 ||
                    FindRegisteredApplication("Zoom.exe", GetKnownZoomPaths()) is not null,
                "plex" => Process.GetProcessesByName("Plex").Length > 0 ||
                    FindRegisteredApplication("Plex.exe", GetKnownPlexPaths()) is not null,
                "blender" => Process.GetProcessesByName("blender").Length > 0 ||
                    FindRegisteredApplication("blender.exe", GetKnownBlenderPaths()) is not null,
                _ => false
            };
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or
                UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GetKnownSpotifyPaths()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe");
    }

    private static bool IsUriSchemeRegistered(string scheme)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(scheme, writable: false);
        if (key is null)
        {
            return false;
        }

        using var command = key.OpenSubKey(@"shell\open\command", writable: false);
        return HasUsableUriSchemeRegistration(
            key.GetValueNames().Contains("URL Protocol", StringComparer.OrdinalIgnoreCase),
            command?.GetValue(null) as string,
            command?.GetValue("DelegateExecute") as string);
    }

    internal static bool HasUsableUriSchemeRegistration(
        bool declaresUrlProtocol,
        string? command,
        string? delegateExecute) =>
        declaresUrlProtocol &&
        (!string.IsNullOrWhiteSpace(command) || !string.IsNullOrWhiteSpace(delegateExecute));

    private static bool TryStartUriScheme(string scheme) =>
        IsUriSchemeRegistered(scheme) && StartShellTarget($"{scheme}:");

    private static bool TryFocusExisting(string processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                nint handle;
                try
                {
                    handle = process.MainWindowHandle;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    continue;
                }
                if (handle == nint.Zero)
                {
                    continue;
                }

                if (IsIconic(handle))
                {
                    _ = ShowWindowAsync(handle, 9);
                }

                if (SetForegroundWindow(handle))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void QueueKnownApplicationRefreshIfStale()
    {
        if (Environment.TickCount64 - Volatile.Read(ref _knownApplicationsRefreshedAt) <
                KnownApplicationRefreshInterval.TotalMilliseconds ||
            Interlocked.CompareExchange(ref _knownApplicationRefreshQueued, 1, 0) != 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var refreshed = KnownAppProfiles.All.Select(profile =>
                    new KnownAppProfileSummary(
                        profile.Id,
                        profile.Label,
                        IsKnownAvailable(profile.Id))).ToArray();
                Volatile.Write(ref _knownApplications, refreshed);
                Volatile.Write(ref _knownApplicationsRefreshedAt, Environment.TickCount64);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or
                    System.ComponentModel.Win32Exception or UnauthorizedAccessException or
                    System.Security.SecurityException)
            {
                Volatile.Write(ref _knownApplicationsRefreshedAt, Environment.TickCount64);
            }
            finally
            {
                Volatile.Write(ref _knownApplicationRefreshQueued, 0);
            }
        });
    }

    private void MarkKnownAvailable(string id)
    {
        var current = Volatile.Read(ref _knownApplications);
        if (current.FirstOrDefault(item => item.Id == id) is not { Available: false })
        {
            return;
        }
        Volatile.Write(
            ref _knownApplications,
            [.. current.Select(item => item.Id == id ? item with { Available = true } : item)]);
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint hWnd, int command);
}
