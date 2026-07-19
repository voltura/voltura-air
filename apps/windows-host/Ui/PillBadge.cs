using System.Windows;
using System.Windows.Controls;

namespace VolturaAir.Host.Ui;

public enum PillBadgeTone
{
    Outline,
    AccentOutline,
    DangerOutline,
    Accent,
    Success,
    Danger
}

public sealed class PillBadge : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone),
        typeof(PillBadgeTone),
        typeof(PillBadge),
        new FrameworkPropertyMetadata(PillBadgeTone.Outline));

    public PillBadge()
    {
        SetResourceReference(StyleProperty, "PillBadgeStyle");
    }

    public PillBadgeTone Tone
    {
        get => (PillBadgeTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}
