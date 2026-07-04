using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
        if (TrySendMouse(dx, dy, 0, MouseEventFMove))
        {
            return;
        }

        var position = Cursor.Position;
        Cursor.Position = new Point(position.X + dx, position.Y + dy);
    }

    public void MouseButton(string button, string action)
    {
        var (downFlag, upFlag) = button.Equals("right", StringComparison.OrdinalIgnoreCase)
            ? (MouseEventFRightDown, MouseEventFRightUp)
            : (MouseEventFLeftDown, MouseEventFLeftUp);

        if (action.Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            if (!TrySendMouse(0, 0, 0, downFlag))
            {
                MouseEvent(downFlag, 0, 0, 0, 0);
            }
        }
        else if (action.Equals("up", StringComparison.OrdinalIgnoreCase))
        {
            if (!TrySendMouse(0, 0, 0, upFlag))
            {
                MouseEvent(upFlag, 0, 0, 0, 0);
            }
        }
        else if (!TrySendInputs(
            MouseInput(0, 0, 0, downFlag),
            MouseInput(0, 0, 0, upFlag)))
        {
            MouseEvent(downFlag, 0, 0, 0, 0);
            MouseEvent(upFlag, 0, 0, 0, 0);
        }
    }

    public void Scroll(int dx, int dy)
    {
        if (dy != 0)
        {
            var wheelDelta = Math.Clamp(dy * WheelScale, -1200, 1200);
            if (!TrySendMouse(0, 0, wheelDelta, MouseEventFWheel))
            {
                MouseEvent(MouseEventFWheel, 0, 0, wheelDelta, 0);
            }
        }

        if (dx != 0)
        {
            var wheelDelta = Math.Clamp(dx * WheelScale, -1200, 1200);
            if (!TrySendMouse(0, 0, wheelDelta, MouseEventFHWheel))
            {
                MouseEvent(MouseEventFHWheel, 0, 0, wheelDelta, 0);
            }
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

        if (!TrySendInputs(
            KeyboardInput(ControlKey, 0, 0),
            MouseInput(0, 0, wheelDelta, MouseEventFWheel),
            KeyboardInput(ControlKey, 0, KeyEventFKeyUp)))
        {
            KeybdEvent((byte)ControlKey, 0, 0, 0);
            MouseEvent(MouseEventFWheel, 0, 0, wheelDelta, 0);
            KeybdEvent((byte)ControlKey, 0, KeyEventFKeyUp, 0);
        }
    }

    public void TypeText(string text)
    {
        foreach (var codeUnit in text)
        {
            if (!TrySendInputs(
                KeyboardInput(0, codeUnit, KeyEventFUnicode),
                KeyboardInput(0, codeUnit, KeyEventFUnicode | KeyEventFKeyUp)))
            {
                SendKeys.SendWait(EscapeSendKeysText(codeUnit.ToString()));
            }
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
        if (TrySendInputs(inputs.ToArray()))
        {
            return;
        }

        foreach (var modifier in modifierKeys)
        {
            KeybdEvent((byte)modifier, 0, 0, 0);
        }

        KeybdEvent((byte)virtualKey, 0, 0, 0);
        KeybdEvent((byte)virtualKey, 0, KeyEventFKeyUp, 0);

        foreach (var modifier in modifierKeys.Reverse())
        {
            KeybdEvent((byte)modifier, 0, KeyEventFKeyUp, 0);
        }
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

    private static bool TrySendMouse(int dx, int dy, int mouseData, uint flags)
    {
        return TrySendInputs(MouseInput(dx, dy, mouseData, flags));
    }

    private static bool TrySendInputs(params Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length)
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        Debug.WriteLine($"SendInput accepted {sent}/{inputs.Length} input events. LastError={error}.");
        Console.Error.WriteLine($"Voltura Air input warning: Windows accepted {sent}/{inputs.Length} input events. LastError={error}.");
        return false;
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

    private static string EscapeSendKeysText(string text)
    {
        return text
            .Replace("{", "{{}", StringComparison.Ordinal)
            .Replace("}", "{}}", StringComparison.Ordinal)
            .Replace("+", "{+}", StringComparison.Ordinal)
            .Replace("^", "{^}", StringComparison.Ordinal)
            .Replace("%", "{%}", StringComparison.Ordinal)
            .Replace("~", "{~}", StringComparison.Ordinal)
            .Replace("(", "{(}", StringComparison.Ordinal)
            .Replace(")", "{)}", StringComparison.Ordinal);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, int dx, int dy, int data, nint extraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte virtualKey, byte scanCode, uint flags, nint extraInfo);

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
