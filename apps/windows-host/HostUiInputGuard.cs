using System.Runtime.InteropServices;
using System.Text.Json;

namespace VolturaAir.Host;

internal static class HostUiInputGuard
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
    private static long _lastClientPointerInputTicks;

    public static bool ShouldBlockClientInput(string? messageType, JsonElement message)
    {
        if (AppClientControlSettings.IsEnabled())
        {
            return false;
        }

        return messageType switch
        {
            "keyboard.text" or "keyboard.special" => IsForegroundVolturaHostWindow(),
            "pointer.button" => ShouldBlockPointerButton(message),
            "pointer.wheel" or "pointer.zoom" => ShouldBlockPointerWheelOrZoom(),
            _ => false
        };
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out int processId);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint windowHandle, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
