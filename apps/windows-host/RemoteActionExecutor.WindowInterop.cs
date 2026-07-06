using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public sealed partial class RemoteActionExecutor
{
    private static bool TryActivateBrowserWindow(IntPtr windowHandle, bool ensureFullscreen = false)
    {
        var activated = TryActivateWindow(windowHandle);
        if (activated && ensureFullscreen)
        {
            EnsureBrowserFullscreen(windowHandle);
        }

        return activated;
    }

    private static void EnsureBrowserFullscreen(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || IsWindowFullscreen(windowHandle))
        {
            return;
        }

        for (var attempt = 0; attempt < BrowserFullscreenAttempts; attempt++)
        {
            var hasKeyboardFocus = TryActivateWindowForKeyboardInput(windowHandle);
            Thread.Sleep(150);

            if (IsWindowFullscreen(windowHandle))
            {
                return;
            }

            if (hasKeyboardFocus)
            {
                SendVirtualKey(VirtualKeyF11);
                Thread.Sleep(350);

                if (IsWindowFullscreen(windowHandle))
                {
                    return;
                }
            }

            PostVirtualKey(windowHandle, VirtualKeyF11, F11ScanCode);
            Thread.Sleep(350);

            if (IsWindowFullscreen(windowHandle))
            {
                return;
            }
        }
    }

    private static bool IsWindowFullscreen(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var windowRect))
        {
            return false;
        }

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance
            && Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance
            && Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance
            && Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }

    private static bool TryActivateWindow(IntPtr windowHandle, bool maximize = false)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        TryActivateWindowForKeyboardInput(windowHandle, maximize);
        return true;
    }

    private static bool TryActivateWindowForKeyboardInput(IntPtr windowHandle, bool maximize = false)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = GetWindowThreadProcessId(windowHandle, out _);
        var foregroundWindow = GetForegroundWindow();
        var foregroundThreadId = foregroundWindow == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foregroundWindow, out _);

        var attachedToTarget = targetThreadId != 0 && targetThreadId != currentThreadId && AttachThreadInput(currentThreadId, targetThreadId, true);
        var attachedToForeground = foregroundThreadId != 0 && foregroundThreadId != currentThreadId && foregroundThreadId != targetThreadId && AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            ShowWindow(windowHandle, maximize ? ShowWindowMaximize : ShowWindowRestore);
            BringWindowToTop(windowHandle);
            SetWindowPos(windowHandle, TopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
            SetWindowPos(windowHandle, NoTopMostWindow, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
            SetActiveWindow(windowHandle);
            SetFocus(windowHandle);
            SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        if (IsForegroundWindowOrRoot(windowHandle))
        {
            return true;
        }

        SendVirtualKey(VirtualKeyAlt);
        Thread.Sleep(50);
        SetForegroundWindow(windowHandle);
        return IsForegroundWindowOrRoot(windowHandle);
    }

    private static bool IsForegroundWindowOrRoot(IntPtr windowHandle)
    {
        var foregroundWindow = GetForegroundWindow();
        return foregroundWindow == windowHandle || GetAncestor(foregroundWindow, GetAncestorRoot) == windowHandle;
    }

    private static void SendVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, scanCode: 0, flags: 0),
            CreateKeyboardInput(virtualKey, scanCode: 0, flags: KeyEventKeyUp)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags
                }
            }
        };
    }

    private static void PostVirtualKey(IntPtr windowHandle, ushort virtualKey, ushort scanCode)
    {
        var keyDownLParam = BuildKeyMessageLParam(scanCode, keyUp: false);
        var keyUpLParam = BuildKeyMessageLParam(scanCode, keyUp: true);
        PostMessage(windowHandle, WindowMessageKeyDown, (nuint)virtualKey, keyDownLParam);
        PostMessage(windowHandle, WindowMessageKeyUp, (nuint)virtualKey, keyUpLParam);
    }

    private static nint BuildKeyMessageLParam(ushort scanCode, bool keyUp)
    {
        var value = 1u | ((uint)scanCode << 16);
        if (keyUp)
        {
            value |= 1u << 30;
            value |= 1u << 31;
        }

        return unchecked((nint)(int)value);
    }

    private const int BrowserFullscreenAttempts = 3;

    private const int ShowWindowMaximize = 3;

    private const int ShowWindowRestore = 9;

    private const int SetWindowPosNoSize = 0x0001;

    private const int SetWindowPosNoMove = 0x0002;

    private const int SetWindowPosShowWindow = 0x0040;

    private const uint MonitorDefaultToNearest = 0x00000002;

    private const uint GetAncestorRoot = 2;

    private const uint InputKeyboard = 1;

    private const uint KeyEventKeyUp = 0x0002;

    private const uint WindowMessageKeyDown = 0x0100;

    private const uint WindowMessageKeyUp = 0x0101;

    private const ushort VirtualKeyAlt = 0x12;

    private const ushort VirtualKeyF11 = 0x7A;

    private const ushort F11ScanCode = 0x57;

    private static readonly nint TopMostWindow = -1;

    private static readonly nint NoTopMostWindow = -2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort LowParam;
        public ushort HighParam;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, nuint wParam, nint lParam);
}
