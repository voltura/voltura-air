using System.Windows;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Thickness = System.Windows.Thickness;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenVolumePreviewFactory
{
    public static Grid Create(Func<string, Brush> brush)
    {
        var layout = new Grid
        {
            IsHitTestVisible = false,
            MinHeight = 52
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(48)
        });
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        layout.Children.Add(new TextBlock
        {
            Text = "Vol",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(slider, 1);
        layout.Children.Add(slider);
        var value = new TextBlock
        {
            Text = "50%",
            Foreground = brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(value, 2);
        layout.Children.Add(value);
        return layout;
    }
}
