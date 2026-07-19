using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class DeveloperSettingsSection(
    Window owner,
    ISystemPowerController powerController,
    IWorkstationLockPolicy workstationLockPolicy,
    IAppLogWriter appLog,
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    HostToastPresenter toasts,
    Action refresh)
{
    public void AddTo(StackPanel parent)
    {
        var developerMode = visuals.CreateCheckBox("Developer mode", AppDeveloperSettings.DeveloperMode());
        developerMode.Checked += (_, _) => AppDeveloperSettings.SetDeveloperMode(true);
        developerMode.Unchecked += (_, _) => AppDeveloperSettings.SetDeveloperMode(false);
        parent.Children.Add(developerMode);

        var alphaFeatures = visuals.CreateCheckBox("Enable alpha features", AppDeveloperSettings.EnableAlphaFeatures());
        alphaFeatures.Checked += (_, _) => SetAlphaFeatures(true);
        alphaFeatures.Unchecked += (_, _) => SetAlphaFeatures(false);
        parent.Children.Add(alphaFeatures);
        parent.Children.Add(visuals.CreateMutedText("Shows experimental features that are still under development. Alpha features remain unavailable to paired devices until this setting is enabled."));

        var gestureDebug = visuals.CreateCheckBox("Show gesture debug screen in the mobile app", AppDeveloperSettings.EnableGestureDebug());
        gestureDebug.Checked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(true);
        gestureDebug.Unchecked += (_, _) => AppDeveloperSettings.SetEnableGestureDebug(false);
        parent.Children.Add(gestureDebug);

        AddWindowsLockPolicySetting(preferenceVisuals.AddNestedSection(parent, "Windows locking"));
    }

    private void AddWindowsLockPolicySetting(StackPanel parent)
    {
        var status = workstationLockPolicy.GetStatus();
        string? actionLabel = null;
        var enablePolicy = false;
        switch (status.State)
        {
            case WorkstationLockPolicyState.NotExplicitlyDisabled:
                parent.Children.Add(visuals.CreateMutedText("Windows does not explicitly disable workstation locking for the current user. Test the native Windows lock action below if Lock PC is not working."));
                actionLabel = "Test Lock PC";
                break;
            case WorkstationLockPolicyState.Disabled:
                var disabledText = visuals.CreateMutedText("Windows explicitly disables workstation locking for the current user.");
                disabledText.Foreground = visuals.Brush("DangerBrush");
                parent.Children.Add(disabledText);
                actionLabel = "Enable Windows locking";
                enablePolicy = true;
                break;
            default:
                var unavailableText = visuals.CreateMutedText("Voltura Air could not read the current-user Windows locking policy.");
                unavailableText.Foreground = visuals.Brush("DangerBrush");
                parent.Children.Add(unavailableText);
                break;
        }

        parent.Children.Add(visuals.CreateMutedText("Controls whether Windows allows Lock PC and Win+L for this user."));
        if (actionLabel is not null)
        {
            var actionButton = visuals.CreateButton(actionLabel, (_, _) => EnableOrTestWindowsLocking(enablePolicy), primary: true);
            actionButton.HorizontalAlignment = HorizontalAlignment.Left;
            parent.Children.Add(actionButton);
        }
    }

    private void EnableOrTestWindowsLocking(bool enablePolicy)
    {
        var title = enablePolicy ? "Enable Windows locking" : "Test Lock PC";
        var message = enablePolicy
            ? "Voltura Air will enable locking for this Windows user, refresh user policy, and test Lock PC. The test may immediately lock this PC."
            : "Voltura Air will test the native Windows Lock PC action. This may immediately lock this PC.";
        if (!ThemedConfirmationDialog.Show(
                owner,
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
            var result = workstationLockPolicy.TryEnable();
            if (!result.Succeeded)
            {
                refresh();
                toasts.Show(result.Message);
                return;
            }
        }

        var lockResult = powerController.TryExecute(SystemPowerActions.Lock);
        appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "test_windows_lock",
            Outcome: lockResult.Succeeded ? "lock_request_accepted" : "failed",
            Code: lockResult.Succeeded ? null : "VAIR-POWER-EXECUTION-FAILED",
            Win32Error: lockResult.Win32Error));
        refresh();
        toasts.Show(lockResult.Succeeded
            ? "Windows accepted the lock request."
            : "Windows still prevents workstation locking. A Windows policy or another program may control this setting.");
    }

    private void SetAlphaFeatures(bool enabled)
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(enabled);
        refresh();
    }
}
