using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class AppLaunchSettingsSection(
    Window owner,
    IAppLaunchService appLaunchService,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    HostToastPresenter toasts,
    Func<bool> isLoading,
    Action refresh)
{
    private bool _synchronizing;

    public void AddTo(StackPanel parent)
    {
        var configured = AppLaunchSettings.GetActions();
        parent.Children.Add(visuals.CreateMutedText("Choose the app-launch buttons available from Remote mode."));
        parent.Children.Add(CreatePresetHeader());

        foreach (var preset in AppLaunchSettings.GetPresets())
        {
            parent.Children.Add(CreatePresetRow(
                preset,
                configured.FirstOrDefault(action => action.Id == preset.Id)));
        }

        var details = preferenceVisuals.AddNestedSection(parent, "More about app-launch buttons");
        details.Children.Add(visuals.CreateMutedText($"The global and per-device application-launch permissions still apply. Button labels are 1–{AppLaunchSettings.MaxLabelLength} characters and save automatically. Browser opens the default browser; Spotify, VLC, and PowerPoint must be installed and registered with Windows."));
        parent.Children.Add(visuals.CreateLabel("Custom buttons"));
        var customActions = configured.Where(action => action.Kind == AppLaunchKind.Custom).ToArray();
        if (customActions.Length == 0)
        {
            parent.Children.Add(visuals.CreateMutedText("No custom application commands are approved."));
        }
        else
        {
            foreach (var action in customActions)
            {
                parent.Children.Add(CreateCustomActionRow(action));
            }
        }

        var add = visuals.CreateButton("Add custom button", (_, _) => OpenEditor(), primary: true);
        add.IsEnabled = configured.Count < AppLaunchSettings.MaxActions;
        add.HorizontalAlignment = HorizontalAlignment.Left;
        parent.Children.Add(add);
        parent.Children.Add(visuals.CreateMutedText("Custom buttons use a locally approved .exe and optional arguments; paths never leave this PC."));
    }

    private Grid CreatePresetHeader()
    {
        var header = CreatePresetGrid();
        header.Children.Add(new TextBlock
        {
            Text = "Presets",
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush")
        });

        var label = new TextBlock
        {
            Text = "Button label",
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush")
        };
        Grid.SetColumn(label, 2);
        header.Children.Add(label);

        var test = new TextBlock
        {
            Text = "Test",
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush")
        };
        Grid.SetColumn(test, 4);
        header.Children.Add(test);
        return header;
    }

    private Grid CreatePresetRow(AppLaunchAction preset, AppLaunchAction? configured)
    {
        var presetName = AppLaunchSettings.GetPresetName(preset.Kind);
        var row = CreatePresetGrid();
        var checkBox = visuals.CreateCheckBox($"Show {presetName}", configured is not null, minimumWidth: 180);
        checkBox.VerticalAlignment = VerticalAlignment.Center;
        checkBox.Checked += (_, _) => SetPreset(preset.Kind, true, checkBox);
        checkBox.Unchecked += (_, _) => SetPreset(preset.Kind, false, checkBox);
        row.Children.Add(checkBox);

        var labelInput = new TextBox
        {
            Text = configured?.Label ?? preset.Label,
            MaxLength = AppLaunchSettings.MaxLabelLength,
            Height = 32,
            Width = 140,
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = configured is not null,
            Background = visuals.Brush("SurfaceRaisedBrush"),
            Foreground = visuals.Brush("TextBrush"),
            BorderBrush = visuals.Brush("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(labelInput, $"{presetName} button label");
        AutomationProperties.SetHelpText(labelInput, "Changes are saved automatically.");
        Grid.SetColumn(labelInput, 2);
        row.Children.Add(labelInput);
        labelInput.TextChanged += (_, _) => SavePresetLabel(preset.Kind, labelInput);

        var test = visuals.CreateButton("Test", (_, _) => TestAction(configured!));
        test.IsEnabled = configured is not null;
        AutomationProperties.SetName(test, $"Test {presetName} launch");
        AutomationProperties.SetHelpText(test, configured is null
            ? $"Enable {presetName} before testing it."
            : $"Start {presetName} locally and show the launch result.");
        Grid.SetColumn(test, 4);
        row.Children.Add(test);
        return row;
    }

    private Border CreateCustomActionRow(AppLaunchAction action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceXs);
        details.Children.Add(new TextBlock
        {
            Text = action.Label,
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush")
        });
        details.Children.Add(new TextBlock
        {
            Text = action.ExecutablePath,
            FontSize = 11,
            Foreground = visuals.Brush("MutedTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = action.ExecutablePath
        });
        grid.Children.Add(details);

        var actions = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var test = visuals.CreateButton("Test", (_, _) => TestAction(action));
        AutomationProperties.SetName(test, $"Test {action.Label} launch");
        AutomationProperties.SetHelpText(test, $"Start {action.Label} locally and show the launch result.");
        actions.Children.Add(test);
        actions.Children.Add(visuals.CreateButton("Edit", (_, _) => OpenEditor(action)));
        actions.Children.Add(visuals.CreateButton("Remove", (_, _) => Remove(action), danger: true));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border
        {
            Background = visuals.Brush("SurfaceBrush"),
            BorderBrush = visuals.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = grid
        };
    }

    private void SetPreset(AppLaunchKind kind, bool enabled, SettingsCheckBox checkBox)
    {
        if (isLoading() || _synchronizing)
        {
            return;
        }

        if (AppLaunchSettings.SetPresetEnabled(kind, enabled, out var error))
        {
            refresh();
            return;
        }

        _synchronizing = true;
        checkBox.IsChecked = !enabled;
        _synchronizing = false;
        toasts.Show(error);
    }

    private void SavePresetLabel(AppLaunchKind kind, TextBox labelInput)
    {
        if (isLoading() || !labelInput.IsEnabled)
        {
            return;
        }

        if (!AppLaunchSettings.TrySetPresetLabel(kind, labelInput.Text, out var error))
        {
            labelInput.BorderBrush = visuals.Brush("DangerBrush");
            labelInput.ToolTip = error;
            AutomationProperties.SetHelpText(labelInput, error);
            return;
        }

        labelInput.BorderBrush = visuals.Brush("BorderBrush");
        labelInput.ToolTip = null;
        AutomationProperties.SetHelpText(labelInput, "Changes are saved automatically.");
    }

    private void OpenEditor(AppLaunchAction? existing = null)
    {
        if (!AppLaunchEditorDialog.ShowAndSave(owner, existing))
        {
            return;
        }

        toasts.Show(existing is null ? "Application button added" : "Application button updated");
        refresh();
    }

    private void Remove(AppLaunchAction action)
    {
        if (!ThemedConfirmationDialog.Show(
                owner,
                "Remove application button?",
                $"Paired devices will no longer be able to start {action.Label} from this button.",
                "Remove",
                "Cancel",
                ConfirmationTone.Warning))
        {
            return;
        }

        AppLaunchSettings.RemoveCustom(action.Id);
        toasts.Show("Application button removed");
        refresh();
    }

    private void TestAction(AppLaunchAction action)
    {
        var result = appLaunchService.Execute(action.Id);
        toasts.Show(result.Message);
    }

    private static Grid CreatePresetGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceSm) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceSm) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }
}
