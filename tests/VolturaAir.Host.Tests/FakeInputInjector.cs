using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class FakeInputInjector : IInputInjector
{
    public List<string> Events { get; } = new();

    public Queue<Exception> Failures { get; } = new();

    public void MoveMouse(int dx, int dy)
    {
        ThrowIfConfigured();
        Events.Add($"MoveMouse:{dx}:{dy}");
    }

    public void MoveMouseAbsolute(int absoluteX, int absoluteY)
    {
        ThrowIfConfigured();
        Events.Add($"MoveMouseAbsolute:{absoluteX}:{absoluteY}");
    }

    public void MouseButton(string button, string action)
    {
        ThrowIfConfigured();
        Events.Add($"MouseButton:{button}:{action}");
    }

    public void MouseButtonAt(int absoluteX, int absoluteY, string button, string action)
    {
        ThrowIfConfigured();
        Events.Add($"MouseButtonAt:{absoluteX}:{absoluteY}:{button}:{action}");
    }

    public void Scroll(int dx, int dy)
    {
        ThrowIfConfigured();
        Events.Add($"Scroll:{dx}:{dy}");
    }

    public void ScrollAt(int absoluteX, int absoluteY, int dx, int dy)
    {
        ThrowIfConfigured();
        Events.Add($"ScrollAt:{absoluteX}:{absoluteY}:{dx}:{dy}");
    }

    public void ReleaseMouseButtons()
    {
        ThrowIfConfigured();
        Events.Add("ReleaseMouseButtons");
    }

    public void Zoom(string direction)
    {
        ThrowIfConfigured();
        Events.Add($"Zoom:{direction}");
    }

    public void TypeText(string text)
    {
        ThrowIfConfigured();
        Events.Add($"TypeText:{text}");
    }

    public void SpecialKey(string key, IReadOnlyList<string> modifiers)
    {
        ThrowIfConfigured();
        Events.Add($"SpecialKey:{key}:{string.Join(",", modifiers)}");
    }

    public void Dispose()
    {
    }

    private void ThrowIfConfigured()
    {
        if (Failures.Count > 0)
        {
            throw Failures.Dequeue();
        }
    }
}
