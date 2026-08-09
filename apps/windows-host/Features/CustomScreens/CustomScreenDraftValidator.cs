using Microsoft.Web.WebView2.Core;

namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenDraftValidator(
    CustomScreenService service,
    CustomScreenDraftLayoutValidator layoutValidator)
{
    public async Task<CustomScreenValidationReport> ValidateAsync(
        CustomScreenDefinition draft,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomScreenLayoutIssue>? layoutIssues = null;
        string? layoutFailure = null;
        try
        {
            layoutIssues = await layoutValidator.ValidateAsync(
                draft,
                cancellationToken);
        }
        catch (Exception exception) when (exception is
            WebView2RuntimeNotFoundException or
            InvalidOperationException or
            UnauthorizedAccessException or
            IOException or
            TimeoutException or
            System.Text.Json.JsonException)
        {
            layoutFailure = exception is WebView2RuntimeNotFoundException
                ? "Install or repair the Microsoft Edge WebView2 Runtime to check rendered layouts."
                : "The real mobile preview renderer was unavailable for this validation run.";
        }

        return CustomScreenValidationAnalyzer.Analyze(
            draft,
            service.GetKnownAppProfiles(),
            service.GetApprovedAppActions(),
            layoutIssues,
            layoutFailure,
            AppPermissionSettings.Load());
    }
}
