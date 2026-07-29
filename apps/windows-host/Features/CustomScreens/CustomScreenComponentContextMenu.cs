using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace VolturaAir.Host.Features.CustomScreens;

internal static class CustomScreenComponentContextMenu
{
    public static void Attach(
        FrameworkElement control,
        string componentName,
        string actionLabel,
        Action action,
        Action? deleteEverywhere = null)
    {
        var menu = new ContextMenu();
        var primaryItem = CreateItem(
            actionLabel,
            $"{actionLabel} {componentName}",
            action);
        menu.Items.Add(primaryItem);

        MenuItem? deleteItem = null;
        if (deleteEverywhere is not null)
        {
            deleteItem = CreateItem(
                "Delete everywhere",
                $"Delete {componentName} everywhere",
                deleteEverywhere);
            menu.Items.Add(deleteItem);
        }

        menu.Opened += (_, _) =>
        {
            menu.Style = (Style)control.FindResource(
                "CustomScreenComponentContextMenuStyle");
            var itemStyle = (Style)control.FindResource(
                "EventMultiSelectMenuItemStyle");
            primaryItem.Style = itemStyle;
            deleteItem?.SetValue(FrameworkElement.StyleProperty, itemStyle);
        };
        control.ContextMenu = menu;
    }

    private static MenuItem CreateItem(
        string label,
        string automationName,
        Action action)
    {
        var item = new MenuItem
        {
            Header = label
        };
        AutomationProperties.SetName(item, automationName);
        item.Click += (_, _) => action();
        return item;
    }
}
