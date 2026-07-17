using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VolturaAir.Host.Ui;
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

    private Border CreateDeviceListRow(DeviceListItem device)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateListCell("Device", device.Name, 0, strong: true, trim: true));
        grid.Children.Add(CreateListCell("Status", device.Status, 2, trim: true));
        var activity = CreateListCell("Activity", device.Activity, 4, trim: true);
        var details = CreateListCell("Details", string.IsNullOrWhiteSpace(device.Metadata) ? "No metadata" : device.Metadata, 6, trim: true);
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
        grid.ColumnDefinitions[1].Width = new GridLength(UiTokens.SpaceMd);
        grid.ColumnDefinitions[2].Width = new GridLength(150);
        grid.ColumnDefinitions[3].Width = showFullRow ? new GridLength(UiTokens.SpaceMd) : new GridLength(0);
        grid.ColumnDefinitions[4].Width = showFullRow ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        grid.ColumnDefinitions[5].Width = showFullRow ? new GridLength(UiTokens.SpaceMd) : new GridLength(0);
        grid.ColumnDefinitions[6].Width = showFullRow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        foreach (var cell in optionalCells)
        {
            cell.Visibility = showFullRow ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Border CreateDiagnosticRow(DiagnosticItem detail)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(CreateDiagnosticCell(detail.Name, 0, strong: true));
        grid.Children.Add(CreateDiagnosticCell(detail.Value, 2, monospace: true));
        var copy = CreateButton("Copy", (_, _) => CopyToClipboard($"{detail.Name}: {detail.Value}", "Copied"));
        Grid.SetColumn(copy, 4);
        grid.Children.Add(copy);
        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = grid
        };
    }

    private Grid CreateDiagnosticsHeaderRow()
    {
        var grid = new Grid { Margin = new Thickness(UiTokens.SpaceSm, 0, UiTokens.SpaceSm, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(CreateDiagnosticsColumnHeader("Name", 0));
        grid.Children.Add(CreateDiagnosticsColumnHeader("Value", 2));
        return grid;
    }

    private TextBlock CreateDiagnosticsColumnHeader(string text, int column)
    {
        var header = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["MutedTextBrush"]
        };
        Grid.SetColumn(header, column);
        return header;
    }

    private TextBlock CreateDiagnosticCell(string value, int column, bool strong = false, bool monospace = false)
    {
        var cell = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = strong ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : SystemFonts.MessageFontFamily,
            Foreground = (Brush)Resources["TextBrush"]
        };
        Grid.SetColumn(cell, column);
        return cell;
    }

    private SpacingStackPanel CreateListCell(string label, string value, int column, bool strong = false, bool monospace = false, bool trim = false)
    {
        var stack = CreateVerticalStack(UiTokens.SpaceXs);
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
