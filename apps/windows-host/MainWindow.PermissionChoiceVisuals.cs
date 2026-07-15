using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private const string PermissionChoiceCheckPrefix = "✓ ";

    private bool _permissionChoiceVisualsHooked;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_permissionChoiceVisualsHooked)
        {
            return;
        }

        _permissionChoiceVisualsHooked = true;
        PageContent.LayoutUpdated += (_, _) => ApplyPermissionChoiceVisuals();
    }

    private void ApplyPermissionChoiceVisuals()
    {
        if (_activePage != HostPage.Devices ||
            _deviceDetailsPanel is null ||
            _devicesList?.SelectedItem is not ListBoxItem { Tag: DeviceListItem selected })
        {
            return;
        }

        var device = _pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == selected.ClientId);
        if (device is null)
        {
            return;
        }

        var globalPermissions = AppPermissionSettings.Load();
        ApplyPermissionChoiceVisuals("PC sleep", device.PermissionOverrides.AllowPcSleep, globalPermissions.AllowPcSleep);
        ApplyPermissionChoiceVisuals("Volume control", device.PermissionOverrides.AllowVolumeControl, globalPermissions.AllowVolumeControl);
        ApplyPermissionChoiceVisuals("Application launch", device.PermissionOverrides.AllowRemoteAppLaunch, globalPermissions.AllowRemoteAppLaunch);
        ApplyPermissionChoiceVisuals("Open web addresses", device.PermissionOverrides.AllowUrlOpen, globalPermissions.AllowUrlOpen);
        ApplyPermissionChoiceVisuals("Lock PC", device.PermissionOverrides.AllowPcLock, globalPermissions.AllowPcLock);
        ApplyPermissionChoiceVisuals("Blackout display", device.PermissionOverrides.AllowBlackoutDisplay, globalPermissions.AllowBlackoutDisplay);
        ApplyPermissionChoiceVisuals("Turn off display", device.PermissionOverrides.AllowDisplayOff, globalPermissions.AllowDisplayOff);
        ApplyPermissionChoiceVisuals("Keep awake", device.PermissionOverrides.AllowAwakeControl, globalPermissions.AllowAwakeControl);
        ApplyPermissionChoiceVisuals("Read PC clipboard", device.PermissionOverrides.AllowClipboardRead, globalPermissions.AllowClipboardRead);
        ApplyPermissionChoiceVisuals("Screen saver", device.PermissionOverrides.AllowScreenSaver, globalPermissions.AllowScreenSaver);
        ApplyPermissionChoiceVisuals("Sign out", device.PermissionOverrides.AllowSignOut, globalPermissions.AllowSignOut);
        ApplyPermissionChoiceVisuals("Restart PC", device.PermissionOverrides.AllowRestart, globalPermissions.AllowRestart);
        ApplyPermissionChoiceVisuals("Shut down PC", device.PermissionOverrides.AllowShutdown, globalPermissions.AllowShutdown);
    }

    private void ApplyPermissionChoiceVisuals(string label, bool? overrideValue, bool inheritedAllow)
    {
        var row = FindPermissionChoiceRow(label);
        if (row is null)
        {
            return;
        }

        var allowButton = FindPermissionButton(row, "Allow");
        var blockButton = FindPermissionButton(row, "Block");
        ResetPermissionButtonVisual(allowButton, "Allow", primary: overrideValue == true, danger: false);
        ResetPermissionButtonVisual(blockButton, "Block", primary: overrideValue == false, danger: overrideValue == false);

        var effectiveAllow = overrideValue ?? inheritedAllow;
        if (effectiveAllow)
        {
            ApplyEffectivePermissionVisual(allowButton, "Allow", isInherited: overrideValue is null, danger: false);
        }
        else
        {
            ApplyEffectivePermissionVisual(blockButton, "Block", isInherited: overrideValue is null, danger: true);
        }
    }

    private StackPanel? FindPermissionChoiceRow(string label)
    {
        if (_deviceDetailsPanel is null)
        {
            return null;
        }

        for (var index = 0; index < _deviceDetailsPanel.Children.Count - 1; index++)
        {
            if (_deviceDetailsPanel.Children[index] is TextBlock { Text: var text } &&
                string.Equals(text, label, StringComparison.Ordinal) &&
                _deviceDetailsPanel.Children[index + 1] is StackPanel row)
            {
                return row;
            }
        }

        return null;
    }

    private static Button? FindPermissionButton(StackPanel row, string text)
    {
        return row.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(GetPermissionButtonText(button), text, StringComparison.Ordinal));
    }

    private static string GetPermissionButtonText(Button button)
    {
        return button.Content is string text && text.StartsWith(PermissionChoiceCheckPrefix, StringComparison.Ordinal)
            ? text[PermissionChoiceCheckPrefix.Length..]
            : button.Content as string ?? string.Empty;
    }

    private void ResetPermissionButtonVisual(Button? button, string text, bool primary, bool danger)
    {
        if (button is null)
        {
            return;
        }

        button.Content = text;
        button.Background = primary ? (Brush)Resources["AccentBrush"] : (Brush)Resources["SurfaceRaisedBrush"];
        button.Foreground = primary ? (Brush)Resources["AccentTextBrush"] : danger ? (Brush)Resources["DangerBrush"] : (Brush)Resources["TextBrush"];
        button.BorderBrush = primary ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderBrush"];
        button.BorderThickness = new Thickness(1);
        button.FontWeight = FontWeights.Normal;
        button.Opacity = 1;
    }

    private void ApplyEffectivePermissionVisual(Button? button, string text, bool isInherited, bool danger)
    {
        if (button is null)
        {
            return;
        }

        button.Content = PermissionChoiceCheckPrefix + text;
        button.FontWeight = FontWeights.SemiBold;

        if (!isInherited)
        {
            return;
        }

        var brush = danger ? (Brush)Resources["DangerBrush"] : (Brush)Resources["AccentBrush"];
        button.Foreground = brush;
        button.BorderBrush = brush;
        button.BorderThickness = new Thickness(2);
    }
}
