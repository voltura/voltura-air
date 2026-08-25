using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class DiagnosticsClientMessageValidatorTests
{
    [Theory]
    [InlineData("""{ "type": "diagnostics.get", "operationId": "diagnostics-1" }""", true)]
    [InlineData("""{ "type": "diagnostics.get", "operationId": "diagnostics-1", "query": "all" }""", false)]
    [InlineData("""{ "type": "diagnostics.get", "operationId": "bad id" }""", false)]
    public void ValidatesBoundedRequest(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, ClientMessageValidator.IsValidAuthenticatedMessage(document.RootElement, "diagnostics.get"));
    }
}
