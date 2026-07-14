using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using WpfApplication = System.Windows.Application;

namespace VolturaAir.Host;

internal static partial class HostUiInputGuard
{
    private static readonly TimeSpan RecentClientInputWindow = TimeSpan.FromSeconds(2);
    private const uint GetAncestorRoot = 2;
    private const int WmNcHitTest = 0x0084;
    private const int WmSysCommand = 0x0112;
    private const int HtNowhere = 0;
    private const int HtClient = 1;
    private const int HtMinButton = 8;
    private const int HtMaxButton = 9;
    private const int HtClose = 20;
    private const int ScClose = 0xF060;
    private const int ScMinimize = 0xF020;
    private const int ScMaximize = 0xF030;
    private const int ScRestore = 0xF120;
    private const int ShowWindowMinimize = 6;
    private static long _lastClientPointerInputTicks;

    public static bool ShouldBlockClientInput(string? messageType, JsonElement message)
    {
        return ShouldBlockClientInput(messageType, message, out _);
    }

    public static bool ShouldBlockTextTransfer()
    {
        return !AppClientControlSettings.IsEnabled() && IsForegroundVolturaHostWindow();
    }

    public static bool ShouldBlockClientInput(string? messageType, JsonElement message, out bool protectedCommandExecuted)
    {
        protectedCommandExecuted = false;
        if (AppClientControlSettings.IsEnabled())
        {
            return false;
        }

        return messageType switch
        {
            "keyboard.text" => IsForegroundVolturaHostWindow(),
            "keyboard.special" => ShouldBlockSpecialKey(message, out protectedCommandExecuted),
            "pointer.button" => ShouldBlockPointerButton(message),
            "pointer.wheel" or "pointer.zoom" => ShouldBlockPointerWheelOrZoom(),
            _ => false
        };
    }

    private static bool ShouldBlockSpecialKey(JsonElement message, out bool protectedCommandExecuted)
    {
        protectedCommandExecuted = false;
        var foregroundWindow = GetForegroundWindow();
        if (!IsVolturaHostWindow(foregroundWindow))
        {
            return false;
        }

        return true;
    }

    internal static bool TryMinimizeForegroundWindow()
    {
        var foregroundWindow = GetRootWindow(GetForegroundWindow());
        if (foregroundWindow == nint.Zero)
        {
            return false;
        }

        _ = ShowWindow(foregroundWindow, ShowWindowMinimize);
        return IsIconic(foregroundWindow);
    }

    internal static bool TryShowDesktop()
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return false;
        }

        try
        {
            return dispatcher.CheckAccess()
                ? TryMinimizeAllDesktopWindows()
                : dispatcher.Invoke(TryMinimizeAllDesktopWindows);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool IsMinimizeWindowShortcut(JsonElement message)
    {
        if (!message.TryGetProperty("key", out var keyProperty) ||
            !string.Equals(keyProperty.GetString(), "ArrowDown", StringComparison.OrdinalIgnoreCase) ||
            !message.TryGetProperty("modifiers", out var modifiers) ||
            modifiers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return HasOnlyWinModifier(modifiers);
    }

    internal static bool IsShowDesktopShortcut(JsonElement message)
    {
        if (!message.TryGetProperty("key", out var keyProperty) ||
            !string.Equals(keyProperty.GetString(), "D", StringComparison.OrdinalIgnoreCase) ||
            !message.TryGetProperty("modifiers", out var modifiers) ||
            modifiers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return HasOnlyWinModifier(modifiers);
    }

    private static bool TryMinimizeAllDesktopWindows()
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            _ = shellType.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static bool HasOnlyWinModifier(JsonElement modifiers)
    {
        using var enumerator = modifiers.EnumerateArray();
        return enumerator.MoveNext() &&
            string.Equals(enumerator.Current.GetString(), "Win", StringComparison.OrdinalIgnoreCase) &&
            !enumerator.MoveNext();
    }

    public static bool IsRecentProtectedClientInput()
    {
        if (AppClientControlSettings.IsEnabled())
        {
            return false;
        }

        var lastInputTicks = Interlocked.Read(ref _lastClientPointerInputTicks);
        return lastInputTicks > 0 &&
            DateTimeOffset.UtcNow - new DateTimeOffset(lastInputTicks, TimeSpan.Zero) <= RecentClientInputWindow;
    }

    private static bool ShouldBlockPointerButton(JsonElement message)
    {
        MarkClientPointerInput();

        var action = message.TryGetProperty("action", out var actionProperty)
            ? actionProperty.GetString()
            : null;

        // Always allow button-up events so a remote mouse button cannot get stuck down.
        if (string.Equals(action, "up", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetPointerVolturaHostHitTest(out var windowHandle, out var hitTest))
        {
            return false;
        }

        if (IsLeftButton(message))
        {
            TryRunWindowChromeCommand(windowHandle, hitTest);
        }

        return true;
    }

    private static bool ShouldBlockPointerWheelOrZoom()
    {
        MarkClientPointerInput();
        return IsPointerOverVolturaHostWindow();
    }

    private static void MarkClientPointerInput()
    {
        Interlocked.Exchange(ref _lastClientPointerInputTicks, DateTimeOffset.UtcNow.Ticks);
    }

    private static bool IsForegroundVolturaHostWindow()
    {
        return IsVolturaHostWindow(GetForegroundWindow());
    }

    private static bool IsPointerOverVolturaHostWindow()
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        return IsVolturaHostWindow(WindowFromPoint(point));
    }

    private static bool TryGetPointerVolturaHostHitTest(out nint windowHandle, out int hitTest)
    {
        windowHandle = nint.Zero;
        hitTest = HtNowhere;

        if (!GetCursorPos(out var point))
        {
            return false;
        }

        windowHandle = GetRootWindow(WindowFromPoint(point));
        if (!IsVolturaHostWindow(windowHandle))
        {
            return false;
        }

        hitTest = unchecked((int)SendMessage(windowHandle, WmNcHitTest, nint.Zero, MakeLParam(point.X, point.Y)));
        return hitTest != HtNowhere;
    }

    internal static bool IsWindowCommandHitTest(int hitTest)
    {
        return hitTest is HtMinButton or HtMaxButton or HtClose;
    }

    internal static unsafe bool IsPointerOverTaskbar()
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        var taskbarWindow = GetRootWindow(WindowFromPoint(point));
        if (taskbarWindow == nint.Zero)
        {
            return false;
        }

        const int classNameCapacity = 32;
        var className = stackalloc char[classNameCapacity];
        var length = GetClassName(taskbarWindow, className, classNameCapacity);
        var classNameSpan = new ReadOnlySpan<char>(className, Math.Max(0, length));
        return classNameSpan.SequenceEqual("Shell_TrayWnd".AsSpan()) ||
            classNameSpan.SequenceEqual("Shell_SecondaryTrayWnd".AsSpan());
    }

    private static bool TryRunWindowChromeCommand(nint windowHandle, int hitTest)
    {
        var command = GetWindowChromeCommand(windowHandle, hitTest);
        if (command is null)
        {
            return false;
        }

        return PostMessage(windowHandle, WmSysCommand, command.Value, nint.Zero);
    }

    private static nint? GetWindowChromeCommand(nint windowHandle, int hitTest)
    {
        return hitTest switch
        {
            HtMinButton => ScMinimize,
            HtMaxButton => IsZoomed(windowHandle) ? ScRestore : ScMaximize,
            HtClose => ScClose,
            _ => null
        };
    }

    private static bool IsLeftButton(JsonElement message)
    {
        var button = message.TryGetProperty("button", out var buttonProperty)
            ? buttonProperty.GetString()
            : null;

        return string.IsNullOrWhiteSpace(button) || string.Equals(button, "left", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVolturaHostWindow(nint windowHandle)
    {
        var rootWindow = GetRootWindow(windowHandle);
        if (rootWindow == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(rootWindow, out var processId);
        return processId == Environment.ProcessId;
    }

    private static nint GetRootWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return nint.Zero;
        }

        var rootWindow = GetAncestor(windowHandle, GetAncestorRoot);
        return rootWindow == nint.Zero ? windowHandle : rootWindow;
    }

    private static nint MakeLParam(int x, int y)
    {
        return unchecked((nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint windowHandle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint windowHandle, char* className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out int processId);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint windowHandle, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
