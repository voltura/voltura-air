using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private const string StyledToastTag = "VolturaAir.Toast.Styled";

    private void OnMainContentRootLayoutUpdated(object? sender, EventArgs e)
    {
        foreach (UIElement child in MainContentRoot.Children)
        {
            if (child is Border toast &&
                toast.HorizontalAlignment == HorizontalAlignment.Right &&
                toast.VerticalAlignment == VerticalAlignment.Bottom &&
                !Equals(toast.Tag, StyledToastTag) &&
                toast.Child is TextBlock messageBlock)
            {
                ApplyToastVisual(toast, messageBlock.Text);
            }
        }
    }

    private void ApplyToastVisual(Border toast, string message)
    {
        toast.Tag = StyledToastTag;
        toast.Background = (Brush)Resources["SurfaceBrush"];
        toast.BorderBrush = (Brush)Resources["AccentBrush"];
        toast.BorderThickness = new Thickness(1);
        toast.CornerRadius = new CornerRadius(14);
        toast.Padding = new Thickness(0);
        toast.Margin = new Thickness(0, 0, 0, 18);
        toast.MinWidth = 240;
        toast.MaxWidth = 360;
        toast.Effect = new DropShadowEffect
        {
            BlurRadius = 28,
            Direction = 270,
            Opacity = 0.28,
            ShadowDepth = 7,
            Color = Colors.Black
        };
        Panel.SetZIndex(toast, 50);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentStrip = new Border
        {
            Background = (Brush)Resources["AccentBrush"],
            CornerRadius = new CornerRadius(14, 0, 0, 14)
        };
        Grid.SetColumn(accentStrip, 0);
        layout.Children.Add(accentStrip);

        var badge = new Border
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(14, 10, 10, 10),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)Resources["AccentBrush"],
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["AccentTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(badge, 1);
        layout.Children.Add(badge);

        var text = new StackPanel
        {
            Margin = new Thickness(0, 10, 16, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Text = "Clipboard",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"]
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Resources["TextBrush"]
        });
        Grid.SetColumn(text, 2);
        layout.Children.Add(text);

        toast.Child = layout;
    }
}
