using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connect;

public partial class AdapterWarningNotice : WpfUserControl
{
    public AdapterWarningNotice()
    {
        InitializeComponent();
    }

    internal TextBlock Text => NoticeText;

    public void SetMessage(string? message, string? emphasis = null)
    {
        NoticeText.Inlines.Clear();
        if (string.IsNullOrWhiteSpace(message))
        {
            NoticeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        NoticeBorder.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(emphasis))
        {
            NoticeText.Inlines.Add(new Run(message));
            return;
        }

        var emphasisIndex = message.IndexOf(emphasis, StringComparison.Ordinal);
        if (emphasisIndex < 0)
        {
            NoticeText.Inlines.Add(new Run(message));
            return;
        }

        if (emphasisIndex > 0)
        {
            NoticeText.Inlines.Add(new Run(message[..emphasisIndex]));
        }

        NoticeText.Inlines.Add(new Run(emphasis) { FontWeight = FontWeights.Bold });
        var suffixIndex = emphasisIndex + emphasis.Length;
        if (suffixIndex < message.Length)
        {
            NoticeText.Inlines.Add(new Run(message[suffixIndex..]));
        }
    }
}
