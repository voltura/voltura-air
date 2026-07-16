using System.ComponentModel;
using Microsoft.Win32;

namespace VolturaAir.Host;

internal static class TrayIconVisibilityPromoter
{
    private const int RetryIntervalMilliseconds = 250;
    private const int RetryLimit = 20;
    private const string NotifyIconSettingsSubKey = @"Control Panel\NotifyIconSettings";

    public static void PromoteWhenReady(IContainer components, NotifyIcon icon)
    {
        var attempts = 0;
        // The supplied component container owns and disposes this timer with the tray context.
#pragma warning disable CA2000
        var timer = new System.Windows.Forms.Timer(components)
        {
            Interval = RetryIntervalMilliseconds
        };
#pragma warning restore CA2000

        timer.Tick += (_, _) =>
        {
            attempts += 1;

            if (TryPromoteCurrentProcess(out var changed))
            {
                timer.Stop();
                RefreshIconIfNeeded(icon, changed);
            }
            else if (attempts >= RetryLimit)
            {
                timer.Stop();
            }
        };

        timer.Start();
    }

    private static bool TryPromoteCurrentProcess(out bool changed)
    {
        changed = false;

        if (Environment.ProcessPath is not { Length: > 0 } executablePath)
        {
            return true;
        }

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsSubKey, writable: true);
            if (root is null)
            {
                return true;
            }

            return TryPromoteEntries(root, executablePath, out changed);
        }
        catch
        {
            return true;
        }
    }

    internal static bool TryPromoteEntries(RegistryKey root, string executablePath, out bool changed)
    {
        changed = false;
        var matchedEntry = false;
        var normalizedExecutablePath = NormalizePath(executablePath);

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            using var entry = root.OpenSubKey(subKeyName, writable: true);
            var entryExecutablePath = entry?.GetValue("ExecutablePath") as string;
            if (!PathsEqual(normalizedExecutablePath, entryExecutablePath))
            {
                continue;
            }

            matchedEntry = true;
            if (!Equals(entry!.GetValue("IsPromoted"), 1))
            {
                entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                changed = true;
            }
        }

        return matchedEntry;
    }

    private static void RefreshIconIfNeeded(NotifyIcon icon, bool changed)
    {
        if (!changed)
        {
            return;
        }

        icon.Visible = false;
        icon.Visible = true;
    }

    private static bool PathsEqual(string path, string? candidate)
    {
        return candidate is { Length: > 0 } &&
            string.Equals(path, NormalizePath(candidate), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }
}
