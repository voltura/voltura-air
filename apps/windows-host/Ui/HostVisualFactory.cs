using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using SystemFonts = System.Windows.SystemFonts;

namespace VolturaAir.Host.Ui;

internal sealed class HostVisualFactory(ResourceDictionary resources)
{
    public Brush Brush(string key) => (Brush)resources[key];

    public Style Style(object key) => (Style)resources[key];

    public Border CreateListRowShell(UIElement content)
    {
        return new Border
        {
            Background = Brush("SurfaceBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = content
        };
    }

    public CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = Brush("TextBrush")
        };
    }

    public ToggleButton CreateSegmentButton(string text, bool isChecked)
    {
        return new ToggleButton
        {
            Content = text,
            IsChecked = isChecked,
            Style = Style("SegmentButtonStyle")
        };
    }

    public static SpacingStackPanel CreateSegmentRow(params ToggleButton[] buttons)
    {
        var row = CreateHorizontalStack(UiTokens.SpaceSm);
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    public static void WireSegmentPair(ToggleButton first, ToggleButton second)
    {
        WireSegmentGroup(first, second);
    }

    public static void WireSegmentGroup(params ToggleButton[] buttons)
    {
        foreach (var button in buttons)
        {
            button.Click += (_, _) =>
            {
                foreach (var candidate in buttons)
                {
                    candidate.IsChecked = ReferenceEquals(candidate, button);
                }
            };
        }
    }

    public static SpacingStackPanel CreateVerticalStack(double spacing)
    {
        return new SpacingStackPanel { Spacing = spacing };
    }

    public static SpacingStackPanel CreateHorizontalStack(double spacing)
    {
        return new SpacingStackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing
        };
    }

    public static SpacingWrapPanel CreateWrap(double horizontalSpacing, double verticalSpacing)
    {
        return new SpacingWrapPanel
        {
            HorizontalSpacing = horizontalSpacing,
            VerticalSpacing = verticalSpacing
        };
    }

    public SpacingStackPanel CreateSectionPanel(double spacing = UiTokens.SpaceMd)
    {
        return new SpacingStackPanel
        {
            Background = Brush("WindowBrush"),
            Spacing = spacing
        };
    }

    public TextBlock CreateSectionHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextBrush")
        };
    }

    public TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("MutedTextBrush")
        };
    }

    public TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        };
    }

    public Border CreateCardText(string title, string text, bool emphasize = false, bool monospace = false)
    {
        var stack = CreateVerticalStack(UiTokens.SpaceXs);
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("MutedTextBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = emphasize ? 18 : 13,
            FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : SystemFonts.MessageFontFamily,
            Foreground = Brush("TextBrush")
        });

        return new Border
        {
            Background = Brush("SurfaceBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = stack
        };
    }

    public Border CreateNotice(string text, bool isError)
    {
        return new Border
        {
            Background = Brush("SurfaceBrush"),
            BorderBrush = isError ? Brush("DangerBrush") : Brush("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = isError ? Brush("DangerBrush") : Brush("TextBrush")
            }
        };
    }

    public Button CreateButton(string text, RoutedEventHandler handler, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Style = primary
                ? Style("PrimaryButtonStyle")
                : danger
                    ? Style("DangerButtonStyle")
                    : Style(typeof(Button))
        };
        button.Click += handler;
        return button;
    }
}
