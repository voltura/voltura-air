using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenEditorPreviewController
{
    private readonly Window _owner;
    private readonly Button _button;
    private readonly CustomScreenService _service;
    private readonly Func<CustomScreenDefinition?> _getDraft;
    private readonly Func<bool> _isDirty;
    private readonly Func<string, UrlOpenExecutionResult> _openPreview;
    private readonly CustomScreenEditorActivityLog _activityLog;
    private readonly Action<string> _showToast;

    public CustomScreenEditorPreviewController(
        Window owner,
        Button button,
        CustomScreenService service,
        Func<CustomScreenDefinition?> getDraft,
        Func<bool> isDirty,
        Func<string, UrlOpenExecutionResult> openPreview,
        CustomScreenEditorActivityLog activityLog,
        Action<string> showToast)
    {
        _owner = owner;
        _button = button;
        _service = service;
        _getDraft = getDraft;
        _isDirty = isDirty;
        _openPreview = openPreview;
        _activityLog = activityLog;
        _showToast = showToast;
        _button.Click += OnPreview;
        ToolTipService.SetShowOnDisabled(_button, true);
    }

    public void Refresh()
    {
        var draft = _getDraft();
        var saved = draft is not null && _service.Find(draft.Id) is not null;
        _button.IsEnabled = saved && !_isDirty();
        _button.ToolTip = saved
            ? "Preview the saved screen in a read-only window. Save changes first."
            : "Save this screen before opening its preview.";
    }

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        var draft = _getDraft();
        if (draft is null || _isDirty() || _service.Find(draft.Id) is null)
        {
            return;
        }

        var result = _openPreview(draft.Id);
        _activityLog.Write(
            "preview",
            result.Succeeded,
            result.Succeeded ? null : result.Code);
        if (result.Succeeded)
        {
            _showToast("Preview window opened");
            return;
        }

        MessageBox.Show(
            _owner,
            result.Message,
            "Custom screens",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
