using System.Text.Json;
using VolturaAir.Host;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host.Tests;

public sealed class AiAssistantProtocolTests
{
    [Theory]
    [InlineData("ai.assistant.open", """{ "type": "ai.assistant.open", "operationId": "assistant-1", "clientSignature": "proof" }""", true)]
    [InlineData("ai.assistant.open", """{ "type": "ai.assistant.open", "operationId": "assistant-1", "clientSignature": "proof", "question": "secret" }""", false)]
    [InlineData("ai.assistant.ask", """{ "type": "ai.assistant.ask", "operationId": "assistant-2", "question": "How does Relay work?", "clientSignature": "proof" }""", true)]
    [InlineData("ai.assistant.ask", """{ "type": "ai.assistant.ask", "operationId": "assistant-2", "question": "", "clientSignature": "proof" }""", false)]
    [InlineData("ai.assistant.reset", """{ "type": "ai.assistant.reset", "operationId": "assistant-3", "clientSignature": "proof" }""", true)]
    [InlineData("ai.assistant.close", """{ "type": "ai.assistant.close", "operationId": "assistant-4" }""", true)]
    [InlineData("ai.assistant.close", """{ "type": "ai.assistant.close", "operationId": "assistant-4", "clientSignature": "unexpected" }""", false)]
    public void ValidatesExactAssistantMessages(string type, string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, type));
    }

    [Fact]
    public void BoundsQuestions()
    {
        string oversized = new('a', AiAssistantProtocol.MaximumQuestionCharacters + 1);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "ai.assistant.ask",
            operationId = "assistant-oversized",
            question = oversized,
            clientSignature = "proof"
        }));
        Assert.False(ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "ai.assistant.ask"));
    }

    [Fact]
    public void ChunksWithoutSplittingUnicodeScalars()
    {
        string text = new string('a', AiAssistantProtocol.MaximumMessageChunkCharacters - 1) + "😀" + new string('b', 40);
        string[] chunks = [.. AiAssistantProtocol.ChunkMessage(text)];

        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, AiAssistantProtocol.MaximumMessageChunkCharacters));
        Assert.All(chunks.SkipLast(1), chunk => Assert.False(char.IsHighSurrogate(chunk[^1])));
    }

    [Fact]
    public void BoundsCompleteMessagesAndPreservesUnicodeScalars()
    {
        string oversized = new string('a', AiAssistantProtocol.MaximumMessageCharacters - 2) + "😀tail";

        string bounded = CodexAppServerClient.BoundText(oversized);

        Assert.Equal(AiAssistantProtocol.MaximumMessageCharacters - 1, bounded.Length);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(bounded[^2]));
        Assert.DoesNotContain('�', bounded);
    }

    [Fact]
    public void BoundsShortErrorResultsWithoutSplittingUnicodeScalars()
    {
        string bounded = AiAssistantProtocol.BoundWithEllipsis(new string('a', 238) + "😀tail", 240);

        Assert.True(bounded.Length <= 240);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(bounded[^2]));
    }

    [Fact]
    public void RequiresEveryEffectiveIntegrationAndFeatureToBeDisabled()
    {
        var featureValues = AiAssistantProfile.DisabledFeatures.ToDictionary(feature => feature, _ => false);
        JsonElement isolated = JsonSerializer.SerializeToElement(new
        {
            web_search = "disabled",
            features = featureValues,
            mcp_servers = new { docs = new { enabled = false } }
        });
        CodexAppServerClient.VerifyIsolation(isolated, ["docs"]);

        JsonElement enabledIntegration = JsonSerializer.SerializeToElement(new
        {
            web_search = "disabled",
            features = featureValues,
            mcp_servers = new { docs = new { enabled = false }, late = new { enabled = true } }
        });
        Assert.Throws<CodexCompatibilityException>(() => CodexAppServerClient.VerifyIsolation(enabledIntegration, ["docs"]));

        featureValues["shell_tool"] = true;
        JsonElement enabledFeature = JsonSerializer.SerializeToElement(new
        {
            web_search = "disabled",
            features = featureValues,
            mcp_servers = new { docs = new { enabled = false } }
        });
        Assert.Throws<CodexCompatibilityException>(() => CodexAppServerClient.VerifyIsolation(enabledFeature, ["docs"]));
    }

    [Fact]
    public void RecoversTheNewestUntitledThreadLeftByANameFailure()
    {
        string root = Path.GetFullPath(AiAssistantProfile.KnowledgeRoot);
        CodexThreadSummary? selected = CodexAppServerClient.SelectAssistantThread(
        [
            new("new-thread", "Untitled assistant", root),
            new("old-thread", AiAssistantProfile.ThreadName, root),
            new("unrelated", "Untitled assistant", Path.GetTempPath())
        ], root);

        Assert.NotNull(selected);
        Assert.Equal("new-thread", selected.Id);
    }
}
