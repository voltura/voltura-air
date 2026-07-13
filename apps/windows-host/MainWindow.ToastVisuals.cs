using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfBrush = System.Windows.Media.Brush;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPanel = System.Windows.Controls.Panel;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private const string StyledToastTag = "VolturaAir.Toast.Styled";

    private void OnMainContentRootLayoutUpdated(object? sender, EventArgs e)
    {
        foreach (UIElement child in MainContentRoot.Children)
        {
            if (child is Border toast &&
                toast.HorizontalAlignment == WpfHorizontalAlignment.Right &&
                toast.VerticalAlignment == WpfVerticalAlignment.Bottom &&
                !Equals(toast.Tag, StyledToastTag) &&
                toast.Child is TextBlock messageBlock)
            {
                ApplyToastVisual(toast, toast.Tag as string ?? "Voltura Air", messageBlock.Text);
            }
        }
    }

    private void ApplyToastVisual(Border toast, string title, string message)
    {
        toast.Tag = StyledToastTag;
        toast.Background = (WpfBrush)Resources["SurfaceBrush"];
        toast.BorderBrush = (WpfBrush)Resources["BorderBrush"];
        toast.BorderThickness = new Thickness(1);
        toast.CornerRadius = new CornerRadius(4);
        toast.Padding = new Thickness(0);
        toast.Margin = new Thickness(0, 0, 0, 18);
        toast.MinWidth = 260;
        toast.MaxWidth = 380;
        toast.Effect = new DropShadowEffect
        {
            BlurRadius = 26,
            Direction = 270,
            Opacity = 0.32,
            ShadowDepth = 8,
            Color = Colors.Black
        };
        WpfPanel.SetZIndex(toast, 50);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentStrip = new Border
        {
            Background = (WpfBrush)Resources["AccentBrush"],
            CornerRadius = new CornerRadius(4, 0, 0, 4)
        };
        Grid.SetColumn(accentStrip, 0);
        layout.Children.Add(accentStrip);

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(14, 12, 10, 12),
            CornerRadius = new CornerRadius(11),
            Background = (WpfBrush)Resources["AccentBrush"],
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (WpfBrush)Resources["AccentTextBrush"],
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center
            }
        };
        Grid.SetColumn(badge, 1);
        layout.Children.Add(badge);

        var text = new StackPanel
        {
            Margin = new Thickness(0, 10, 16, 10),
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)Resources["MutedTextBrush"]
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (WpfBrush)Resources["TextBrush"]
        });
        Grid.SetColumn(text, 2);
        layout.Children.Add(text);

        toast.Child = layout;
    }
}
