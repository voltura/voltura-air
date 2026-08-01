using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.Windows.Shapes.Path;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenNavigationRingPreviewFactory
{
    public static Viewbox Create(Func<string, Brush> brush)
    {
        var ring = new Grid
        {
            Width = 180,
            Height = 180,
            IsHitTestVisible = false
        };
        ring.Children.Add(new Border
        {
            Background = brush("SurfaceRaisedBrush"),
            BorderBrush = brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(90)
        });
        ring.Children.Add(Direction("up", 0, HorizontalAlignment.Center, VerticalAlignment.Top, brush));
        ring.Children.Add(Direction("left", -90, HorizontalAlignment.Left, VerticalAlignment.Center, brush));
        ring.Children.Add(Direction("right", 90, HorizontalAlignment.Right, VerticalAlignment.Center, brush));
        ring.Children.Add(Direction("down", 180, HorizontalAlignment.Center, VerticalAlignment.Bottom, brush));

        var center = new Grid
        {
            Width = 72,
            Height = 72,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        center.Children.Add(new Border
        {
            Background = brush("SurfaceBrush"),
            BorderBrush = brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(36)
        });
        center.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            Background = brush("MutedTextBrush"),
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        ring.Children.Add(center);

        return new Viewbox
        {
            MaxHeight = 180,
            Stretch = Stretch.Uniform,
            Child = ring
        };
    }

    private static Path Direction(
        string name,
        double rotation,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        Func<string, Brush> brush)
    {
        var arrow = new Path
        {
            Data = Geometry.Parse("M 12,22 L 12,3 M 4,11 L 12,3 L 20,11"),
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            Stroke = brush("MutedTextBrush"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(20),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(rotation)
        };
        AutomationProperties.SetName(arrow, $"D-pad {name} preview");
        return arrow;
    }
}
