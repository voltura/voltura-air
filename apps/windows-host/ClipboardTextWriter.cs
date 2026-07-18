namespace VolturaAir.Host;

public interface IClipboardTextWriter
{
    void WriteText(string text);
}

internal sealed class WindowsClipboardTextWriter : IClipboardTextWriter
{
    public void WriteText(string text) => System.Windows.Clipboard.SetText(text);
}
