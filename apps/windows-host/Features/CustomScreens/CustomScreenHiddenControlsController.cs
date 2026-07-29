using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenHiddenControlsController(
    FrameworkElement root,
    TextBlock hint,
    StackPanel list,
    Func<string, Brush> brush,
    Action<CustomScreenDefinition, string, string?> showComponent)
{
    public void Render(CustomScreenDefinition? draft, string orientation)
    {
        list.Children.Clear();
        if (draft?.OrientationLayoutsEnabled != true)
        {
            root.Visibility = Visibility.Collapsed;
            return;
        }

        root.Visibility = Visibility.Visible;
        hint.Text = $"Hidden in {OrientationTitle(orientation)}";
        var hiddenCount = 0;
        foreach (var section in draft.Sections.OrderBy(section =>
            CustomScreenOrientationEditing.SectionOverride(section, orientation).Order))
        {
            if (!CustomScreenOrientationEditing.SectionOverride(
                    section,
                    orientation).Visible)
            {
                Add(
                    draft,
                    $"Panel · {section.Name}",
                    section.Id,
                    buttonId: null,
                    panelName: null,
                    orientation);
                hiddenCount++;
                continue;
            }

            foreach (var button in section.Buttons.OrderBy(button =>
                CustomScreenOrientationEditing.ButtonOverride(button, orientation).Order))
            {
                if (CustomScreenOrientationEditing.ButtonOverride(
                        button,
                        orientation).Visible)
                {
                    continue;
                }
                Add(
                    draft,
                    $"Button · {button.Name}",
                    section.Id,
                    button.Id,
                    section.Name,
                    orientation);
                hiddenCount++;
            }
        }

        if (hiddenCount == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No hidden controls.",
                Foreground = brush("MutedTextBrush")
            });
        }
    }

    private void Add(
        CustomScreenDefinition draft,
        string label,
        string sectionId,
        string? buttonId,
        string? panelName,
        string orientation)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        var identity = new StackPanel();
        identity.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        if (panelName is not null)
        {
            identity.Children.Add(new TextBlock
            {
                Text = $"Panel · {panelName}",
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 11,
                Foreground = brush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        row.Children.Add(identity);
        var show = new Button
        {
            Content = "Show",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5)
        };
        var ownership = panelName is null
            ? string.Empty
            : $" from panel {panelName}";
        AutomationProperties.SetName(
            show,
            $"Show {label}{ownership} in {orientation}");
        show.Click += (_, _) => showComponent(
            CustomScreenOrientationEditing.SetComponentVisibility(
                draft,
                sectionId,
                buttonId,
                orientation,
                visible: true),
            sectionId,
            buttonId);
        Grid.SetColumn(show, 1);
        row.Children.Add(show);
        list.Children.Add(row);
    }

    private static string OrientationTitle(string orientation) =>
        orientation == "landscape" ? "Landscape" : "Portrait";
}
