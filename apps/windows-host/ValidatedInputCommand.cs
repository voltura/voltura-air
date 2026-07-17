namespace VolturaAir.Host;

internal enum InputCommandKind
{
    PointerMove,
    PointerButton,
    PointerWheel,
    PointerZoom,
    KeyboardText,
    KeyboardSpecial
}

internal readonly record struct ValidatedInputCommand(
    InputCommandKind Kind,
    long? Sequence = null,
    int Dx = 0,
    int Dy = 0,
    string? Button = null,
    string? Action = null,
    string? Text = null,
    string? Key = null,
    string[]? ModifierValues = null)
{
    public string Type => Kind switch
    {
        InputCommandKind.PointerMove => "pointer.move",
        InputCommandKind.PointerButton => "pointer.button",
        InputCommandKind.PointerWheel => "pointer.wheel",
        InputCommandKind.PointerZoom => "pointer.zoom",
        InputCommandKind.KeyboardText => "keyboard.text",
        InputCommandKind.KeyboardSpecial => "keyboard.special",
        _ => "unknown"
    };

    public IReadOnlyList<string> Modifiers => ModifierValues ?? [];
}
