using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.Windows.Shapes.Path;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;

namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenPaletteGhostFactory
{
    public static FrameworkElement Create(
        string kind,
        FrameworkElement resourceSource,
        double previewWidth)
    {
        var width = Math.Max(180, previewWidth);
        return kind switch
        {
            "new-button" => CreateButton(resourceSource),
            "new-volume" => CreateVolume(resourceSource, width),
            "new-trackpad" => CreateTrackpad(resourceSource, width),
            "new-collapsible-trackpad" => CreateCollapsibleTrackpad(resourceSource, width),
            "new-navigation-ring" => CreateNavigationRing(resourceSource, width),
            "new-dpad" => CreateNavigationRing(resourceSource, width),
            "new-collapsible" => CreateCollapsible(resourceSource, width),
            _ => CreatePanel(resourceSource, width)
        };
    }

    private static Border CreatePanel(
        FrameworkElement resources,
        double width) =>
        Card(
            resources,
            width,
            84,
            new TextBlock
            {
                Text = "New panel",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(resources, "TextBrush")
            });

    private static Border CreateCollapsible(
        FrameworkElement resources,
        double width)
    {
        var header = new DockPanel { LastChildFill = true };
        var chevron = new Path
        {
            Width = 12,
            Height = 7,
            Margin = new Thickness(10, 0, 2, 0),
            Data = Geometry.Parse("M 1 1 L 6 6 L 11 1"),
            Stroke = Brush(resources, "MutedTextBrush"),
            StrokeThickness = 1.8,
            Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(chevron, Dock.Right);
        header.Children.Add(chevron);
        header.Children.Add(new TextBlock
        {
            Text = "Collapsible panel",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(resources, "TextBrush")
        });
        return Card(resources, width, 84, header);
    }

    private static Border CreateButton(FrameworkElement resources) =>
        Card(
            resources,
            104,
            52,
            new TextBlock
            {
                Text = "[play]  New button",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(resources, "TextBrush")
            });

    private static Border CreateVolume(
        FrameworkElement resources,
        double width)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = "Vol",
            Foreground = Brush(resources, "TextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(slider, 1);
        root.Children.Add(slider);
        var value = new TextBlock
        {
            Text = "50%",
            Foreground = Brush(resources, "MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(value, 2);
        root.Children.Add(value);
        return Card(resources, width, 68, root);
    }

    private static Border CreateTrackpad(
        FrameworkElement resources,
        double width)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new Border
        {
            Background = Brush(resources, "WindowBrush"),
            BorderBrush = Brush(resources, "BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = new TextBlock
            {
                Text = "Trackpad",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(resources, "MutedTextBrush")
            }
        });
        var clicks = new UniformGrid { Columns = 2, Rows = 1 };
        clicks.Children.Add(ClickButton("Left"));
        clicks.Children.Add(ClickButton("Right"));
        Grid.SetRow(clicks, 2);
        root.Children.Add(clicks);
        return Card(resources, width, 200, root);
    }

    private static Border CreateCollapsibleTrackpad(
        FrameworkElement resourceSource,
        double width)
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "Collapsible trackpad",
            Foreground = Brush(resourceSource, "TextBrush"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new Border
        {
            Height = 88,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(resourceSource, "BorderBrush"),
            Child = new TextBlock
            {
                Text = "Trackpad",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(resourceSource, "MutedTextBrush")
            }
        });
        return Card(resourceSource, width, 160, root);
    }

    private static Border CreateNavigationRing(
        FrameworkElement resourceSource,
        double width) =>
        Card(
            resourceSource,
            width,
            200,
            CustomScreenNavigationRingPreviewFactory.Create(
                key => Brush(resourceSource, key)));

    private static Button ClickButton(string label) => new()
    {
        Content = label,
        IsHitTestVisible = false,
        Margin = new Thickness(3, 0, 3, 0),
        MinHeight = 52
    };

    private static Border Card(
        FrameworkElement resources,
        double width,
        double height,
        UIElement content) => new()
        {
            Width = width,
            Height = height,
            Padding = new Thickness(10),
            Background = Brush(resources, "SurfaceBrush"),
            BorderBrush = Brush(resources, "BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };

    private static Brush Brush(FrameworkElement source, string key) =>
        source.TryFindResource(key) as Brush ?? Brushes.Transparent;
}
