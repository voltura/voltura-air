using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class FakeInputInjector : IInputInjector
{
    public List<string> Events { get; } = new();

    public void MoveMouse(int dx, int dy)
    {
        Events.Add($"MoveMouse:{dx}:{dy}");
    }

    public void MouseButton(string button, string action)
    {
        Events.Add($"MouseButton:{button}:{action}");
    }

    public void Scroll(int dx, int dy)
    {
        Events.Add($"Scroll:{dx}:{dy}");
    }

    public void Zoom(string direction)
    {
        Events.Add($"Zoom:{direction}");
    }

    public void TypeText(string text)
    {
        Events.Add($"TypeText:{text}");
    }

    public void SpecialKey(string key, IReadOnlyList<string> modifiers)
    {
        Events.Add($"SpecialKey:{key}:{string.Join(",", modifiers)}");
    }

    public void Dispose()
    {
    }
}
