using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public sealed class SendInputInjector : IInputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFWheel = 0x0800;
    private const uint MouseEventFHWheel = 0x01000;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort ControlKey = 0x11;
    private const int WheelScale = 9;
    private const int ZoomWheelDelta = 120;

    public void MoveMouse(int dx, int dy)
    {
        SendMouse(dx, dy, 0, MouseEventFMove);
    }

    public void MouseButton(string button, string action)
    {
        var (downFlag, upFlag) = button.Equals("right", StringComparison.OrdinalIgnoreCase)
            ? (MouseEventFRightDown, MouseEventFRightUp)
            : (MouseEventFLeftDown, MouseEventFLeftUp);

        if (action.Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            SendMouse(0, 0, 0, downFlag);
        }
        else if (action.Equals("up", StringComparison.OrdinalIgnoreCase))
        {
            SendMouse(0, 0, 0, upFlag);
        }
        else
        {
            SendInputs(
                MouseInput(0, 0, 0, downFlag),
                MouseInput(0, 0, 0, upFlag));
        }
    }

    public void Scroll(int dx, int dy)
    {
        if (dy != 0)
        {
            SendMouse(0, 0, Math.Clamp(dy * WheelScale, -1200, 1200), MouseEventFWheel);
        }

        if (dx != 0)
        {
            SendMouse(0, 0, Math.Clamp(dx * WheelScale, -1200, 1200), MouseEventFHWheel);
        }
    }

    public void Zoom(string direction)
    {
        var wheelDelta = direction.ToLowerInvariant() switch
        {
            "in" => ZoomWheelDelta,
            "out" => -ZoomWheelDelta,
            _ => 0
        };

        if (wheelDelta == 0)
        {
            return;
        }

        SendInputs(
            KeyboardInput(ControlKey, 0, 0),
            MouseInput(0, 0, wheelDelta, MouseEventFWheel),
            KeyboardInput(ControlKey, 0, KeyEventFKeyUp));
    }

    public void TypeText(string text)
    {
        foreach (var codeUnit in text)
        {
            SendInputs(
                KeyboardInput(0, codeUnit, KeyEventFUnicode),
                KeyboardInput(0, codeUnit, KeyEventFUnicode | KeyEventFKeyUp));
        }
    }

    public void SpecialKey(string key, IReadOnlyList<string> modifiers)
    {
        if (!VirtualKeys.TryGetValue(key, out var virtualKey) && key.Length == 1)
        {
            virtualKey = char.ToUpperInvariant(key[0]);
        }

        if (virtualKey == 0)
        {
            return;
        }

        var modifierKeys = modifiers.Select(GetModifierVirtualKey).Where(value => value != 0).ToArray();
        var inputs = new List<Input>();
        inputs.AddRange(modifierKeys.Select(modifier => KeyboardInput((ushort)modifier, 0, 0)));
        inputs.Add(KeyboardInput((ushort)virtualKey, 0, 0));
        inputs.Add(KeyboardInput((ushort)virtualKey, 0, KeyEventFKeyUp));
        inputs.AddRange(modifierKeys.Reverse().Select(modifier => KeyboardInput((ushort)modifier, 0, KeyEventFKeyUp)));
        SendInputs(inputs.ToArray());
    }

    public void Dispose()
    {
    }

    private static int GetModifierVirtualKey(string modifier)
    {
        return modifier.ToLowerInvariant() switch
        {
            "control" or "ctrl" => 0x11,
            "shift" => 0x10,
            "alt" => 0x12,
            "meta" or "win" or "windows" => 0x5B,
            _ => 0
        };
    }

    private static void SendMouse(int dx, int dy, int mouseData, uint flags)
    {
        SendInputs(MouseInput(dx, dy, mouseData, flags));
    }

    private static void SendInputs(params Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException("Windows did not accept all input events.");
        }
    }

    private static Input MouseInput(int dx, int dy, int mouseData, uint flags)
    {
        return new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInputData
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = mouseData,
                    Flags = flags
                }
            }
        };
    }

    private static Input KeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

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
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    private static readonly Dictionary<string, int> VirtualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Escape"] = 0x1B,
        ["ArrowLeft"] = 0x25,
        ["ArrowUp"] = 0x26,
        ["ArrowRight"] = 0x27,
        ["ArrowDown"] = 0x28,
        ["Delete"] = 0x2E,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Space"] = 0x20,
        ["Win"] = 0x5B,
        ["Windows"] = 0x5B,
        ["BrowserBack"] = 0xA6,
        ["MediaNextTrack"] = 0xB0,
        ["MediaPreviousTrack"] = 0xB1,
        ["MediaStop"] = 0xB2,
        ["MediaPlayPause"] = 0xB3,
        ["VolumeMute"] = 0xAD,
        ["VolumeDown"] = 0xAE,
        ["VolumeUp"] = 0xAF,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B
    };
}
