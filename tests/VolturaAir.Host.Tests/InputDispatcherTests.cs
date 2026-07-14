using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class InputDispatcherTests
{
    [Fact]
    public void DispatchesPointerAndKeyboardMessages()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "pointer.move", "dx": 4.7, "dy": -3.2 }""")));
        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "pointer.button", "button": "right", "action": "click" }""")));
        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "pointer.wheel", "dx": 5, "dy": -9 }""")));
        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "pointer.zoom", "direction": "out" }""")));
        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "keyboard.text", "text": "Hello" }""")));
        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "keyboard.special", "key": "Enter", "modifiers": ["Control"] }""")));

        Assert.Equal(
            [
                "MoveMouse:5:-3",
                "MouseButton:right:click",
                "Scroll:5:-9",
                "Zoom:out",
                "TypeText:Hello",
                "SpecialKey:Enter:Control"
            ],
            fake.Events);
    }

    [Fact]
    public void DispatchesUndoAndRedoShortcutAliases()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "keyboard.special", "key": "Undo" }""")));
        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "keyboard.special", "key": "Redo" }""")));

        Assert.Equal(
            [
                "SpecialKey:Z:Control",
                "SpecialKey:Y:Control"
            ],
            fake.Events);
    }

    [Fact]
    public void IgnoresUnknownMessages()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        Assert.False(dispatcher.Dispatch(Parse("""{ "type": "unknown" }""")));
        Assert.Empty(fake.Events);
    }

    [Fact]
    public void TransfersTextAndSendsEnterOnlyAfterText()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        var outcome = dispatcher.TransferText("Hello", sendEnter: true);

        Assert.Equal(InputDispatchOutcome.Executed, outcome);
        Assert.Equal(["TypeText:Hello", "SpecialKey:Enter:"], fake.Events);
    }

    [Fact]
    public void PreservesLfCrLfAndCrAsSinglePhysicalEnterKeys()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        var outcome = dispatcher.TransferText("First\r\nSecond\nThird\rFourth", sendEnter: true);

        Assert.Equal(InputDispatchOutcome.Executed, outcome);
        Assert.Equal(
            [
                "TypeText:First",
                "SpecialKey:Enter:",
                "TypeText:Second",
                "SpecialKey:Enter:",
                "TypeText:Third",
                "SpecialKey:Enter:",
                "TypeText:Fourth",
                "SpecialKey:Enter:"
            ],
            fake.Events);
    }

    [Fact]
    public void DispatchesBufferedKeyboardTextWithPhysicalEnterKeys()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        Assert.True(dispatcher.Dispatch(Parse("""{ "type": "keyboard.text", "text": "First line\nSecond line\r\nThird line" }""")));

        Assert.Equal(
            [
                "TypeText:First line",
                "SpecialKey:Enter:",
                "TypeText:Second line",
                "SpecialKey:Enter:",
                "TypeText:Third line"
            ],
            fake.Events);
    }

    [Fact]
    public void PreservesConsecutiveAndTrailingLineBreaksWithoutEmptyTextEvents()
    {
        using var fake = new FakeInputInjector();
        var dispatcher = new InputDispatcher(fake);

        var outcome = dispatcher.TransferText("\n\r\n", sendEnter: false);

        Assert.Equal(InputDispatchOutcome.Executed, outcome);
        Assert.Equal(["SpecialKey:Enter:", "SpecialKey:Enter:"], fake.Events);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
