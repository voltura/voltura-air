using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddAppLaunchSettings(StackPanel parent)
    {
        var configured = AppLaunchSettings.GetActions();
        parent.Children.Add(CreateMutedText("Choose the app-launch buttons available from Remote mode."));
        parent.Children.Add(CreateAppLaunchPresetHeader());

        foreach (var preset in AppLaunchSettings.GetPresets())
        {
            parent.Children.Add(CreateAppLaunchPresetRow(
                preset,
                configured.FirstOrDefault(action => action.Id == preset.Id)));
        }

        var appLaunchDetailsPanel = AddNestedPreferencesSection(parent, "More about app-launch buttons");
        appLaunchDetailsPanel.Children.Add(CreateMutedText($"The global and per-device application-launch permissions still apply. Button labels are 1–{AppLaunchSettings.MaxLabelLength} characters and save automatically. Browser opens the default browser; Spotify, VLC, and PowerPoint must be installed and registered with Windows."));
        var customButtonsLabel = CreateLabel("Custom buttons");
        parent.Children.Add(customButtonsLabel);
        var customActions = configured.Where(action => action.Kind == AppLaunchKind.Custom).ToArray();
        if (customActions.Length == 0)
        {
            parent.Children.Add(CreateMutedText("No custom application commands are approved."));
        }
        else
        {
            foreach (var action in customActions)
            {
                parent.Children.Add(CreateAppLaunchRow(action));
            }
        }

        var add = CreateButton("Add custom button", (_, _) => OpenAppLaunchEditor(), primary: true);
        add.IsEnabled = configured.Count < AppLaunchSettings.MaxActions;
        add.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        parent.Children.Add(add);
        parent.Children.Add(CreateMutedText("Custom buttons use a locally approved .exe and optional arguments; paths never leave this PC."));
    }

    private Grid CreateAppLaunchPresetHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceSm) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var presets = new TextBlock
        {
            Text = "Presets",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"]
        };
        header.Children.Add(presets);

        var label = new TextBlock
        {
            Text = "Button label",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"]
        };
        Grid.SetColumn(label, 2);
        header.Children.Add(label);
        return header;
    }

    private Grid CreateAppLaunchPresetRow(AppLaunchAction preset, AppLaunchAction? configured)
    {
        var presetName = AppLaunchSettings.GetPresetName(preset.Kind);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceSm) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var checkBox = CreateCheckBox($"Show {presetName}", configured is not null);
        checkBox.VerticalAlignment = VerticalAlignment.Center;
        checkBox.Checked += (_, _) => SetAppLaunchPreset(preset.Kind, true, checkBox);
        checkBox.Unchecked += (_, _) => SetAppLaunchPreset(preset.Kind, false, checkBox);
        row.Children.Add(checkBox);

        var labelInput = new TextBox
        {
            Text = configured?.Label ?? preset.Label,
            MaxLength = AppLaunchSettings.MaxLabelLength,
            Height = 32,
            Width = 140,
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = configured is not null,
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            Foreground = (Brush)Resources["TextBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(labelInput, $"{presetName} button label");
        AutomationProperties.SetHelpText(labelInput, "Changes are saved automatically.");
        Grid.SetColumn(labelInput, 2);
        row.Children.Add(labelInput);
        labelInput.TextChanged += (_, _) => SaveAppLaunchPresetLabel(preset.Kind, labelInput);
        return row;
    }

    private Border CreateAppLaunchRow(AppLaunchAction action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UiTokens.SpaceMd) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = CreateVerticalStack(UiTokens.SpaceXs);
        details.Children.Add(new TextBlock
        {
            Text = action.Label,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["TextBrush"]
        });
        details.Children.Add(new TextBlock
        {
            Text = action.ExecutablePath,
            FontSize = 11,
            Foreground = (Brush)Resources["MutedTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = action.ExecutablePath
        });
        grid.Children.Add(details);

        var actions = CreateHorizontalStack(UiTokens.SpaceSm);
        actions.Children.Add(CreateButton("Edit", (_, _) => OpenAppLaunchEditor(action)));
        actions.Children.Add(CreateButton("Remove", (_, _) => RemoveAppLaunchAction(action), danger: true));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border
        {
            Background = (Brush)Resources["SurfaceBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = grid
        };
    }

    private void SetAppLaunchPreset(AppLaunchKind kind, bool enabled, CheckBox checkBox)
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        if (AppLaunchSettings.SetPresetEnabled(kind, enabled, out var error))
        {
            ReopenAppLaunchPreferences();
            return;
        }

        _isLoadingPreferences = true;
        checkBox.IsChecked = !enabled;
        _isLoadingPreferences = false;
        ShowToast(error);
    }

    private void SaveAppLaunchPresetLabel(AppLaunchKind kind, TextBox labelInput)
    {
        if (_isLoadingPreferences || !labelInput.IsEnabled)
        {
            return;
        }

        if (!AppLaunchSettings.TrySetPresetLabel(kind, labelInput.Text, out var error))
        {
            labelInput.BorderBrush = (Brush)Resources["DangerBrush"];
            labelInput.ToolTip = error;
            AutomationProperties.SetHelpText(labelInput, error);
            return;
        }

        labelInput.BorderBrush = (Brush)Resources["BorderBrush"];
        labelInput.ToolTip = null;
        AutomationProperties.SetHelpText(labelInput, "Changes are saved automatically.");
    }

    private void OpenAppLaunchEditor(AppLaunchAction? existing = null)
    {
        if (!AppLaunchEditorDialog.ShowAndSave(this, existing))
        {
            return;
        }

        ShowToast(existing is null ? "Application button added" : "Application button updated");
        ReopenAppLaunchPreferences();
    }

    private void RemoveAppLaunchAction(AppLaunchAction action)
    {
        if (!ThemedConfirmationDialog.Show(
            this,
            "Remove application button?",
            $"Paired devices will no longer be able to start {action.Label} from this button.",
            "Remove",
            "Cancel",
            ConfirmationTone.Warning))
        {
            return;
        }

        AppLaunchSettings.RemoveCustom(action.Id);
        ShowToast("Application button removed");
        ReopenAppLaunchPreferences();
    }

    private void ReopenAppLaunchPreferences()
    {
        RefreshPreferencesPage();
    }
}
