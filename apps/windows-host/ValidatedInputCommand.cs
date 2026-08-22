namespace VolturaAir.Host;

internal enum InputCommandKind
{
    PointerMove,
    PointerButton,
    PointerWheel,
    PointerZoom,
    ScreenPointerMove,
    ScreenPointerButton,
    ScreenPointerWheel,
    KeyboardText,
    KeyboardSpecial
}

internal enum InputCommandContext
{
    Trackpad,
    Keyboard,
    Dictation,
    MediaControls,
    Presentation,
    CustomScreens,
    ScreenView,
    GyroMouse
}

internal readonly record struct ValidatedInputCommand(
    InputCommandKind Kind,
    long? Sequence = null,
    int Dx = 0,
    int Dy = 0,
    string? Button = null,
    string? Action = null,
    string? DisplayId = null,
    double X = 0,
    double Y = 0,
    string? Text = null,
    string? Key = null,
    string[]? ModifierValues = null,
    InputCommandContext? Context = null)
{
    public string Type => Kind switch
    {
        InputCommandKind.PointerMove => "pointer.move",
        InputCommandKind.PointerButton => "pointer.button",
        InputCommandKind.PointerWheel => "pointer.wheel",
        InputCommandKind.PointerZoom => "pointer.zoom",
        InputCommandKind.ScreenPointerMove => "screen.pointer.move",
        InputCommandKind.ScreenPointerButton => "screen.pointer.button",
        InputCommandKind.ScreenPointerWheel => "screen.pointer.wheel",
        InputCommandKind.KeyboardText => "keyboard.text",
        InputCommandKind.KeyboardSpecial => "keyboard.special",
        _ => "unknown"
    };

    public IReadOnlyList<string> Modifiers => ModifierValues ?? [];
}
