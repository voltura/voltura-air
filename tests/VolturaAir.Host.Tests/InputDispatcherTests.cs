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
            new[]
            {
                "MoveMouse:5:-3",
                "MouseButton:right:click",
                "Scroll:5:-9",
                "Zoom:out",
                "TypeText:Hello",
                "SpecialKey:Enter:Control"
            },
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
            new[]
            {
                "SpecialKey:Z:Control",
                "SpecialKey:Y:Control"
            },
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

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
