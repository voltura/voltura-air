using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SystemFonts = System.Windows.SystemFonts;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private ListBox CreateModernList<T>(IEnumerable<T> items, Func<T, UIElement> createRow)
    {
        var list = new ListBox
        {
            Style = (Style)Resources["ModernListBoxStyle"],
            ItemContainerStyle = (Style)Resources["ModernListBoxItemStyle"]
        };

        foreach (var item in items)
        {
            list.Items.Add(new ListBoxItem
            {
                Tag = item,
                Style = (Style)Resources["ModernListBoxItemStyle"],
                Content = createRow(item)
            });
        }

        return list;
    }

    private UIElement CreateDeviceListRow(DeviceListItem device)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateListCell("Device", device.Name, 0, strong: true, trim: true));
        grid.Children.Add(CreateListCell("Status", device.Status, 1, trim: true));
        var activity = CreateListCell("Activity", device.Activity, 2, trim: true);
        var details = CreateListCell("Details", string.IsNullOrWhiteSpace(device.Metadata) ? "No metadata" : device.Metadata, 3, trim: true);
        grid.Children.Add(activity);
        grid.Children.Add(details);

        grid.SizeChanged += (_, _) => UpdateDeviceListRowColumns(grid, activity, details);
        grid.Loaded += (_, _) => UpdateDeviceListRowColumns(grid, activity, details);
        return CreateListRowShell(grid);
    }

    private static void UpdateDeviceListRowColumns(Grid grid, params UIElement[] optionalCells)
    {
        var showFullRow = grid.ActualWidth >= FullDeviceRowMinimumWidth;
        grid.ColumnDefinitions[0].Width = showFullRow
            ? new GridLength(1.5, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = new GridLength(150);
        grid.ColumnDefinitions[2].Width = showFullRow ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        grid.ColumnDefinitions[3].Width = showFullRow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        foreach (var cell in optionalCells)
        {
            cell.Visibility = showFullRow ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private UIElement CreateCandidateListRow(CandidateListItem candidate)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateListCell("Adapter", candidate.Adapter, 0, strong: true));
        grid.Children.Add(CreateListCell("Address", candidate.Address, 1, monospace: true));
        grid.Children.Add(CreateListCell("Status", string.IsNullOrWhiteSpace(candidate.Status) ? "Available" : candidate.Status, 2));
        return CreateListRowShell(grid);
    }

    private UIElement CreateDiagnosticRow(DiagnosticItem detail)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(CreateListCell("Name", detail.Name, 0, strong: true));
        grid.Children.Add(CreateListCell("Value", detail.Value, 1, monospace: true));
        var copy = CreateButton("Copy", (_, _) => CopyToClipboard(detail.Value, "Copied"));
        copy.Margin = new Thickness(12, 8, 0, 8);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);
        return CreateListRowShell(grid);
    }

    private UIElement CreateListCell(string label, string value, int column, bool strong = false, bool monospace = false, bool trim = false)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"],
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextWrapping = trim ? TextWrapping.NoWrap : TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = trim ? TextWrapping.NoWrap : TextWrapping.Wrap,
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            FontSize = 13,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : SystemFonts.MessageFontFamily,
            Foreground = (Brush)Resources["TextBrush"]
        });
        Grid.SetColumn(stack, column);
        return stack;
    }

    private UIElement CreateListRowShell(UIElement content)
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

    private StackPanel CreateSegmentRow(params ToggleButton[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 12)
        };
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

    private StackPanel CreateSectionPanel()
    {
        return new StackPanel
        {
            Background = (Brush)Resources["WindowBrush"]
        };
    }

    private TextBlock CreateSectionHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Resources["TextBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Resources["MutedTextBrush"],
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"],
            Margin = new Thickness(0, 12, 0, 0)
        };
    }

    private UIElement CreateDetailsDisclosure(string topic, params string[] paragraphs)
    {
        var details = new StackPanel { Visibility = Visibility.Collapsed };
        foreach (var paragraph in paragraphs)
        {
            details.Children.Add(CreateMutedText(paragraph));
        }

        var toggle = CreateButton($"More about {topic}", (_, _) => { });
        toggle.Click += (_, _) =>
        {
            var showDetails = details.Visibility != Visibility.Visible;
            details.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = showDetails ? $"Hide {topic} details" : $"More about {topic}";
        };
        toggle.HorizontalAlignment = HorizontalAlignment.Left;

        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        container.Children.Add(toggle);
        container.Children.Add(details);
        return container;
    }

    private Border CreateCardText(string title, string text, bool emphasize = false, bool monospace = false)
    {
        var stack = new StackPanel();
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
            Margin = new Thickness(0, 5, 0, 0),
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
            Margin = new Thickness(0, 0, 0, 12),
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
            Margin = new Thickness(0, 0, 0, 12),
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
            Background = primary ? (Brush)Resources["AccentBrush"] : (Brush)Resources["SurfaceRaisedBrush"],
            Foreground = primary ? (Brush)Resources["AccentTextBrush"] : danger ? (Brush)Resources["DangerBrush"] : (Brush)Resources["TextBrush"],
            BorderBrush = primary ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderBrush"]
        };
        button.Click += handler;
        return button;
    }
}
