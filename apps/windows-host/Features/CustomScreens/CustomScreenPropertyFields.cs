using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal abstract class CustomScreenPropertyFields(Func<string, Brush> brush)
{
    protected void AddTextProperty(
        StackPanel target,
        string label,
        string value,
        Action<string> update,
        int maxLength,
        bool allowEmpty = false,
        bool showLabel = true)
    {
        AddPropertyLabel(target, label, showLabel);
        var input = new TextBox
        {
            Text = value,
            MaxLength = maxLength,
            Padding = new Thickness(8, 5, 8, 5)
        };
        AutomationProperties.SetName(input, label);
        input.LostFocus += (_, _) =>
        {
            var normalized = input.Text.Trim();
            if (allowEmpty || normalized.Length > 0)
            {
                update(normalized);
            }
        };
        target.Children.Add(input);
    }

    protected void AddChoiceProperty(
        StackPanel target,
        string label,
        IReadOnlyList<string> values,
        string selected,
        Action<string> update,
        bool showLabel = true)
    {
        AddPropertyLabel(target, label, showLabel);
        var combo = new ComboBox { ItemsSource = values, SelectedItem = selected };
        AutomationProperties.SetName(combo, label);
        combo.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value && value != selected)
            {
                update(value);
            }
        };
        target.Children.Add(combo);
    }

    protected void AddTaggedChoiceProperty(
        StackPanel target,
        string label,
        IReadOnlyList<(string Label, string Value)> options,
        string selected,
        Action<string> update,
        bool showLabel = true)
    {
        AddPropertyLabel(target, label, showLabel);
        var combo = new ComboBox();
        AutomationProperties.SetName(combo, label);
        combo.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var option in options)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Value,
                IsSelected = option.Value == selected
            });
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string value } &&
                value != selected)
            {
                update(value);
            }
        };
        target.Children.Add(combo);
    }

    protected static void AddBooleanProperty(
        StackPanel target,
        string label,
        bool selected,
        Action<bool> update,
        bool enabled = true)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = selected,
            IsEnabled = enabled
        };
        AutomationProperties.SetName(checkBox, label);
        checkBox.Click += (_, _) => update(checkBox.IsChecked == true);
        target.Children.Add(checkBox);
    }

    protected static void AddComponentActions(
        StackPanel target,
        Action moveEarlier,
        Action moveLater,
        Action delete,
        bool canMoveEarlier,
        bool canMoveLater,
        string deleteAutomationName,
        string deleteLabel = "Delete",
        Action? deleteEverywhere = null,
        string? deleteEverywhereAutomationName = null)
    {
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Move up", moveEarlier, canMoveEarlier));
        actions.Children.Add(ActionButton("Move down", moveLater, canMoveLater));
        var deleteButton = ActionButton(
            deleteLabel,
            delete,
            automationName: deleteAutomationName);
        if (deleteEverywhere is null)
        {
            deleteButton.SetResourceReference(
                FrameworkElement.StyleProperty,
                "DangerButtonStyle");
        }
        actions.Children.Add(deleteButton);
        if (deleteEverywhere is not null)
        {
            var deleteEverywhereButton = ActionButton(
                "Delete everywhere",
                deleteEverywhere,
                automationName: deleteEverywhereAutomationName);
            deleteEverywhereButton.SetResourceReference(
                FrameworkElement.StyleProperty,
                "DangerButtonStyle");
            actions.Children.Add(deleteEverywhereButton);
        }
        target.Children.Add(actions);
    }

    private void AddPropertyLabel(StackPanel target, string label, bool visible)
    {
        if (!visible)
        {
            return;
        }

        target.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush("MutedTextBrush")
        });
    }

    private static Button ActionButton(
        string label,
        Action action,
        bool enabled = true,
        string? automationName = null)
    {
        var button = new Button
        {
            Content = label,
            IsEnabled = enabled,
            Margin = new Thickness(0, 0, 6, 6)
        };
        AutomationProperties.SetName(button, automationName ?? label);
        button.Click += (_, _) => action();
        return button;
    }
}
