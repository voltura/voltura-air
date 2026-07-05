using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private bool _permissionChoiceVisualsHooked;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_permissionChoiceVisualsHooked)
        {
            return;
        }

        _permissionChoiceVisualsHooked = true;
        PageContent.LayoutUpdated += (_, _) => ApplyInheritedPermissionChoiceVisuals();
    }

    private void ApplyInheritedPermissionChoiceVisuals()
    {
        if (_activePage != HostPage.Devices || _deviceDetailsPanel is null)
        {
            return;
        }

        var globalPermissions = AppPermissionSettings.Load();
        ApplyInheritedPermissionChoiceVisuals("PC sleep", globalPermissions.AllowPcSleep);
        ApplyInheritedPermissionChoiceVisuals("Volume control", globalPermissions.AllowVolumeControl);
    }

    private void ApplyInheritedPermissionChoiceVisuals(string label, bool inheritedAllow)
    {
        var row = FindPermissionChoiceRow(label);
        if (row is null || !IsUseGlobalSelected(row))
        {
            return;
        }

        ApplyMutedInheritedVisual(FindPermissionButton(row, "Allow"), inheritedAllow);
        ApplyMutedInheritedVisual(FindPermissionButton(row, "Block"), !inheritedAllow);
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

    private bool IsUseGlobalSelected(StackPanel row)
    {
        var useGlobalButton = FindPermissionButton(row, "Use global");
        return useGlobalButton is not null &&
            Equals(useGlobalButton.Background, (Brush)Resources["AccentBrush"]);
    }

    private static Button? FindPermissionButton(StackPanel row, string text)
    {
        return row.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content as string, text, StringComparison.Ordinal));
    }

    private void ApplyMutedInheritedVisual(Button? button, bool isInherited)
    {
        if (button is null || !isInherited)
        {
            return;
        }

        button.Foreground = (Brush)Resources["MutedTextBrush"];
        button.BorderBrush = (Brush)Resources["MutedTextBrush"];
        button.Opacity = 0.78;
    }
}
