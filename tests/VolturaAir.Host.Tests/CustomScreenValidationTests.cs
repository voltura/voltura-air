using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenValidationTests
{
    [Fact]
    public void ClippedLabelIsAdvisoryAndSuggestsALargerExistingSize()
    {
        var draft = CustomScreenService.CreateDraft();
        var button = draft.Sections[0].Buttons[0];

        var report = CustomScreenValidationAnalyzer.Analyze(
            draft,
            AvailableKnownApplications(),
            [],
            [new("label", button.Id, button.Label, "portrait", "standard")]);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(CustomScreenValidationSeverity.Warning, finding.Severity);
        Assert.Equal(button.Id, finding.ButtonId);
        Assert.Contains("Wide or Fill", finding.Resolution, StringComparison.Ordinal);
        Assert.Contains(report.PassedChecks, check =>
            check.Contains("save contract", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedShortcutReportsTheExistingSaveContractWithoutRepair()
    {
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0];
        var button = section.Buttons[0] with
        {
            Presentation = "label",
            Action = new CustomScreenAction(
                "shortcut",
                Key: "NotAKey",
                Modifiers: [])
        };
        draft = draft with
        {
            Sections = [section with { Buttons = [button] }]
        };

        var report = CustomScreenValidationAnalyzer.Analyze(
            draft,
            AvailableKnownApplications(),
            [],
            []);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(CustomScreenValidationSeverity.CannotSave, finding.Severity);
        Assert.Equal(button.Id, finding.ButtonId);
        Assert.Equal("NotAKey", button.Action.Key);
    }

    [Fact]
    public void UnavailableTargetApplicationIsOnlyAWarning()
    {
        var draft = CustomScreenService.CreateDraft();
        var section = draft.Sections[0];
        var button = section.Buttons[0] with
        {
            Action = new CustomScreenAction("knownApp", ActionId: "windowsPhotos")
        };
        draft = draft with
        {
            Sections = [section with { Buttons = [button] }]
        };

        var report = CustomScreenValidationAnalyzer.Analyze(
            draft,
            [new("windowsPhotos", "Windows Photos", false)],
            [],
            []);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(CustomScreenValidationSeverity.Warning, finding.Severity);
        Assert.Contains("ms-photos", finding.Resolution, StringComparison.Ordinal);
        Assert.Contains(report.PassedChecks, check =>
            check.Contains("save contract", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftPreviewLeaseIsMemoryOnlyAndRemovedOnDispose()
    {
        var service = new CustomScreenService(
            new InMemoryCustomScreenStore(),
            new FakeAppLaunchService());
        var draft = CustomScreenService.CreateDraft();
        string previewId;

        using (var lease = service.BeginDraftPreview(draft))
        {
            previewId = lease.ScreenId;
            var preview = Assert.IsType<CustomScreenMobileDefinition>(
                service.GetPreviewDefinition(previewId));
            Assert.Equal(previewId, preview.Id);
            Assert.Null(service.Find(previewId));
        }

        Assert.Null(service.GetPreviewDefinition(previewId));
        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void ValidationIsIndependentOfDeviceSpecificPermissions()
    {
        var draft = CustomScreenService.CreateDraft();

        var report = CustomScreenValidationAnalyzer.Analyze(
            draft,
            AvailableKnownApplications(),
            [],
            []);

        Assert.Empty(report.Findings);
        Assert.Contains(report.PassedChecks, check =>
            check.Contains("save contract", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftPreviewLeasesAreBoundedAndCapacityReturnsAfterCleanup()
    {
        var service = new CustomScreenService(
            new InMemoryCustomScreenStore(),
            new FakeAppLaunchService());
        var draft = CustomScreenService.CreateDraft();
        var leases = Enumerable.Range(0, 4)
            .Select(_ => service.BeginDraftPreview(draft))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            service.BeginDraftPreview(draft));
        foreach (var lease in leases)
        {
            lease.Dispose();
            lease.Dispose();
        }

        using var replacement = service.BeginDraftPreview(draft);
        Assert.NotNull(service.GetPreviewDefinition(replacement.ScreenId));
    }

    [Fact]
    public void WebViewLayoutResultRequiresTheExpectedArrayShape()
    {
        var decoded = CustomScreenDraftLayoutValidator.DecodeLayoutIssues(
            "[{\"kind\":\"label\",\"buttonId\":\"button.play\",\"label\":\"Play\",\"orientation\":\"portrait\",\"size\":\"standard\"}]");

        var issue = Assert.Single(decoded);
        Assert.Equal("button.play", issue.ButtonId);
        Assert.Throws<InvalidOperationException>(() =>
            CustomScreenDraftLayoutValidator.DecodeLayoutIssues("{}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            CustomScreenDraftLayoutValidator.DecodeLayoutIssues("not-json"));
    }

    private static IReadOnlyList<KnownAppProfileSummary>
        AvailableKnownApplications() =>
        [.. KnownAppProfiles.All.Select(profile =>
            new KnownAppProfileSummary(profile.Id, profile.Label, true))];
}
