using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddApplicationLoggingSettings(StackPanel parent)
    {
        var applicationLogging = CreateCheckBox("Write application log", AppLoggingSettings.IsEnabled());
        applicationLogging.Checked += (_, _) =>
        {
            AppLoggingSettings.SetEnabled(true);
            _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "enabled"));
        };
        applicationLogging.Unchecked += (_, _) =>
        {
            _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "disabled"));
            AppLoggingSettings.SetEnabled(false);
        };
        parent.Children.Add(applicationLogging);
        parent.Children.Add(CreateMutedText("Off by default. Typed text, pointer coordinates, and pairing secrets are excluded."));
        parent.Children.Add(CreateDetailsDisclosure("application logs", $"Records sanitized remote commands, host actions, outcomes, responses, and Windows errors. Daily JSON Lines files are written to {_appLog.LogDirectory}."));

        parent.Children.Add(CreateLabel("Keep application logs for"));
        var logRetention = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 12) };
        logRetention.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var days in new[] { 1, 2, 7, 14, 30 })
        {
            logRetention.Items.Add(new ComboBoxItem
            {
                Content = days == 1 ? "1 day" : $"{days} days",
                Tag = days,
                IsSelected = days == AppLoggingSettings.GetMaxAgeDays()
            });
        }

        logRetention.SelectionChanged += (_, _) =>
        {
            if (!_isLoadingPreferences && logRetention.SelectedItem is ComboBoxItem item && item.Tag is int days)
            {
                AppLoggingSettings.SetMaxAgeDays(days);
                _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_log_retention", Outcome: "changed", Detail: $"days={days}"));
            }
        };
        parent.Children.Add(logRetention);
    }

    private void AddWindowsLockPolicySetting(StackPanel parent)
    {
        var status = _workstationLockPolicy.GetStatus();
        string? actionLabel = null;
        var enablePolicy = false;
        switch (status.State)
        {
            case WorkstationLockPolicyState.NotExplicitlyDisabled:
                parent.Children.Add(CreateMutedText("Windows does not explicitly disable workstation locking for the current user. Test the native Windows lock action below if Lock PC is not working."));
                actionLabel = "Test Lock PC";
                break;
            case WorkstationLockPolicyState.Disabled:
                var disabledText = CreateMutedText("Windows explicitly disables workstation locking for the current user.");
                disabledText.Foreground = (Brush)Resources["DangerBrush"];
                parent.Children.Add(disabledText);
                actionLabel = "Enable Windows locking";
                enablePolicy = true;
                break;
            default:
                var unavailableText = CreateMutedText("Voltura Air could not read the current-user Windows locking policy.");
                unavailableText.Foreground = (Brush)Resources["DangerBrush"];
                parent.Children.Add(unavailableText);
                break;
        }

        parent.Children.Add(CreateMutedText("Controls whether Windows allows Lock PC and Win+L for this user."));
        if (actionLabel is not null)
        {
            var actionButton = CreateButton(actionLabel, (_, _) => EnableOrTestWindowsLocking(enablePolicy), primary: true);
            actionButton.HorizontalAlignment = HorizontalAlignment.Left;
            parent.Children.Add(actionButton);
        }
    }

    private StackPanel AddPreferencesSection(StackPanel parent, ICollection<Expander> sections, string title)
    {
        var content = new StackPanel();
        var expander = new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = false,
            Style = (Style)Resources["PreferencesAccordionStyle"]
        };
        expander.Expanded += (_, _) =>
        {
            foreach (var section in sections)
            {
                if (!ReferenceEquals(section, expander))
                {
                    section.IsExpanded = false;
                }
            }
        };
        sections.Add(expander);
        parent.Children.Add(expander);
        return content;
    }

    private void EnableOrTestWindowsLocking(bool enablePolicy)
    {
        var title = enablePolicy ? "Enable Windows locking" : "Test Lock PC";
        var message = enablePolicy
            ? "Voltura Air will enable locking for this Windows user, refresh user policy, and test Lock PC. The test may immediately lock this PC."
            : "Voltura Air will test the native Windows Lock PC action. This may immediately lock this PC.";
        if (!ThemedConfirmationDialog.Show(
                this,
                title,
                message,
                enablePolicy ? "Enable and test" : "Test Lock PC",
                "Cancel",
                ConfirmationTone.Question))
        {
            return;
        }

        if (enablePolicy)
        {
            var result = _workstationLockPolicy.TryEnable();
            if (!result.Succeeded)
            {
                SelectPage(HostPage.Preferences);
                ShowToast(result.Message);
                return;
            }
        }

        var lockResult = _powerController.TryExecute(SystemPowerActions.Lock);
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "test_windows_lock",
            Outcome: lockResult.Succeeded ? "lock_request_accepted" : "failed",
            Code: lockResult.Succeeded ? null : "VAIR-POWER-EXECUTION-FAILED",
            Win32Error: lockResult.Win32Error));
        SelectPage(HostPage.Preferences);
        ShowToast(lockResult.Succeeded
            ? "Windows accepted the lock request."
            : "Windows still prevents workstation locking. A Windows policy or another program may control this setting.");
    }

    private void AddGlobalPointerSpeedSetting(StackPanel parent)
    {
        parent.Children.Add(CreateLabel("Default pointer speed"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var currentSpeed = AppPointerSettings.GetDefaultPointerSpeed();
        var slider = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
            Minimum = DevicePointerProfile.MinPointerSpeed,
            Maximum = DevicePointerProfile.MaxPointerSpeed,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = currentSpeed,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var output = new TextBlock
        {
            Text = $"{currentSpeed.ToString(CultureInfo.InvariantCulture)}%",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["TextBrush"],
            MinWidth = 48
        };
        slider.ValueChanged += (_, _) =>
        {
            var speed = (int)Math.Round(slider.Value);
            output.Text = $"{speed.ToString(CultureInfo.InvariantCulture)}%";
            if (!_isLoadingPreferences)
            {
                AppPointerSettings.SetDefaultPointerSpeed(speed);
            }
        };
        row.Children.Add(slider);
        row.Children.Add(output);
        parent.Children.Add(row);
    }

    private void AddGlobalPointerHighlightSetting(StackPanel parent)
    {
        var highlightPointer = CreateCheckBox("Highlight pointer", AppPointerSettings.HighlightPointer());
        highlightPointer.Checked += (_, _) => SetGlobalPointerHighlight(enabled: true);
        highlightPointer.Unchecked += (_, _) => SetGlobalPointerHighlight(enabled: false);
        parent.Children.Add(highlightPointer);
        parent.Children.Add(CreateMutedText("Off by default. Makes the pointer easier to find during paired-device activity; device-specific overrides take precedence."));
    }

    private void SetGlobalPointerHighlight(bool enabled)
    {
        AppPointerSettings.SetHighlightPointer(enabled);
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "pointer_highlight_default",
            Outcome: enabled ? "enabled" : "disabled"));
    }

    private void SaveGlobalPermissions(
        CheckBox sleep,
        CheckBox volume,
        CheckBox remoteLaunch,
        CheckBox pcLock,
        CheckBox blackoutDisplay,
        CheckBox displayOff,
        CheckBox screenSaver,
        CheckBox awakeControl,
        CheckBox signOut,
        CheckBox restart,
        CheckBox shutdown)
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: sleep.IsChecked == true,
            AllowVolumeControl: volume.IsChecked == true,
            AllowRemoteAppLaunch: remoteLaunch.IsChecked == true,
            AllowPcLock: pcLock.IsChecked == true,
            AllowBlackoutDisplay: blackoutDisplay.IsChecked == true,
            AllowDisplayOff: displayOff.IsChecked == true,
            AllowScreenSaver: screenSaver.IsChecked == true,
            AllowAwakeControl: awakeControl.IsChecked == true,
            AllowSignOut: signOut.IsChecked == true,
            AllowRestart: restart.IsChecked == true,
            AllowShutdown: shutdown.IsChecked == true));
    }

    private void AddYoutubeUrlSetting(StackPanel parent)
    {
        parent.Children.Add(CreateLabel("YouTube URL"));
        parent.Children.Add(CreateMutedText("Used when a paired device triggers the YouTube remote launch action. The URL stays on this PC."));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var input = new TextBox
        {
            Text = AppRemoteSettings.GetYoutubeUrl(),
            Width = 360,
            Margin = new Thickness(0, 0, 12, 0)
        };
        row.Children.Add(input);
        row.Children.Add(CreateButton("Save URL", (_, _) => SaveYoutubeUrl(input), primary: true));
        parent.Children.Add(row);
    }

    private void SaveYoutubeUrl(TextBox input)
    {
        if (AppRemoteSettings.TrySetYoutubeUrl(input.Text, out var normalizedUrl))
        {
            input.Text = normalizedUrl;
            ShowToast("YouTube URL updated");
            return;
        }

        ShowToast("Enter a valid http or https URL");
    }

    private void SetThemeMode(AppThemeMode mode)
    {
        if (!_isLoadingPreferences)
        {
            AppThemeSettings.SetMode(mode);
        }
    }

    private void SetDefaultRemoteMode(AppRemoteMode mode)
    {
        if (!_isLoadingPreferences)
        {
            AppRemoteSettings.SetDefaultRemoteMode(mode);
        }
    }
}
