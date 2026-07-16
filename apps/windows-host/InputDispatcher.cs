using System.Text.Json;

namespace VolturaAir.Host;

public sealed class InputDispatcher(IInputInjector inputInjector)
{
    private readonly IInputInjector _inputInjector = inputInjector;
    internal event EventHandler? TaskbarActivated;

    public bool Dispatch(JsonElement message)
    {
        return Dispatch(message, out _);
    }

    public bool Dispatch(JsonElement message, out InputDispatchOutcome outcome)
    {
        if (!ClientMessageValidator.TryReadType(message, out var type) || type is null ||
            !ClientMessageValidator.TryDecodeInputMessage(message, type, out var command))
        {
            outcome = InputDispatchOutcome.Executed;
            return false;
        }

        return Dispatch(command, out outcome);
    }

    internal bool Dispatch(ValidatedInputCommand command, out InputDispatchOutcome outcome)
    {
        outcome = InputDispatchOutcome.Executed;
        if (command.Kind == InputCommandKind.KeyboardSpecial && HostUiInputGuard.IsShowDesktopShortcut(command))
        {
            outcome = HostUiInputGuard.TryShowDesktop()
                ? InputDispatchOutcome.Executed
                : InputDispatchOutcome.Failed;
            return true;
        }

        if (command.Kind == InputCommandKind.KeyboardSpecial && HostUiInputGuard.IsMinimizeWindowShortcut(command))
        {
            outcome = HostUiInputGuard.TryMinimizeForegroundWindow()
                ? InputDispatchOutcome.Executed
                : InputDispatchOutcome.Failed;
            return true;
        }

        if (HostUiInputGuard.ShouldBlockClientInput(command, out var protectedCommandExecuted))
        {
            outcome = protectedCommandExecuted ? InputDispatchOutcome.Executed : InputDispatchOutcome.Blocked;
            return true;
        }

        switch (command.Kind)
        {
            case InputCommandKind.PointerMove:
                _inputInjector.MoveMouse(command.Dx, command.Dy);
                return true;
            case InputCommandKind.PointerButton:
                var button = command.Button ?? string.Empty;
                var action = command.Action ?? string.Empty;
                _inputInjector.MouseButton(button, action);
                if (ShouldRecheckTaskbarAfterLeftButton(button, action) && HostUiInputGuard.IsPointerOverTaskbar())
                {
                    TaskbarActivated?.Invoke(this, EventArgs.Empty);
                }

                return true;
            case InputCommandKind.PointerWheel:
                _inputInjector.Scroll(command.Dx, command.Dy);
                return true;
            case InputCommandKind.PointerZoom:
                _inputInjector.Zoom(command.Action ?? string.Empty);
                return true;
            case InputCommandKind.KeyboardText:
                TypeTextWithLineBreaks(command.Text ?? string.Empty);
                return true;
            case InputCommandKind.KeyboardSpecial:
                DispatchSpecialKey(command.Key ?? string.Empty, command.Modifiers);
                return true;
            default:
                return false;
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

    public InputDispatchOutcome DispatchShortcut(string key, IReadOnlyList<string> modifiers)
    {
        if (HostUiInputGuard.ShouldBlockTextTransfer())
        {
            return InputDispatchOutcome.Blocked;
        }

        _inputInjector.SpecialKey(key, modifiers);
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

    private void DispatchSpecialKey(string key, IReadOnlyList<string> modifiers)
    {
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
