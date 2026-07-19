namespace VolturaAir.Host.Ui;

internal sealed class HostClipboardFeedback(IClipboardTextWriter clipboard, HostToastPresenter toasts)
{
    public void Copy(string value, string confirmation)
    {
        clipboard.WriteText(value);
        toasts.Show(confirmation, "Clipboard");
    }
}
