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
        if (button.Action.Kind == "laserPointer")
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Laser pointer",
                FontWeight = FontWeights.SemiBold,
                Foreground = brush("TextBrush")
            });
            AddTaggedChoiceProperty(
                panel,
                "Color",
                [("Default", "default"), ("Red", "red"), ("Green", "green"), ("Blue", "blue")],
                button.Action.Color ?? "default",
                value => updateButton(button with
                {
                    Action = new CustomScreenAction("laserPointer", Color: value),
                    Repeat = false
                }));
            return;
        }

        AddTaggedChoiceProperty(
            panel,
            "Action type",
            [
                ("Keyboard shortcut", "shortcut"),
                ("Built-in control", "builtIn"),
                ("Open website", "urlOpen"),
                ("Known application", "knownApp"),
                ("Host/system action", "hostAction"),
                ("Text", "text"),
                ("Host-local application", "appLaunch")
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
            case "urlOpen":
                AddTextProperty(
                    options,
                    "Website URL (HTTP or HTTPS)",
                    button.Action.Url ?? "https://example.com/",
                    value => updateButton(button with
                    {
                        Action = new CustomScreenAction("urlOpen", Url: value)
                    }),
                    UrlOpenLimits.MaxUrlLength);
                break;
            case "knownApp":
                AddKnownApplicationProperty(button, options);
                break;
            case "hostAction":
                AddTaggedChoiceProperty(
                    options,
                    "Host/system action",
                    [.. CustomScreenHostActions.All.Select(action => (action.Label, action.Id))],
                    button.Action.ActionId ?? "power.lock",
                    value => updateButton(button with
                    {
                        Action = new CustomScreenAction("hostAction", ActionId: value)
                    }));
                options.Children.Add(new TextBlock
                {
                    Text = "Power and display actions use their dedicated host permissions. Restart and shutdown require hold-to-confirm on mobile.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = brush("MutedTextBrush")
                });
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

    private void AddKnownApplicationProperty(
        CustomScreenButton button,
        StackPanel panel)
    {
        var profiles = service.GetKnownAppProfiles();
        AddTaggedChoiceProperty(
            panel,
            "Known application",
            [.. profiles.Select(profile => (
                profile.Available ? profile.Label : $"{profile.Label} (unavailable)",
                profile.Id))],
            button.Action.ActionId ?? "browser",
            value => updateButton(button with
            {
                Action = new CustomScreenAction("knownApp", ActionId: value)
            }));
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
            "urlOpen" => new CustomScreenAction("urlOpen", Url: "https://example.com/"),
            "knownApp" => new CustomScreenAction("knownApp", ActionId: "browser"),
            "hostAction" => new CustomScreenAction("hostAction", ActionId: "power.lock"),
            _ => new CustomScreenAction("builtIn", BuiltIn: "media.playPause")
        };
    }

    private void AddTextProperty(
        StackPanel panel,
        string label,
        string value,
        Action<string> update,
        int maxLength = CustomScreenLimits.MaxTextLength)
    {
        panel.Children.Add(CreatePropertyLabel(label));
        var input = new TextBox
        {
            Text = value,
            MaxLength = maxLength,
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
