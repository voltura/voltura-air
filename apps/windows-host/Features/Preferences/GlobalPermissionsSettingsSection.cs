using System.Windows.Controls;
using VolturaAir.Host.Ui;
using CheckBox = System.Windows.Controls.CheckBox;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class GlobalPermissionsSettingsSection(
    ISystemPowerController powerController,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        var permissions = AppPermissionSettings.Load();
        var allowClientControl = visuals.CreateCheckBox("Allow paired devices to control Voltura Air host", AppClientControlSettings.IsEnabled());
        allowClientControl.Checked += (_, _) => AppClientControlSettings.SetEnabled(true);
        allowClientControl.Unchecked += (_, _) => AppClientControlSettings.SetEnabled(false);
        parent.Children.Add(allowClientControl);
        parent.Children.Add(visuals.CreateMutedText("When off, paired devices cannot inject input into Voltura Air itself. They can still control Windows and other permitted apps."));

        var controls = new[]
        {
            (Control: visuals.CreateCheckBox("Allow paired devices to request PC sleep", permissions.AllowPcSleep), Key: "sleep"),
            (Control: visuals.CreateCheckBox("Allow paired devices to control volume", permissions.AllowVolumeControl), Key: "volume"),
            (Control: visuals.CreateCheckBox("Allow paired devices to control presentations", permissions.AllowPresentationControl), Key: "presentation"),
            (Control: visuals.CreateCheckBox("Allow paired devices to start applications", permissions.AllowRemoteAppLaunch), Key: "launch"),
            (Control: visuals.CreateCheckBox("Allow paired devices to open web addresses", permissions.AllowUrlOpen), Key: "url"),
            (Control: visuals.CreateCheckBox("Allow paired devices to lock the PC", permissions.AllowPcLock), Key: "lock"),
            (Control: visuals.CreateCheckBox("Allow paired devices to blackout displays", permissions.AllowBlackoutDisplay), Key: "blackout"),
            (Control: visuals.CreateCheckBox("Allow paired devices to turn off displays", permissions.AllowDisplayOff), Key: "display-off"),
            (Control: visuals.CreateCheckBox("Allow paired devices to start the screen saver", permissions.AllowScreenSaver), Key: "screen-saver"),
            (Control: visuals.CreateCheckBox("Allow paired devices to control Keep awake", permissions.AllowAwakeControl), Key: "awake"),
            (Control: visuals.CreateCheckBox("Allow paired devices to read the PC clipboard", permissions.AllowClipboardRead), Key: "clipboard"),
            (Control: visuals.CreateCheckBox("Allow paired devices to sign out", permissions.AllowSignOut), Key: "sign-out"),
            (Control: visuals.CreateCheckBox("Allow paired devices to restart the PC", permissions.AllowRestart), Key: "restart"),
            (Control: visuals.CreateCheckBox("Allow paired devices to shut down the PC", permissions.AllowShutdown), Key: "shutdown")
        };
        void SavePermissions() => Save(controls);
        foreach (var (control, key) in controls)
        {
            control.Checked += (_, _) => SavePermissions();
            control.Unchecked += (_, _) => SavePermissions();
            if (key == "presentation" && !AppDeveloperSettings.EnableAlphaFeatures() ||
                key == "screen-saver" && !powerController.IsActionAvailable(SystemPowerActions.ScreenSaver))
            {
                continue;
            }
            parent.Children.Add(control);
        }
        parent.Children.Add(visuals.CreateMutedText("Display off and session-ending actions require hold-to-confirm on the mobile device."));
        var details = preferenceVisuals.AddNestedSection(parent, "More about global permissions");
        details.Children.Add(visuals.CreateMutedText("Lock and blackout are enabled by default. The screen-saver permission appears when Windows has a screen saver configured. Opening web addresses, reading the PC clipboard, display off, sign out, restart, and shut down require explicit host approval."));
    }

    private void Save((CheckBox Control, string Key)[] controls)
    {
        if (isLoading())
        {
            return;
        }

        bool IsAllowed(string key) => controls.First(item => item.Key == key).Control.IsChecked == true;
        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: IsAllowed("sleep"),
            AllowVolumeControl: IsAllowed("volume"),
            AllowPresentationControl: IsAllowed("presentation"),
            AllowRemoteAppLaunch: IsAllowed("launch"),
            AllowUrlOpen: IsAllowed("url"),
            AllowPcLock: IsAllowed("lock"),
            AllowBlackoutDisplay: IsAllowed("blackout"),
            AllowDisplayOff: IsAllowed("display-off"),
            AllowScreenSaver: IsAllowed("screen-saver"),
            AllowAwakeControl: IsAllowed("awake"),
            AllowClipboardRead: IsAllowed("clipboard"),
            AllowSignOut: IsAllowed("sign-out"),
            AllowRestart: IsAllowed("restart"),
            AllowShutdown: IsAllowed("shutdown")));
    }
}
