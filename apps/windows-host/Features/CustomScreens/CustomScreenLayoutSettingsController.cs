using System.Windows;
using CheckBox = System.Windows.Controls.CheckBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenLayoutSettingsController
{
    private readonly CheckBox _orientationLayouts;
    private readonly CheckBox _navigationHeader;
    private readonly Func<CustomScreenDefinition?> _getDraft;
    private readonly Func<bool> _isSynchronizing;
    private readonly Action<CustomScreenDefinition> _applyDraft;

    public CustomScreenLayoutSettingsController(
        CheckBox orientationLayouts,
        CheckBox navigationHeader,
        Func<CustomScreenDefinition?> getDraft,
        Func<bool> isSynchronizing,
        Action<CustomScreenDefinition> applyDraft)
    {
        _orientationLayouts = orientationLayouts;
        _navigationHeader = navigationHeader;
        _getDraft = getDraft;
        _isSynchronizing = isSynchronizing;
        _applyDraft = applyDraft;
        _orientationLayouts.Checked += OnOrientationLayoutsChanged;
        _orientationLayouts.Unchecked += OnOrientationLayoutsChanged;
        _navigationHeader.Checked += OnNavigationHeaderChanged;
        _navigationHeader.Unchecked += OnNavigationHeaderChanged;
    }

    private void OnOrientationLayoutsChanged(object sender, RoutedEventArgs e)
    {
        var draft = _getDraft();
        if (draft is null || _isSynchronizing())
        {
            return;
        }

        var enabled = _orientationLayouts.IsChecked == true;
        _applyDraft(enabled && !draft.OrientationLayoutsEnabled
            ? CustomScreenOrientationEditing.Enable(draft)
            : draft with { OrientationLayoutsEnabled = enabled });
    }

    private void OnNavigationHeaderChanged(object sender, RoutedEventArgs e)
    {
        var draft = _getDraft();
        if (draft is not null && !_isSynchronizing())
        {
            _applyDraft(draft with
            {
                ShowNavigationHeader = _navigationHeader.IsChecked == true
            });
        }
    }
}
