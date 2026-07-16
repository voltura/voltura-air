using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ClientMessageValidatorTests
{
    [Fact]
    public void DecodesAndNormalizesPointerInputOnce()
    {
        using var document = JsonDocument.Parse("""{ "type": "pointer.move", "seq": 42, "dx": 4.7, "dy": -3.2 }""");

        var decoded = ClientMessageValidator.TryDecodeInputMessage(document.RootElement, "pointer.move", out var command);

        Assert.True(decoded);
        Assert.Equal(InputCommandKind.PointerMove, command.Kind);
        Assert.Equal(42, command.Sequence);
        Assert.Equal(5, command.Dx);
        Assert.Equal(-3, command.Dy);
        Assert.Equal("pointer.move", command.Type);
    }

    [Fact]
    public void DecodesValidatedKeyboardModifiersWithoutASecondJsonRead()
    {
        using var document = JsonDocument.Parse("""{ "type": "keyboard.special", "seq": 9, "key": "Enter", "modifiers": ["Control", "Shift"] }""");

        var decoded = ClientMessageValidator.TryDecodeInputMessage(document.RootElement, "keyboard.special", out var command);

        Assert.True(decoded);
        Assert.Equal(InputCommandKind.KeyboardSpecial, command.Kind);
        Assert.Equal("Enter", command.Key);
        Assert.Equal(["Control", "Shift"], command.Modifiers);
    }

    [Theory]
    [InlineData("""{ "type": "pointer.move", "seq": 0, "dx": 1, "dy": 1 }""", "pointer.move")]
    [InlineData("""{ "type": "pointer.move", "dx": 5001, "dy": 1 }""", "pointer.move")]
    [InlineData("""{ "type": "pointer.button", "button": "middle", "action": "click" }""", "pointer.button")]
    [InlineData("""{ "type": "keyboard.special", "key": "Enter", "modifiers": [1] }""", "keyboard.special")]
    public void RejectsInvalidInputWithoutProducingACommand(string json, string type)
    {
        using var document = JsonDocument.Parse(json);

        Assert.False(ClientMessageValidator.TryDecodeInputMessage(document.RootElement, type, out _));
    }
}
