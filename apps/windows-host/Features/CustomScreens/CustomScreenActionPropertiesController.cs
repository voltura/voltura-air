using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenActionPropertiesController(
    CustomScreenService service,
    Func<string, Brush> brush,
    Action<CustomScreenButton> updateButton)
{
    public void Render(CustomScreenButton button, StackPanel panel)
    {
        AddTaggedChoiceProperty(
            panel,
            "Action type",
            [
                ("Built-in", "builtIn"),
                ("Key or shortcut", "shortcut"),
                ("Text", "text"),
                ("Approved application", "appLaunch")
            ],
            button.Action.Kind,
            kind =>
            {
                var action = CreateDefaultAction(kind);
                updateButton(button with
                {
                    Action = action,
                    Repeat = button.Repeat && CustomScreenService.IsRepeatable(action),
                    Presentation = CustomScreenService.RequiresLabelOnlyPresentation(action)
                        ? "label"
                        : button.Presentation,
                    Icon = action.Kind == "builtIn"
                        ? CustomScreenBuiltIns.Find(action.BuiltIn)?.Icon ?? button.Icon
                        : button.Icon
                });
            });

        var options = new VolturaAir.Host.Ui.SpacingStackPanel
        {
            Spacing = 8
        };
        panel.Children.Add(new Border
        {
            Background = brush("SurfaceRaisedBrush"),
            BorderBrush = brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = options
        });

        switch (button.Action.Kind)
        {
            case "shortcut":
                AddShortcutProperties(button, options);
                break;
            case "text":
                AddTextProperty(
                    options,
                    "Literal text",
                    button.Action.Text ?? string.Empty,
                    value => updateButton(button with
                    {
                        Action = new CustomScreenAction("text", Text: value)
                    }));
                break;
            case "appLaunch":
                AddApprovedApplicationProperty(button, options);
                break;
            default:
                AddTaggedChoiceProperty(
                    options,
                    "Built-in action",
                    [.. CustomScreenBuiltIns.All.Select(item => (item.Label, item.Id))],
                    button.Action.BuiltIn ?? "media.playPause",
                    value => updateButton(button with
                    {
                        Action = new CustomScreenAction("builtIn", BuiltIn: value),
                        Icon = CustomScreenBuiltIns.Find(value)?.Icon ?? button.Icon,
                        Repeat = button.Repeat &&
                            CustomScreenBuiltIns.Find(value)?.Repeatable == true
                    }));
                break;
        }
    }

    private void AddShortcutProperties(
        CustomScreenButton button,
        StackPanel panel)
    {
        new CustomScreenShortcutBuilder(
            panel,
            brush,
            updateButton).Render(button);
    }

    private void AddApprovedApplicationProperty(
        CustomScreenButton button,
        StackPanel panel)
    {
        var actions = service.GetApprovedAppActions();
        if (actions.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No application actions are approved in Preferences.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = brush("MutedTextBrush")
            });
            return;
        }

        AddTaggedChoiceProperty(
            panel,
            "Application",
            [.. actions.Select(action => (action.Label, action.Id))],
            button.Action.ActionId ?? actions[0].Id,
            value => updateButton(button with
            {
                Action = new CustomScreenAction("appLaunch", ActionId: value)
            }));
    }

    private CustomScreenAction CreateDefaultAction(string kind)
    {
        var approvedActions = service.GetApprovedAppActions();
        return kind switch
        {
            "shortcut" => new CustomScreenAction("shortcut", Key: "A", Modifiers: []),
            "text" => new CustomScreenAction("text", Text: "Text"),
            "appLaunch" => new CustomScreenAction(
                "appLaunch",
                ActionId: approvedActions.Count > 0 ? approvedActions[0].Id : "unavailable"),
            _ => new CustomScreenAction("builtIn", BuiltIn: "media.playPause")
        };
    }

    private void AddTextProperty(
        StackPanel panel,
        string label,
        string value,
        Action<string> update)
    {
        panel.Children.Add(CreatePropertyLabel(label));
        var input = new TextBox
        {
            Text = value,
            MaxLength = CustomScreenLimits.MaxTextLength,
            Padding = new Thickness(8, 5, 8, 5)
        };
        AutomationProperties.SetName(input, label);
        input.LostFocus += (_, _) => update(input.Text);
        panel.Children.Add(input);
    }

    private void AddTaggedChoiceProperty(
        StackPanel panel,
        string label,
        IReadOnlyList<(string Label, string Value)> options,
        string selected,
        Action<string> update)
    {
        panel.Children.Add(CreatePropertyLabel(label));
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
        panel.Children.Add(combo);
    }

    private TextBlock CreatePropertyLabel(string label) => new()
    {
        Text = label,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = brush("MutedTextBrush")
    };

}
