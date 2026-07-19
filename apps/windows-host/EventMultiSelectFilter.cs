using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MenuItem = System.Windows.Controls.MenuItem;
using UserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host;

public sealed class EventMultiSelectFilter : UserControl
{
    private readonly Button _button;
    private readonly MenuItem[] _items;
    private bool _isUpdating;

    public EventMultiSelectFilter(params (string Label, string Value)[] options)
    {
        _button = new Button
        {
            Width = 170,
            Content = "All events",
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        var menu = new ContextMenu
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = _button
        };
        _items = [.. options.Select(option => new MenuItem
        {
            Header = option.Label,
            Tag = option.Value,
            IsCheckable = true,
            StaysOpenOnClick = true
        })];
        foreach (var item in _items)
        {
            item.Checked += OnSelectionChanged;
            item.Unchecked += OnSelectionChanged;
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) =>
        {
            menu.Style = (Style)_button.FindResource("EventMultiSelectContextMenuStyle");
            var itemStyle = (Style)_button.FindResource("EventMultiSelectMenuItemStyle");
            foreach (var item in _items)
            {
                item.Style = itemStyle;
            }
        };
        _button.ContextMenu = menu;
        _button.Click += (_, _) => menu.IsOpen = true;
        Content = _button;
    }

    public IReadOnlySet<string> SelectedValues => _items
        .Where(item => item.IsChecked)
        .Select(item => (string)item.Tag)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? SelectionChanged;

    public void Clear()
    {
        _isUpdating = true;
        foreach (var item in _items)
        {
            item.IsChecked = false;
        }

        _isUpdating = false;
        UpdateLabel();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (_isUpdating)
        {
            return;
        }

        UpdateLabel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateLabel()
    {
        var selected = _items.Where(item => item.IsChecked).ToArray();
        _button.Content = selected.Length switch
        {
            0 => "All events",
            1 => selected[0].Header,
            _ => $"{selected.Length} events"
        };
    }
}
