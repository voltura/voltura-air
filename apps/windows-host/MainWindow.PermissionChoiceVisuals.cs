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
        ResetPermissionButtonVisual(allowButton, "Allow");
        ResetPermissionButtonVisual(blockButton, "Block");

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

    private void ResetPermissionButtonVisual(Button? button, string text)
    {
        if (button is null)
        {
            return;
        }

        button.Content = text;
        button.FontWeight = FontWeights.Normal;
        button.BorderThickness = new Thickness(1);
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
