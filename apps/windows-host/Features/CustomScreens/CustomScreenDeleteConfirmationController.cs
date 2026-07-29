using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenDeleteConfirmationController(
    CheckBox libraryDeleteCheckBox,
    CheckBox libraryHideCheckBox,
    CheckBox editorDeleteCheckBox,
    CheckBox editorHideCheckBox)
{
    private bool _synchronizing;

    public void Synchronize()
    {
        _synchronizing = true;
        var confirmDeletes = CustomScreenEditorSettings.ConfirmDeletes();
        var confirmHides = CustomScreenEditorSettings.ConfirmHides();
        libraryDeleteCheckBox.IsChecked = confirmDeletes;
        editorDeleteCheckBox.IsChecked = confirmDeletes;
        libraryHideCheckBox.IsChecked = confirmHides;
        editorHideCheckBox.IsChecked = confirmHides;
        _synchronizing = false;
    }

    public void HandleDeleteChanged(object sender)
    {
        if (_synchronizing || sender is not CheckBox checkBox)
        {
            return;
        }

        CustomScreenEditorSettings.SetConfirmDeletes(checkBox.IsChecked == true);
        Synchronize();
    }

    public void HandleHideChanged(object sender)
    {
        if (_synchronizing || sender is not CheckBox checkBox)
        {
            return;
        }

        CustomScreenEditorSettings.SetConfirmHides(checkBox.IsChecked == true);
        Synchronize();
    }
}
