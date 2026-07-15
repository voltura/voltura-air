using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VolturaAir.Host;

public interface ITextDestinationPlatform
{
    string? ResolveExecutable(TextDestinationProfile profile, string? executableOverride);
    nint? FindRunningWindow(string executable);
    bool Start(string executable, string arguments);
    Task<nint?> WaitForWindowAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken);
    bool TryActivate(nint window);
    bool IsForeground(nint window);
    bool IsElevatedAboveHost(nint window);
    bool TrySetClipboardText(string text);
}

internal sealed partial class WindowsTextDestinationPlatform : ITextDestinationPlatform
{
    public string? ResolveExecutable(TextDestinationProfile profile, string? executableOverride)
    {
        if (!string.IsNullOrWhiteSpace(executableOverride) && File.Exists(executableOverride)) return executableOverride;
        foreach (string name in profile.ExecutableNames)
        {
            var path = GetAppPath(name);
            if (path is not null) return path;
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                var candidate = Path.Combine(root, name == "code.exe" ? "Programs\\Microsoft VS Code\\Code.exe" : name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public nint? FindRunningWindow(string executable) => FindWindow(executable);
    public async Task<nint?> WaitForWindowAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        do { var window = FindWindow(executable); if (window is not null) return window; await Task.Delay(100, cancellationToken); } while (DateTime.UtcNow < until);
        return null;
    }

    public bool Start(string executable, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool TrySetClipboardText(string text)
    {
        try
        {
            var application = System.Windows.Application.Current;
            if (application is null) return false;
            application.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
            return true;
        }
        catch { return false; }
    }

    public bool TryActivate(nint window)
    {
        if (window == nint.Zero) return false;
        if (IsIconic(window)) _ = ShowWindow(window, ShowWindowRestore);

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(window, out _);
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == nint.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var attachedTarget = targetThread != 0 && targetThread != currentThread && AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(window);
            _ = SetWindowPos(window, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            _ = SetWindowPos(window, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            _ = SetActiveWindow(window);
            _ = SetFocus(window);
            _ = SetForegroundWindow(window);
        }
        finally
        {
            if (attachedForeground) _ = AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedTarget) _ = AttachThreadInput(currentThread, targetThread, false);
        }

        return IsForeground(window);
    }

    public bool IsForeground(nint window) => GetForegroundWindow() == window;
    public bool IsElevatedAboveHost(nint window) => WindowsProcessIntegrity.TryGetCurrentProcessIntegrityLevel(out var host) && WindowsProcessIntegrity.TryGetWindowIntegrityLevel(window, out var target) && WindowsProcessIntegrity.IsHigherIntegrity(host, target);

    internal static bool MatchesExecutableName(string processName, string executablePathOrName) => string.Equals(processName, Path.GetFileNameWithoutExtension(executablePathOrName), StringComparison.OrdinalIgnoreCase);

    private static nint? FindWindow(string executable)
    {
        var processName = Path.GetFileNameWithoutExtension(executable);
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try { if (MatchesExecutableName(process.ProcessName, processName) && process.MainWindowHandle != nint.Zero) return process.MainWindowHandle; } catch { }
        }
        return null;
    }

    private static string? GetAppPath(string executable)
    {
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executable}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(subKey);
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        return null;
    }

    private const int ShowWindowRestore = 9;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private static readonly nint TopMostWindow = -1;
    private static readonly nint NoTopMostWindow = -2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attachThread, uint attachToThread, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    private static partial nint SetActiveWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint window);
}
