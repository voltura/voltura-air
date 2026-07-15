namespace VolturaAir.Host;

public sealed record ClipboardTextReadResult(bool Succeeded, string? Text, string? Code, string Message);

public interface IClipboardTextReader
{
    ClipboardTextReadResult ReadText();
}

internal sealed class WindowsClipboardTextReader : IClipboardTextReader
{
    public ClipboardTextReadResult ReadText()
    {
        try
        {
            var application = System.Windows.Application.Current;
            if (application is null)
            {
                return Failed("VAIR-CLIPBOARD-UNAVAILABLE", "Windows clipboard text is unavailable. Try again.");
            }

            return application.Dispatcher.Invoke(ReadTextOnDispatcher);
        }
        catch
        {
            return Failed("VAIR-CLIPBOARD-UNAVAILABLE", "Windows clipboard text is unavailable. Try again.");
        }
    }

    private static ClipboardTextReadResult ReadTextOnDispatcher()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return Failed("VAIR-CLIPBOARD-NO-TEXT", "The PC clipboard does not contain text.");
        }

        var text = System.Windows.Clipboard.GetText();
        if (text.Length > TextTransferLimits.MaxTextLength)
        {
            return Failed("VAIR-CLIPBOARD-TEXT-TOO-LONG", $"PC clipboard text is too long. Get text from PC supports up to {TextTransferLimits.MaxTextLength:N0} characters.");
        }

        return new ClipboardTextReadResult(true, text, null, "Text fetched from the PC clipboard.");
    }

    private static ClipboardTextReadResult Failed(string code, string message) => new(false, null, code, message);
}
