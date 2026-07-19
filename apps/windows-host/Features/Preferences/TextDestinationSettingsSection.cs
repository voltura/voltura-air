using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class TextDestinationSettingsSection(
    Window owner,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    HostToastPresenter toasts,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        var settings = AppTextDestinationSettings.Load();
        parent.Children.Add(visuals.CreateMutedText("Choose where sent text goes. App paths and window details stay on this PC."));
        parent.Children.Add(visuals.CreateLabel("Default destination"));
        var mode = CreateComboBox(280);
        mode.Items.Add(new ComboBoxItem { Content = "Currently focused application", Tag = TextDestinationMode.Focused });
        mode.Items.Add(new ComboBoxItem { Content = "Windows clipboard", Tag = TextDestinationMode.Clipboard });
        mode.Items.Add(new ComboBoxItem { Content = "Managed application", Tag = TextDestinationMode.Managed });
        mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, settings.Mode));
        parent.Children.Add(mode);

        var destinationSummary = visuals.CreateMutedText(string.Empty);
        parent.Children.Add(destinationSummary);
        var managedSettings = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceMd);
        managedSettings.Visibility = settings.Mode == TextDestinationMode.Managed ? Visibility.Visible : Visibility.Collapsed;
        parent.Children.Add(managedSettings);
        managedSettings.Children.Add(visuals.CreateLabel("Managed application"));
        var preset = CreateComboBox(280);
        foreach (var value in Enum.GetValues<TextDestinationPreset>())
        {
            preset.Items.Add(new ComboBoxItem { Content = GetPresetName(value), Tag = value });
        }
        preset.SelectedItem = preset.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, settings.Preset));
        managedSettings.Children.Add(preset);

        var overrides = settings.ExecutableOverrides is null
            ? new Dictionary<TextDestinationPreset, string>()
            : new(settings.ExecutableOverrides);
        var overrideSettings = HostVisualFactory.CreateVerticalStack(UiTokens.SpaceSm);
        overrideSettings.Children.Add(visuals.CreateLabel("Approved executable override"));
        var overridePath = new TextBox
        {
            Text = AppTextDestinationSettings.GetExecutableOverride(settings, settings.Preset) ?? string.Empty,
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            IsReadOnly = true
        };
        overrideSettings.Children.Add(overridePath);
        var row = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var browse = visuals.CreateButton("Choose approved .exe", (_, _) => ChooseExecutable(mode, preset, overridePath, overrides));
        var clear = visuals.CreateButton("Clear override", (_, _) => ClearExecutable(mode, preset, overridePath, overrides));
        row.Children.Add(browse);
        row.Children.Add(clear);
        overrideSettings.Children.Add(row);
        managedSettings.Children.Add(overrideSettings);

        var openDefaultApps = visuals.CreateButton("Open Windows default apps", (_, _) =>
        {
            var instruction = preset.SelectedItem is ComboBoxItem { Tag: TextDestinationPreset.DefaultTextFile }
                ? "In Settings, choose 'Choose defaults by file type', search .txt, then select the text-file app."
                : "In Settings, choose 'Choose defaults by link type', search MAILTO, then select the email app.";
            toasts.Show(DefaultMailCompose.TryOpenDefaultAppsSettings()
                ? instruction
                : "Windows could not open Default apps settings.");
        });
        openDefaultApps.HorizontalAlignment = HorizontalAlignment.Left;
        openDefaultApps.Visibility = Visibility.Collapsed;
        managedSettings.Children.Add(openDefaultApps);

        var keepDraftFiles = visuals.CreateCheckBox("Keep generated draft files", !AppTextDestinationDraftSettings.AutomaticallyRemoveDraftFiles());
        keepDraftFiles.Checked += (_, _) =>
        {
            AppTextDestinationDraftSettings.SetAutomaticallyRemoveDraftFiles(false);
            toasts.Show("Generated drafts will be kept until you remove them.");
        };
        keepDraftFiles.Unchecked += (_, _) =>
        {
            AppTextDestinationDraftSettings.SetAutomaticallyRemoveDraftFiles(true);
            toasts.Show("Generated drafts will be removed after 24 hours.");
        };
        var draftRetention = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceMd);
        draftRetention.Children.Add(keepDraftFiles);
        var openDraftFolder = visuals.CreateButton("Open generated drafts folder", (_, _) =>
        {
            if (!TextDestinationDraftStore.TryOpenFolder())
            {
                toasts.Show("Windows could not open the generated drafts folder.");
            }
        });
        openDraftFolder.VerticalAlignment = VerticalAlignment.Center;
        draftRetention.Children.Add(openDraftFolder);
        managedSettings.Children.Add(draftRetention);
        var draftRetentionNotice = visuals.CreateMutedText("Generated drafts contain the sent text. They are removed after 24 hours unless you keep them.");
        managedSettings.Children.Add(draftRetentionNotice);

        var details = preferenceVisuals.AddNestedSection(managedSettings, "More about text destinations");
        details.Children.Add(visuals.CreateMutedText("Managed destinations create a new item when possible. Before pasting, Voltura Air confirms that the intended, non-elevated window is in the foreground. If it cannot confirm that, the text stays on the Windows clipboard for manual paste."));
        details.Children.Add(visuals.CreateMutedText("Approved executable paths apply only to the selected app and never leave this PC. Word, Excel, Notepad++, and New text file drafts are stored in %LOCALAPPDATA%\\Voltura Air\\Text destination drafts. Default text-file and email destinations use Windows' .txt and MAILTO associations."));

        void UpdateManagedDestinationUi()
        {
            var selectedMode = mode.SelectedItem is ComboBoxItem { Tag: TextDestinationMode selected }
                ? selected
                : TextDestinationMode.Focused;
            var selectedPreset = preset.SelectedItem is ComboBoxItem { Tag: TextDestinationPreset selectedValue }
                ? selectedValue
                : settings.Preset;
            destinationSummary.Text = GetSummary(selectedMode, selectedPreset);
            managedSettings.Visibility = selectedMode == TextDestinationMode.Managed ? Visibility.Visible : Visibility.Collapsed;
            if (selectedMode != TextDestinationMode.Managed)
            {
                return;
            }

            var supportsOverride = AppTextDestinationSettings.SupportsExecutableOverride(selectedPreset);
            overrideSettings.Visibility = supportsOverride ? Visibility.Visible : Visibility.Collapsed;
            overridePath.IsEnabled = supportsOverride;
            browse.IsEnabled = supportsOverride;
            clear.IsEnabled = supportsOverride;
            openDefaultApps.Visibility = UsesWindowsDefaultAssociation(selectedPreset) ? Visibility.Visible : Visibility.Collapsed;
            var usesGeneratedDrafts = UsesGeneratedDrafts(selectedPreset);
            draftRetention.Visibility = usesGeneratedDrafts ? Visibility.Visible : Visibility.Collapsed;
            draftRetentionNotice.Visibility = usesGeneratedDrafts ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateManagedDestinationUi();
        mode.SelectionChanged += (_, _) =>
        {
            UpdateManagedDestinationUi();
            if (!isLoading())
            {
                Save(mode, preset, overrides);
            }
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is ComboBoxItem { Tag: TextDestinationPreset selectedPreset })
            {
                overridePath.Text = overrides.GetValueOrDefault(selectedPreset, string.Empty);
                UpdateManagedDestinationUi();
            }
            if (!isLoading())
            {
                Save(mode, preset, overrides);
            }
        };
    }

    private void ChooseExecutable(ComboBox mode, ComboBox preset, TextBox executableOverride, Dictionary<TextDestinationPreset, string> overrides)
    {
        if (preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset })
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(owner) != true || !ThemedConfirmationDialog.Show(
                owner,
                "Approve executable",
                "Voltura Air will use this local executable only for the selected text destination. It is never sent to paired devices.",
                "Approve",
                "Cancel",
                ConfirmationTone.Question))
        {
            return;
        }

        overrides[selectedPreset] = dialog.FileName;
        executableOverride.Text = dialog.FileName;
        Save(mode, preset, overrides);
    }

    private void ClearExecutable(ComboBox mode, ComboBox preset, TextBox executableOverride, Dictionary<TextDestinationPreset, string> overrides)
    {
        if (preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset })
        {
            return;
        }

        overrides.Remove(selectedPreset);
        executableOverride.Clear();
        Save(mode, preset, overrides);
    }

    private void Save(ComboBox mode, ComboBox preset, Dictionary<TextDestinationPreset, string> overrides)
    {
        if (mode.SelectedItem is not ComboBoxItem { Tag: TextDestinationMode selectedMode } ||
            preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset })
        {
            return;
        }

        if (!AppTextDestinationSettings.TrySave(selectedMode, selectedPreset, overrides.Count == 0 ? null : overrides, out var error))
        {
            toasts.Show(error);
            return;
        }

        toasts.Show("Text destination saved");
    }

    private static ComboBox CreateComboBox(double width)
    {
        var comboBox = new ComboBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        comboBox.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        return comboBox;
    }

    private static string GetPresetName(TextDestinationPreset preset) => preset switch
    {
        TextDestinationPreset.Notepad => "Windows 11 Notepad",
        TextDestinationPreset.NotepadPlusPlus => "Notepad++",
        TextDestinationPreset.Word => "Microsoft Word",
        TextDestinationPreset.VisualStudioCode => "Visual Studio Code",
        TextDestinationPreset.Excel => "Microsoft Excel",
        TextDestinationPreset.DefaultTextFile => "New text file (default app)",
        TextDestinationPreset.DefaultMail => "Default email client",
        TextDestinationPreset.Outlook => "Outlook compose (classic)",
        TextDestinationPreset.Custom => "Custom executable",
        _ => preset.ToString()
    };

    private static bool UsesWindowsDefaultAssociation(TextDestinationPreset preset) =>
        preset is TextDestinationPreset.DefaultTextFile or TextDestinationPreset.DefaultMail;

    private static bool UsesGeneratedDrafts(TextDestinationPreset preset) =>
        preset is TextDestinationPreset.NotepadPlusPlus or TextDestinationPreset.Word or TextDestinationPreset.Excel or TextDestinationPreset.DefaultTextFile;

    private static string GetSummary(TextDestinationMode mode, TextDestinationPreset preset) => mode switch
    {
        TextDestinationMode.Focused => "Types into the application currently focused on this PC.",
        TextDestinationMode.Clipboard => "Copies text to the Windows clipboard for manual paste.",
        TextDestinationMode.Managed => $"Creates a new item in {GetPresetName(preset)} when possible. If safe delivery cannot be confirmed, text remains on the clipboard.",
        _ => string.Empty
    };
}
