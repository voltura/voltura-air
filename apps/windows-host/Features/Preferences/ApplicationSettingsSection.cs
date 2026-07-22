using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class ApplicationSettingsSection(
    IAppLog appLog,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        var toggles = preferenceVisuals.AddToggleGroup(parent);
        var start = visuals.CreateCheckBox("Start Voltura Air when I sign in to Windows", AppStartupSettings.IsEnabled());
        start.Checked += (_, _) => AppStartupSettings.SetEnabled(true);
        start.Unchecked += (_, _) => AppStartupSettings.SetEnabled(false);
        toggles.Children.Add(start);

        var startHidden = visuals.CreateCheckBox("Start Voltura Air hidden in the tray", AppWindowSettings.StartHiddenInTray());
        startHidden.Checked += (_, _) => AppWindowSettings.SetStartHiddenInTray(true);
        startHidden.Unchecked += (_, _) => AppWindowSettings.SetStartHiddenInTray(false);
        toggles.Children.Add(startHidden);

        var notify = visuals.CreateCheckBox("Show connection status notifications", AppNotificationSettings.ShowConnectionStatusNotifications());
        notify.Checked += (_, _) => AppNotificationSettings.SetShowConnectionStatusNotifications(true);
        notify.Unchecked += (_, _) => AppNotificationSettings.SetShowConnectionStatusNotifications(false);
        toggles.Children.Add(notify);

        var showOnDisconnect = visuals.CreateCheckBox("Show Voltura Air when the last device disconnects", AppNotificationSettings.ShowPairingWindowOnDisconnect());
        showOnDisconnect.Checked += (_, _) => AppNotificationSettings.SetShowPairingWindowOnDisconnect(true);
        showOnDisconnect.Unchecked += (_, _) => AppNotificationSettings.SetShowPairingWindowOnDisconnect(false);
        toggles.Children.Add(showOnDisconnect);
        AddLoggingSettings(parent);
    }

    private void AddLoggingSettings(StackPanel parent)
    {
        var applicationLogging = visuals.CreateCheckBox("Write application log", AppLoggingSettings.IsEnabled());
        applicationLogging.Checked += (_, _) =>
        {
            AppLoggingSettings.SetEnabled(true);
            appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "enabled"));
        };
        applicationLogging.Unchecked += (_, _) =>
        {
            appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "disabled"));
            AppLoggingSettings.SetEnabled(false);
        };
        parent.Children.Add(applicationLogging);
        parent.Children.Add(visuals.CreateMutedText("Off by default. Typed text, pointer coordinates, pairing tokens, private reconnect keys, and proofs are excluded."));
        var details = preferenceVisuals.AddNestedSection(parent, "More about application logs");
        details.Children.Add(visuals.CreateMutedText($"Records sanitized remote commands, host actions, outcomes, responses, and Windows errors. Daily JSON Lines files are written to {appLog.LogDirectory}."));

        parent.Children.Add(visuals.CreateLabel("Keep application logs for"));
        var retention = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        retention.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var days in new[] { 1, 2, 7, 14, 30 })
        {
            retention.Items.Add(new ComboBoxItem
            {
                Content = days == 1 ? "1 day" : $"{days} days",
                Tag = days,
                IsSelected = days == AppLoggingSettings.GetMaxAgeDays()
            });
        }
        retention.SelectionChanged += (_, _) =>
        {
            if (!isLoading() && retention.SelectedItem is ComboBoxItem { Tag: int days })
            {
                AppLoggingSettings.SetMaxAgeDays(days);
                appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_log_retention", Outcome: "changed", Detail: $"days={days}"));
            }
        };
        parent.Children.Add(retention);
    }
}
