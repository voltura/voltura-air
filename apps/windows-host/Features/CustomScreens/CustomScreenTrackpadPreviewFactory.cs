using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenTrackpadPreviewFactory
{
    public static Grid Create(
        CustomScreenSection section,
        Func<string, Brush> brush)
    {
        var root = new Grid { MinHeight = 100 };
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.Children.Add(new Border
        {
            Background = brush("WindowBrush"),
            BorderBrush = brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = new TextBlock
            {
                Text = "Trackpad",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = brush("MutedTextBrush")
            }
        });

        if (section.TrackpadFullscreenControl)
        {
            var fullscreen = new Button
            {
                Content = "⛶",
                Width = 38,
                Height = 38,
                Padding = new Thickness(0),
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            AutomationProperties.SetName(
                fullscreen,
                "Fullscreen trackpad control");
            root.Children.Add(fullscreen);
        }

        var buttons = CreateClickButtons(section);
        if (buttons.Count > 0)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(8)
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            var buttonRow = new UniformGrid
            {
                Rows = 1,
                Columns = buttons.Count
            };
            foreach (var button in buttons)
            {
                button.Margin = new Thickness(
                    buttonRow.Children.Count == 0 ? 0 : 4,
                    0,
                    buttonRow.Children.Count + 1 == buttons.Count ? 0 : 4,
                    0);
                buttonRow.Children.Add(button);
            }
            Grid.SetRow(buttonRow, 2);
            root.Children.Add(buttonRow);
        }
        return root;
    }

    private static List<Button> CreateClickButtons(
        CustomScreenSection section)
    {
        var buttons = new List<Button>();
        void Add(string click) => buttons.Add(new Button
        {
            Content = click,
            MinHeight = 52,
            IsHitTestVisible = false
        });

        if (section.TrackpadButtonSide == "left")
        {
            if (section.TrackpadRightClick) Add("Right");
            if (section.TrackpadLeftClick) Add("Left");
        }
        else
        {
            if (section.TrackpadLeftClick) Add("Left");
            if (section.TrackpadRightClick) Add("Right");
        }
        return buttons;
    }
}
