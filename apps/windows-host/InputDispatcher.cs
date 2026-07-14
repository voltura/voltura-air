using System.Text.Json;

namespace VolturaAir.Host;

public sealed class InputDispatcher
{
    private readonly IInputInjector _inputInjector;
    private readonly IPointerHighlightService _pointerHighlightService;

    internal event EventHandler? TaskbarActivated;

    public InputDispatcher(IInputInjector inputInjector, IPointerHighlightService? pointerHighlightService = null)
    {
        _inputInjector = inputInjector;
        _pointerHighlightService = pointerHighlightService ?? NullPointerHighlightService.Instance;
    }

    public bool Dispatch(JsonElement message)
    {
        return Dispatch(message, out _);
    }

    public bool Dispatch(JsonElement message, out InputDispatchOutcome outcome)
    {
        return Dispatch(message, highlightPointer: false, out outcome);
    }

    public bool Dispatch(JsonElement message, bool highlightPointer, out InputDispatchOutcome outcome)
    {
        outcome = InputDispatchOutcome.Executed;
        if (!message.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        var type = typeProperty.GetString();
        if (type == "keyboard.special" && HostUiInputGuard.IsShowDesktopShortcut(message))
        {
            outcome = HostUiInputGuard.TryShowDesktop()
                ? InputDispatchOutcome.Executed
                : InputDispatchOutcome.Failed;
            return true;
        }

        if (type == "keyboard.special" && HostUiInputGuard.IsMinimizeWindowShortcut(message))
        {
            outcome = HostUiInputGuard.TryMinimizeForegroundWindow()
                ? InputDispatchOutcome.Executed
                : InputDispatchOutcome.Failed;
            return true;
        }

        if (HostUiInputGuard.ShouldBlockClientInput(type, message, out var protectedCommandExecuted))
        {
            outcome = protectedCommandExecuted ? InputDispatchOutcome.Executed : InputDispatchOutcome.Blocked;
            return true;
        }

        switch (type)
        {
            case "pointer.move":
                _inputInjector.MoveMouse(GetNumber(message, "dx"), GetNumber(message, "dy"));
                NotifyPointerActivity(highlightPointer);
                return true;
            case "pointer.button":
                var button = GetString(message, "button");
                var action = GetString(message, "action");
                _inputInjector.MouseButton(button, action);
                NotifyPointerActivity(highlightPointer);
                if (ShouldRecheckTaskbarAfterLeftButton(button, action) && HostUiInputGuard.IsPointerOverTaskbar())
                {
                    TaskbarActivated?.Invoke(this, EventArgs.Empty);
                }

                return true;
            case "pointer.wheel":
                _inputInjector.Scroll(GetNumber(message, "dx"), GetNumber(message, "dy"));
                NotifyPointerActivity(highlightPointer);
                return true;
            case "pointer.zoom":
                _inputInjector.Zoom(GetString(message, "direction"));
                NotifyPointerActivity(highlightPointer);
                return true;
            case "keyboard.text":
                TypeTextWithLineBreaks(GetString(message, "text"));
                return true;
            case "keyboard.special":
                DispatchSpecialKey(message);
                return true;
            default:
                return false;
        }
    }

    private void NotifyPointerActivity(bool highlightPointer)
    {
        if (highlightPointer)
        {
            _pointerHighlightService.NotifyPointerActivity();
        }
    }

    public InputDispatchOutcome TransferText(string text, bool sendEnter)
    {
        if (HostUiInputGuard.ShouldBlockTextTransfer())
        {
            return InputDispatchOutcome.Blocked;
        }

        TypeTextWithLineBreaks(text);
        if (sendEnter)
        {
            _inputInjector.SpecialKey("Enter", []);
        }

        return InputDispatchOutcome.Executed;
    }

    private void TypeTextWithLineBreaks(string text)
    {
        var segmentStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            if (index > segmentStart)
            {
                _inputInjector.TypeText(text[segmentStart..index]);
            }

            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            _inputInjector.SpecialKey("Enter", []);
            segmentStart = index + 1;
        }

        if (segmentStart < text.Length)
        {
            _inputInjector.TypeText(text[segmentStart..]);
        }
    }

    private void DispatchSpecialKey(JsonElement message)
    {
        var key = GetString(message, "key");
        var modifiers = GetModifiers(message);

        if (TryResolveShortcutAlias(key, out var shortcutKey, out var shortcutModifiers))
        {
            _inputInjector.SpecialKey(shortcutKey, shortcutModifiers);
            return;
        }

        _inputInjector.SpecialKey(key, modifiers);
    }

    private static bool TryResolveShortcutAlias(string key, out string shortcutKey, out IReadOnlyList<string> shortcutModifiers)
    {
        shortcutKey = key;
        shortcutModifiers = [];

        if (key.Equals("Undo", StringComparison.OrdinalIgnoreCase))
        {
            shortcutKey = "Z";
            shortcutModifiers = ["Control"];
            return true;
        }

        if (key.Equals("Redo", StringComparison.OrdinalIgnoreCase))
        {
            shortcutKey = "Y";
            shortcutModifiers = ["Control"];
            return true;
        }

        return false;
    }

    internal static bool ShouldRecheckTaskbarAfterLeftButton(string button, string action)
    {
        return (string.Equals(action, "click", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "up", StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(button) || string.Equals(button, "left", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetNumber(JsonElement message, string propertyName)
    {
        return message.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number)
            ? (int)Math.Clamp(Math.Round(number), -5000, 5000)
            : 0;
    }

    private static string GetString(JsonElement message, string propertyName)
    {
        return message.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static IReadOnlyList<string> GetModifiers(JsonElement message)
    {
        if (!message.TryGetProperty("modifiers", out var modifiers) || modifiers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return modifiers.EnumerateArray()
            .Select(modifier => modifier.GetString())
            .Where(modifier => !string.IsNullOrWhiteSpace(modifier))
            .Select(modifier => modifier!)
            .ToArray();
    }
}

public enum InputDispatchOutcome
{
    Executed,
    Blocked,
    Failed
}

public interface IInputInjector : IDisposable
{
    void MoveMouse(int dx, int dy);

    void MouseButton(string button, string action);

    void Scroll(int dx, int dy);

    void Zoom(string direction);

    void TypeText(string text);

    void SpecialKey(string key, IReadOnlyList<string> modifiers);
}

public interface IPointerHighlightService
{
    void NotifyPointerActivity();

    void SetOverlaySuppressed(bool suppressed);
}

internal sealed class NullPointerHighlightService : IPointerHighlightService
{
    public static NullPointerHighlightService Instance { get; } = new();

    public void NotifyPointerActivity()
    {
    }

    public void SetOverlaySuppressed(bool suppressed)
    {
    }
}
