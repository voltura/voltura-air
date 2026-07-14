using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostOriginPolicyTests : WebHostServiceTestBase
{
    [Fact]
    public void OriginPolicyAllowsNormalLocalAndDevOrigins()
    {
        var originalClientUrl = Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");

        try
        {
            Environment.SetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL", "http://dev.example.test:5173");

            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest(null)));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://192.168.68.51:51395")));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://192.168.68.20:5173")));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://localhost:5173")));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://dev.example.test:5173")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL", originalClientUrl);
        }
    }

    [Fact]
    public void OriginPolicyRejectsClearlyUnrelatedPublicOrigins()
    {
        Assert.False(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("https://example.com")));
    }
}
