using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddTextDestinationSettings(StackPanel parent)
    {
        var settings = AppTextDestinationSettings.Load();
        parent.Children.Add(CreateMutedText("Choose where sent text goes. App paths and window details stay on this PC."));
        parent.Children.Add(CreateLabel("Default destination"));
        var mode = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 10) };
        mode.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        mode.Items.Add(new ComboBoxItem { Content = "Currently focused application", Tag = TextDestinationMode.Focused });
        mode.Items.Add(new ComboBoxItem { Content = "Windows clipboard", Tag = TextDestinationMode.Clipboard });
        mode.Items.Add(new ComboBoxItem { Content = "Managed application", Tag = TextDestinationMode.Managed });
        mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, settings.Mode));
        parent.Children.Add(mode);

        var destinationSummary = CreateMutedText(string.Empty);
        parent.Children.Add(destinationSummary);
        var managedSettings = new StackPanel { Visibility = settings.Mode == TextDestinationMode.Managed ? Visibility.Visible : Visibility.Collapsed };
        parent.Children.Add(managedSettings);
        managedSettings.Children.Add(CreateLabel("Managed application"));
        var preset = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 10) };
        preset.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var value in Enum.GetValues<TextDestinationPreset>()) preset.Items.Add(new ComboBoxItem { Content = GetTextDestinationPresetName(value), Tag = value });
        preset.SelectedItem = preset.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, settings.Preset));
        managedSettings.Children.Add(preset);

        var overrides = settings.ExecutableOverrides is null ? new Dictionary<TextDestinationPreset, string>() : new(settings.ExecutableOverrides);
        var overrideSettings = new StackPanel();
        var overrideLabel = CreateLabel("Approved executable override");
        overrideLabel.Margin = new Thickness(0, 16, 0, 8);
        overrideSettings.Children.Add(overrideLabel);
        var overridePath = new TextBox
        {
            Text = AppTextDestinationSettings.GetExecutableOverride(settings, settings.Preset) ?? string.Empty,
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            IsReadOnly = true
        };
        overrideSettings.Children.Add(overridePath);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var browse = CreateButton("Choose approved .exe", (_, _) => ChooseTextDestinationExecutable(mode, preset, overridePath, overrides));
        var clear = CreateButton("Clear override", (_, _) => ClearTextDestinationExecutable(mode, preset, overridePath, overrides));
        row.Children.Add(browse);
        row.Children.Add(clear);
        overrideSettings.Children.Add(row);
        managedSettings.Children.Add(overrideSettings);
        var openDefaultMailApps = CreateButton("Open Windows default apps", (_, _) =>
        {
            var instruction = preset.SelectedItem is ComboBoxItem { Tag: TextDestinationPreset.DefaultTextFile }
                ? "In Settings, choose 'Choose defaults by file type', search .txt, then select the text-file app."
                : "In Settings, choose 'Choose defaults by link type', search MAILTO, then select the email app.";
            if (DefaultMailCompose.TryOpenDefaultAppsSettings()) ShowToast(instruction);
            else ShowToast("Windows could not open Default apps settings.");
        });
        openDefaultMailApps.HorizontalAlignment = HorizontalAlignment.Left;
        openDefaultMailApps.Margin = new Thickness(0, 0, 0, 12);
        openDefaultMailApps.Visibility = Visibility.Collapsed;
        managedSettings.Children.Add(openDefaultMailApps);
        var keepDraftFiles = CreateCheckBox("Keep generated draft files", !AppTextDestinationDraftSettings.AutomaticallyRemoveDraftFiles());
        keepDraftFiles.Checked += (_, _) =>
        {
            AppTextDestinationDraftSettings.SetAutomaticallyRemoveDraftFiles(false);
            ShowToast("Generated drafts will be kept until you remove them.");
        };
        keepDraftFiles.Unchecked += (_, _) =>
        {
            AppTextDestinationDraftSettings.SetAutomaticallyRemoveDraftFiles(true);
            ShowToast("Generated drafts will be removed after 24 hours.");
        };
        keepDraftFiles.Margin = new Thickness(0);
        var draftRetention = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        draftRetention.Children.Add(keepDraftFiles);
        var openDraftFolder = CreateButton("Open generated drafts folder", (_, _) =>
        {
            if (!TextDestinationDraftStore.TryOpenFolder()) ShowToast("Windows could not open the generated drafts folder.");
        });
        openDraftFolder.Margin = new Thickness(12, 0, 0, 0);
        openDraftFolder.VerticalAlignment = VerticalAlignment.Center;
        draftRetention.Children.Add(openDraftFolder);
        managedSettings.Children.Add(draftRetention);
        var draftRetentionNotice = CreateMutedText("Generated drafts contain the sent text. They are removed after 24 hours unless you keep them.");
        managedSettings.Children.Add(draftRetentionNotice);

        var details = new StackPanel { Visibility = Visibility.Collapsed };
        details.Children.Add(CreateMutedText("Managed destinations create a new item when possible. Before pasting, Voltura Air confirms that the intended, non-elevated window is in the foreground. If it cannot confirm that, the text stays on the Windows clipboard for manual paste."));
        details.Children.Add(CreateMutedText("Approved executable paths apply only to the selected app and never leave this PC. Word, Excel, Notepad++, and New text file drafts are stored in %LOCALAPPDATA%\\Voltura Air\\Text destination drafts. Default text-file and email destinations use Windows' .txt and MAILTO associations."));
        var moreDetails = CreateButton("More about text destinations", (_, _) => { });
        moreDetails.Click += (_, _) =>
        {
            var showDetails = details.Visibility != Visibility.Visible;
            details.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
            moreDetails.Content = showDetails ? "Hide text destination details" : "More about text destinations";
        };
        moreDetails.HorizontalAlignment = HorizontalAlignment.Left;
        managedSettings.Children.Add(moreDetails);
        managedSettings.Children.Add(details);

        void UpdateManagedDestinationUi()
        {
            var selectedMode = mode.SelectedItem is ComboBoxItem { Tag: TextDestinationMode selected } ? selected : TextDestinationMode.Focused;
            destinationSummary.Text = GetTextDestinationSummary(selectedMode, preset.SelectedItem is ComboBoxItem { Tag: TextDestinationPreset summaryPreset } ? summaryPreset : settings.Preset);
            managedSettings.Visibility = selectedMode == TextDestinationMode.Managed ? Visibility.Visible : Visibility.Collapsed;
            if (selectedMode != TextDestinationMode.Managed || preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset }) return;

            var supportsOverride = AppTextDestinationSettings.SupportsExecutableOverride(selectedPreset);
            overrideSettings.Visibility = supportsOverride ? Visibility.Visible : Visibility.Collapsed;
            overridePath.IsEnabled = supportsOverride;
            browse.IsEnabled = supportsOverride;
            clear.IsEnabled = supportsOverride;
            openDefaultMailApps.Visibility = UsesWindowsDefaultAssociation(selectedPreset) ? Visibility.Visible : Visibility.Collapsed;
            var usesGeneratedDrafts = UsesGeneratedDrafts(selectedPreset);
            draftRetention.Visibility = usesGeneratedDrafts ? Visibility.Visible : Visibility.Collapsed;
            draftRetentionNotice.Visibility = usesGeneratedDrafts ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateManagedDestinationUi();
        mode.SelectionChanged += (_, _) =>
        {
            UpdateManagedDestinationUi();
            if (!_isLoadingPreferences) SaveTextDestination(mode, preset, overrides);
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is ComboBoxItem { Tag: TextDestinationPreset selectedPreset })
            {
                overridePath.Text = overrides.GetValueOrDefault(selectedPreset, string.Empty);
                UpdateManagedDestinationUi();
            }
            if (!_isLoadingPreferences) SaveTextDestination(mode, preset, overrides);
        };
    }

    private void ChooseTextDestinationExecutable(ComboBox mode, ComboBox preset, TextBox executableOverride, Dictionary<TextDestinationPreset, string> overrides)
    {
        if (preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset }) return;
        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !ThemedConfirmationDialog.Show(this, "Approve executable", "Voltura Air will use this local executable only for the selected text destination. It is never sent to paired devices.", "Approve", "Cancel", ConfirmationTone.Question)) return;
        overrides[selectedPreset] = dialog.FileName;
        executableOverride.Text = dialog.FileName;
        SaveTextDestination(mode, preset, overrides);
    }

    private void ClearTextDestinationExecutable(ComboBox mode, ComboBox preset, TextBox executableOverride, Dictionary<TextDestinationPreset, string> overrides)
    {
        if (preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset }) return;
        overrides.Remove(selectedPreset);
        executableOverride.Clear();
        SaveTextDestination(mode, preset, overrides);
    }

    private void SaveTextDestination(ComboBox mode, ComboBox preset, Dictionary<TextDestinationPreset, string> overrides)
    {
        if (mode.SelectedItem is not ComboBoxItem { Tag: TextDestinationMode selectedMode } || preset.SelectedItem is not ComboBoxItem { Tag: TextDestinationPreset selectedPreset }) return;
        if (!AppTextDestinationSettings.TrySave(selectedMode, selectedPreset, overrides.Count == 0 ? null : overrides, out var error)) { ShowToast(error); return; }
        ShowToast("Text destination saved");
    }

    private static string GetTextDestinationPresetName(TextDestinationPreset preset) => preset switch
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

    private static bool UsesWindowsDefaultAssociation(TextDestinationPreset preset) => preset is TextDestinationPreset.DefaultTextFile or TextDestinationPreset.DefaultMail;
    private static bool UsesGeneratedDrafts(TextDestinationPreset preset) => preset is TextDestinationPreset.NotepadPlusPlus or TextDestinationPreset.Word or TextDestinationPreset.Excel or TextDestinationPreset.DefaultTextFile;

    private static string GetTextDestinationSummary(TextDestinationMode mode, TextDestinationPreset preset) => mode switch
    {
        TextDestinationMode.Focused => "Types into the application currently focused on this PC.",
        TextDestinationMode.Clipboard => "Copies text to the Windows clipboard for manual paste.",
        TextDestinationMode.Managed => $"Creates a new item in {GetTextDestinationPresetName(preset)} when possible. If safe delivery cannot be confirmed, text remains on the clipboard.",
        _ => string.Empty
    };
}
