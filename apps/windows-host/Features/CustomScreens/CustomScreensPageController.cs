using System.Windows;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreensPageController(
    Window owner,
    CustomScreenService service,
    PairingManager pairingManager,
    Func<string, CustomScreenViewport?, bool?, string?, UrlOpenExecutionResult> openPreview,
    Action closePreviews,
    CustomScreenEditorActivityLog activityLog,
    Action<string> showToast,
    Func<CustomScreenDefinition, CancellationToken,
        Task<CustomScreenValidationReport>>? validateDraft = null)
{
    private CustomScreensPageView? _view;

    public CustomScreensPageView CreateView()
    {
        _view = new CustomScreensPageView(
            owner,
            service,
            pairingManager,
            showToast,
            screenId => openPreview(screenId, null, null, null),
            (screenId, viewport, controlDepth, clientId) =>
                openPreview(screenId, viewport, controlDepth, clientId),
            activityLog,
            validateDraft: validateDraft);
        return _view;
    }

    public bool TryLeavePage()
    {
        if (_view?.TryLeave() == false)
        {
            return false;
        }

        closePreviews();
        return true;
    }

    public void ClosePreviews() => closePreviews();

    public void OpenFirstForScreenshot()
    {
        var screens = service.GetAll();
        if (_view is not null && screens.Count > 0)
        {
            _view.OpenEditor(screens[0]);
        }
    }

    public void ImportBytes(byte[] bytes)
    {
        _view ??= CreateView();
        _view.ImportBytes(bytes);
    }
}
