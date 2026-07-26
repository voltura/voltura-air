using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ClientMessageValidatorTests
{
    [Theory]
    [InlineData("""{ "type": "presentation.command", "operationId": "laser-1", "target": "powerpoint", "action": "pointer", "enabled": true }""", true)]
    [InlineData("""{ "type": "presentation.command", "operationId": "laser-1", "target": "pdf", "action": "pointer" }""", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "next-1", "target": "powerpoint", "action": "next", "enabled": false }""", false)]
    public void RequiresDesiredStateOnlyForLaserPointerCommands(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "presentation.command"));
    }

    [Theory]
    [InlineData("""{ "type": "presentation.command", "operationId": "go-1", "target": "powerpoint", "action": "goto", "runtimePresentationId": "runtime-1", "slideNumber": 8 }""", "presentation.command", true)]
    [InlineData("""{ "type": "presentation.command", "operationId": "go-1", "target": "powerpoint", "action": "goto", "slideNumber": 0 }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "pause-1", "target": "powerpoint", "action": "pause" }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "focus-1", "target": "powerpoint", "action": "activate", "runtimePresentationId": "runtime-1" }""", "presentation.command", true)]
    [InlineData("""{ "type": "presentation.command", "operationId": "focus-1", "target": "pdf", "action": "activate" }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.command", "operationId": "first-1", "target": "pdf", "action": "first" }""", "presentation.command", false)]
    [InlineData("""{ "type": "presentation.powerpoint.refresh", "operationId": "refresh-1" }""", "presentation.powerpoint.refresh", true)]
    [InlineData("""{ "type": "presentation.powerpoint.launch", "operationId": "launch-1", "presentationId": "report-1" }""", "presentation.powerpoint.launch", true)]
    [InlineData("""{ "type": "presentation.powerpoint.launch", "operationId": "launch-1", "presentationId": "../bad" }""", "presentation.powerpoint.launch", false)]
    [InlineData("""{ "type": "presentation.session", "operationId": "session-1", "action": "start", "runtimePresentationId": "runtime-1" }""", "presentation.session", true)]
    [InlineData("""{ "type": "presentation.session", "operationId": "break-1", "action": "break", "enabled": true }""", "presentation.session", true)]
    [InlineData("""{ "type": "presentation.session", "operationId": "save-1", "action": "save", "enabled": false }""", "presentation.session", false)]
    public void ValidatesExpandedPresentationMessages(
        string json,
        string type,
        bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            expected,
            ClientMessageValidator.IsValidAuthenticatedMessage(
                document.RootElement,
                type));
    }

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
