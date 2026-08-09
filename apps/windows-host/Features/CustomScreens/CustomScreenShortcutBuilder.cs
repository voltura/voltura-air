using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenShortcutBuilder(
    StackPanel panel,
    Func<string, Brush> brush,
    Action<CustomScreenButton> updateButton)
{
    private static readonly (string Label, string Value)[] ModifierOptions =
    [
        ("CTRL", "Control"),
        ("ALT", "Alt"),
        ("SHIFT", "Shift"),
        ("ALT GR", "AltGr"),
        ("WIN", "Win")
    ];

    private static readonly (string Label, string Value)[] SpecialKeyOptions =
    [
        ("Backspace", "Backspace"),
        ("Delete", "Delete"),
        ("Enter", "Enter"),
        ("Insert", "Insert"),
        ("Tab", "Tab"),
        ("Escape", "Escape"),
        ("Space", "Space"),
        ("Page up", "PageUp"),
        ("Page down", "PageDown"),
        ("Home", "Home"),
        ("End", "End"),
        ("Left", "ArrowLeft"),
        ("Right", "ArrowRight"),
        ("Up", "ArrowUp"),
        ("Down", "ArrowDown")
    ];

    private static readonly (string Label, string Value)[] SymbolKeyOptions =
    [
        ("Period  .", "."),
        ("Comma  ,", ","),
        ("Semicolon  ;", ";"),
        ("Slash  /", "/"),
        ("Backslash  \\", "\\"),
        ("Apostrophe  '", "'"),
        ("Backtick  `", "`"),
        ("Left bracket  [", "["),
        ("Right bracket  ]", "]"),
        ("Minus  -", "-"),
        ("Equals  =", "=")
    ];

    public void Render(CustomScreenButton button)
    {
        var modifiers = (button.Action.Modifiers ?? []).ToList();
        var key = CustomScreenShortcutKeys.TryNormalize(
            button.Action.Key,
            out var normalizedKey)
            ? normalizedKey
            : null;
        var usesFunctionSelector = key is not null &&
            CustomScreenShortcutKeys.FunctionKeys.Contains(
                key,
                StringComparer.Ordinal);
        var usesSpecialSelector = key is not null &&
            CustomScreenShortcutKeys.SpecialKeys.Contains(
                key,
                StringComparer.Ordinal);
        var usesSymbolSelector = key is not null &&
            CustomScreenShortcutKeys.SymbolKeys.Contains(
                key,
                StringComparer.Ordinal);
        var usesNumpadOrMediaSelector = key is not null &&
            CustomScreenShortcutKeys.NumpadAndMediaKeys.Contains(
                key,
                StringComparer.Ordinal);

        panel.Children.Add(Label("Modifiers"));
        var availableModifiers = new WrapPanel();
        panel.Children.Add(availableModifiers);
        panel.Children.Add(new TextBlock
        {
            Text = "Modifiers move into the command as they are selected.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = brush("MutedTextBrush")
        });

        panel.Children.Add(Label("Letter or number"));
        var keyInput = new ComboBox
        {
            ItemsSource = CustomScreenShortcutKeys.Suggestions,
            SelectedItem = usesFunctionSelector || usesSpecialSelector ||
                usesSymbolSelector || usesNumpadOrMediaSelector
                ? null
                : key
        };
        AutomationProperties.SetName(keyInput, "Command letter or number");
        keyInput.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ModernComboBoxStyle");
        panel.Children.Add(keyInput);

        panel.Children.Add(Label("Function key"));
        var functionInput = new ComboBox
        {
            ItemsSource = CustomScreenShortcutKeys.FunctionKeys,
            SelectedItem = usesFunctionSelector ? key : null
        };
        AutomationProperties.SetName(functionInput, "Function key");
        functionInput.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ModernComboBoxStyle");
        panel.Children.Add(functionInput);

        panel.Children.Add(Label("Special key"));
        var specialInput = new ComboBox();
        foreach (var option in SpecialKeyOptions)
        {
            specialInput.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Value,
                IsSelected = option.Value == key
            });
        }
        AutomationProperties.SetName(specialInput, "Special key");
        specialInput.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ModernComboBoxStyle");
        panel.Children.Add(specialInput);

        panel.Children.Add(Label("Symbol key"));
        var symbolInput = new ComboBox();
        foreach (var option in SymbolKeyOptions)
        {
            symbolInput.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Value,
                IsSelected = option.Value == key
            });
        }
        AutomationProperties.SetName(symbolInput, "Symbol key");
        symbolInput.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ModernComboBoxStyle");
        panel.Children.Add(symbolInput);

        panel.Children.Add(Label("Numpad or media key"));
        var numpadAndMediaInput = new ComboBox
        {
            ItemsSource = CustomScreenShortcutKeys.NumpadAndMediaKeys,
            SelectedItem = usesNumpadOrMediaSelector ? key : null
        };
        AutomationProperties.SetName(numpadAndMediaInput, "Numpad or media key");
        numpadAndMediaInput.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ModernComboBoxStyle");
        panel.Children.Add(numpadAndMediaInput);

        panel.Children.Add(Label("Command"));
        var commandText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(commandText, "Command preview");
        var command = new Border
        {
            MinHeight = 44,
            Padding = new Thickness(10, 7, 10, 7),
            Background = brush("SurfaceRaisedBrush"),
            BorderBrush = brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = commandText
        };
        panel.Children.Add(command);

        var feedback = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = brush("MutedTextBrush")
        };
        panel.Children.Add(feedback);

        var actions = new WrapPanel();
        var reset = ActionButton("Reset command", "Reset command");
        var save = ActionButton("Save command", "Save command");
        save.SetResourceReference(
            FrameworkElement.StyleProperty,
            "PrimaryButtonStyle");
        actions.Children.Add(reset);
        actions.Children.Add(save);
        panel.Children.Add(actions);

        void Refresh()
        {
            availableModifiers.Children.Clear();
            foreach (var option in ModifierOptions.Where(option =>
                !modifiers.Contains(option.Value, StringComparer.Ordinal) &&
                IsCompatibleModifier(option.Value, modifiers)))
            {
                var modifier = ActionButton(
                    option.Label,
                    $"Add {option.Label} modifier");
                modifier.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    "ChoiceStateButtonStyle");
                modifier.Click += (_, _) =>
                {
                    modifiers.Add(option.Value);
                    Refresh();
                };
                availableModifiers.Children.Add(modifier);
            }

            var preview = CommandText(modifiers, key);
            commandText.Text = preview.Length == 0 ? "No command yet" : preview;
            feedback.Text = key is null
                ? "Choose a letter, number, function, special, or symbol key."
                : "Ready to save this command.";
            save.IsEnabled = key is not null;
        }

        void SelectKey(string selected, ComboBox sourceSelector)
        {
            if (!CustomScreenShortcutKeys.TryNormalize(selected, out var nextKey))
            {
                return;
            }
            key = nextKey;
            if (!ReferenceEquals(sourceSelector, keyInput))
            {
                keyInput.SelectedItem = null;
            }
            if (!ReferenceEquals(sourceSelector, functionInput))
            {
                functionInput.SelectedItem = null;
            }
            if (!ReferenceEquals(sourceSelector, specialInput))
            {
                specialInput.SelectedItem = null;
            }
            if (!ReferenceEquals(sourceSelector, symbolInput))
            {
                symbolInput.SelectedItem = null;
            }
            if (!ReferenceEquals(sourceSelector, numpadAndMediaInput))
            {
                numpadAndMediaInput.SelectedItem = null;
            }
            Refresh();
        }

        keyInput.SelectionChanged += (_, _) =>
        {
            if (keyInput.SelectedItem is string selected)
            {
                SelectKey(selected, keyInput);
            }
        };
        functionInput.SelectionChanged += (_, _) =>
        {
            if (functionInput.SelectedItem is string selected)
            {
                SelectKey(selected, functionInput);
            }
        };
        specialInput.SelectionChanged += (_, _) =>
        {
            if (specialInput.SelectedItem is ComboBoxItem { Tag: string selected })
            {
                SelectKey(selected, specialInput);
            }
        };
        symbolInput.SelectionChanged += (_, _) =>
        {
            if (symbolInput.SelectedItem is ComboBoxItem { Tag: string selected })
            {
                SelectKey(selected, symbolInput);
            }
        };
        numpadAndMediaInput.SelectionChanged += (_, _) =>
        {
            if (numpadAndMediaInput.SelectedItem is string selected)
            {
                SelectKey(selected, numpadAndMediaInput);
            }
        };
        reset.Click += (_, _) =>
        {
            modifiers.Clear();
            key = null;
            keyInput.SelectedItem = null;
            functionInput.SelectedItem = null;
            specialInput.SelectedItem = null;
            symbolInput.SelectedItem = null;
            numpadAndMediaInput.SelectedItem = null;
            Refresh();
        };
        save.Click += (_, _) =>
        {
            if (key is null)
            {
                return;
            }

            updateButton(button with
            {
                Action = new CustomScreenAction(
                    "shortcut",
                    Key: key,
                    Modifiers: [.. modifiers]),
                Repeat = false
            });
        };
        Refresh();
    }

    private TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = brush("MutedTextBrush")
    };

    private static Button ActionButton(string content, string automationName)
    {
        var button = new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 6, 6)
        };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static string CommandText(
        IReadOnlyList<string> modifiers,
        string? key)
    {
        var labels = modifiers.Select(modifier =>
            ModifierOptions.First(option => option.Value == modifier).Label);
        return string.Join(
            " + ",
            key is null ? labels : labels.Append(key));
    }

    private static bool IsCompatibleModifier(
        string candidate,
        IReadOnlyCollection<string> selected)
    {
        if (candidate == "AltGr")
        {
            return !selected.Contains("Control", StringComparer.Ordinal) &&
                !selected.Contains("Alt", StringComparer.Ordinal);
        }

        return candidate is not ("Control" or "Alt") ||
            !selected.Contains("AltGr", StringComparer.Ordinal);
    }
}
