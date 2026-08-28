using System.Text.Json;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class AiAssistantReadToolsTests
{
    [Fact]
    public async Task SearchesAndReadsOnlyBundledDocumentation()
    {
        using JsonDocument query = JsonDocument.Parse("""{"query":"Phone Webcam"}""");
        object? search = await AiAssistantReadTools.InvokeAsync("search_voltura_docs", query.RootElement, TestContext.Current.CancellationToken);
        string searchJson = JsonSerializer.Serialize(search);
        Assert.Contains("docs/features.md", searchJson, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse("""{"document":"docs/features.md"}""");
        string readJson = JsonSerializer.Serialize(await AiAssistantReadTools.InvokeAsync("read_voltura_doc", document.RootElement, TestContext.Current.CancellationToken));
        Assert.Contains("Phone Webcam", readJson, StringComparison.Ordinal);

        using JsonDocument outside = JsonDocument.Parse("""{"document":"../secrets.txt"}""");
        string rejected = JsonSerializer.Serialize(await AiAssistantReadTools.InvokeAsync("read_voltura_doc", outside.RootElement, TestContext.Current.CancellationToken));
        Assert.Contains("\"success\":false", rejected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsUnknownToolsAndMalformedArguments()
    {
        using JsonDocument arguments = JsonDocument.Parse("""{"query":"features"}""");
        string unknown = JsonSerializer.Serialize(await AiAssistantReadTools.InvokeAsync("run_command", arguments.RootElement, TestContext.Current.CancellationToken));
        Assert.Contains("\"success\":false", unknown, StringComparison.Ordinal);

        using JsonDocument malformed = JsonDocument.Parse("""{"query":42}""");
        string rejected = JsonSerializer.Serialize(await AiAssistantReadTools.InvokeAsync("search_voltura_docs", malformed.RootElement, TestContext.Current.CancellationToken));
        Assert.Contains("\"success\":false", rejected, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvertisesOnlyTheThreeReadOnlyTools()
    {
        string json = JsonSerializer.Serialize(AiAssistantReadTools.Specifications);
        Assert.Contains("search_voltura_docs", json, StringComparison.Ordinal);
        Assert.Contains("read_voltura_doc", json, StringComparison.Ordinal);
        Assert.Contains("find_user_files", json, StringComparison.Ordinal);
        Assert.DoesNotContain("shell", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("write", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundsToolResultsWithoutSplittingUnicodeScalars()
    {
        string bounded = AiAssistantReadTools.Bound(new string('a', (24 * 1024) - 2) + "😀tail");

        Assert.True(bounded.Length <= 24 * 1024);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(bounded[^2]));
    }

    [Fact]
    public void RejectsAnEmptyUserProfileBeforeResolvingItAgainstTheWorkingDirectory()
    {
        IOException error = Assert.Throws<IOException>(() => AiAssistantReadTools.NormalizeUserProfileRoot("  "));
        Assert.Contains("profile is unavailable", error.Message, StringComparison.Ordinal);
    }
}
