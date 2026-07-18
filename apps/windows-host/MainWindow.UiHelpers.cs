using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SystemFonts = System.Windows.SystemFonts;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private Border CreateListRowShell(UIElement content)
    {
        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = content
        };
    }

    private ToggleButton CreateSegmentButton(string text, bool isChecked)
    {
        return new ToggleButton
        {
            Content = text,
            IsChecked = isChecked,
            Style = (Style)Resources["SegmentButtonStyle"]
        };
    }

    private static SpacingStackPanel CreateSegmentRow(params ToggleButton[] buttons)
    {
        var row = CreateHorizontalStack(UiTokens.SpaceSm);
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    private static void WireSegmentPair(ToggleButton first, ToggleButton second)
    {
        WireSegmentGroup(first, second);
    }

    private static void WireSegmentGroup(params ToggleButton[] buttons)
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

    private static SpacingStackPanel CreateVerticalStack(double spacing)
    {
        return new SpacingStackPanel { Spacing = spacing };
    }

    private static SpacingStackPanel CreateHorizontalStack(double spacing)
    {
        return new SpacingStackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing
        };
    }

    private static SpacingWrapPanel CreateWrap(double horizontalSpacing, double verticalSpacing)
    {
        return new SpacingWrapPanel
        {
            HorizontalSpacing = horizontalSpacing,
            VerticalSpacing = verticalSpacing
        };
    }

    private SpacingStackPanel CreateSectionPanel(double spacing = UiTokens.SpaceMd)
    {
        return new SpacingStackPanel
        {
            Background = (Brush)Resources["WindowBrush"],
            Spacing = spacing
        };
    }

    private TextBlock CreateSectionHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Resources["TextBrush"]
        };
    }

    private TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Resources["MutedTextBrush"]
        };
    }

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"]
        };
    }

    private Border CreateCardText(string title, string text, bool emphasize = false, bool monospace = false)
    {
        var stack = CreateVerticalStack(UiTokens.SpaceXs);
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = emphasize ? 18 : 13,
            FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : SystemFonts.MessageFontFamily,
            Foreground = (Brush)Resources["TextBrush"]
        });

        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = stack
        };
    }

    private Border CreateNotice(string text, bool isError)
    {
        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["TextBrush"]
            }
        };
    }

    private Button CreateButton(string text, RoutedEventHandler handler, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Style = primary
                ? (Style)Resources["PrimaryButtonStyle"]
                : danger
                    ? (Style)Resources["DangerButtonStyle"]
                    : (Style)Resources[typeof(Button)]
        };
        button.Click += handler;
        return button;
    }
}
