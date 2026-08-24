using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using ComboBox = System.Windows.Controls.ComboBox;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class GlobalPermissionsSettingsSection(
    Window owner,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        var defaultLabel = new TextBlock
        {
            Text = "Default access for newly paired devices",
            FontWeight = FontWeights.SemiBold,
            Foreground = visuals.Brush("TextBrush")
        };
        var defaultAccess = new ComboBox
        {
            Width = 240,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        defaultAccess.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        AutomationProperties.SetName(defaultAccess, "Default access for newly paired devices");
        AddProfile(defaultAccess, DeviceAccessProfile.MyDevice);
        AddProfile(defaultAccess, DeviceAccessProfile.RemoteControls);
        defaultAccess.SelectedItem = defaultAccess.Items.OfType<ComboBoxItem>()
            .First(item => Equals(item.Tag, AppPermissionSettings.LoadDefaultAccessProfile()));
        defaultAccess.SelectionChanged += (_, _) =>
        {
            if (!isLoading() && defaultAccess.SelectedItem is ComboBoxItem { Tag: DeviceAccessProfile selected })
            {
                AppPermissionSettings.SaveDefaultAccessProfile(selected);
            }
        };
        parent.Children.Add(defaultLabel);
        parent.Children.Add(defaultAccess);
        parent.Children.Add(visuals.CreateMutedText(
            "This choice applies only when a new device is paired. Existing devices keep their access."));
        preferenceVisuals.RegisterLabel(defaultLabel, defaultAccess, "device access profile default pairing");

        var toggles = preferenceVisuals.AddToggleGroup(parent);
        var allowClientControl = preferenceVisuals.Register(visuals.CreateCheckBox(
            "Allow trusted devices to control the Voltura Air application",
            AppClientControlSettings.IsEnabled(),
            showInformation: () => ThemedConfirmationDialog.ShowInformation(
                owner,
                "Control of the Voltura Air application",
                "When this setting is on, My device and Custom devices may inject input into Voltura Air itself. Remote controls devices are always blocked from the application. All profiles can still control other permitted Windows applications.")));
        allowClientControl.Checked += (_, _) => AppClientControlSettings.SetEnabled(true);
        allowClientControl.Unchecked += (_, _) => AppClientControlSettings.SetEnabled(false);
        toggles.Children.Add(allowClientControl);

        var hideProtected = preferenceVisuals.Register(visuals.CreateCheckBox(
            "Hide protected operating system files and folders (recommended)",
            AppPermissionSettings.Load().HideProtectedFileSystemItems));
        hideProtected.Checked += (_, _) => SaveProtectedFileSetting(true);
        hideProtected.Unchecked += (_, _) => SaveProtectedFileSetting(false);
        toggles.Children.Add(hideProtected);

        var details = preferenceVisuals.AddNestedSection(parent, "More about device access");
        details.Children.Add(visuals.CreateMutedText(
            "My device allows every product permission. Remote controls allows pointer and keyboard, volume, presentations, application launch, PC lock, display blackout, and the screen saver. Customize access for a specific device from Devices."));
        details.Children.Add(visuals.CreateMutedText(
            "Control of the Voltura Air window or tray stays separate and is disabled by default. Protected-file filtering is also separate from access profiles."));
    }

    private void SaveProtectedFileSetting(bool hideProtected)
    {
        if (!isLoading())
        {
            AppPermissionSettings.Save(AppPermissionSettings.Load() with
            {
                HideProtectedFileSystemItems = hideProtected
            });
        }
    }

    private static void AddProfile(ComboBox comboBox, DeviceAccessProfile profile) =>
        comboBox.Items.Add(new ComboBoxItem
        {
            Content = DeviceAccessProfiles.GetDisplayName(profile),
            Tag = profile
        });
}
