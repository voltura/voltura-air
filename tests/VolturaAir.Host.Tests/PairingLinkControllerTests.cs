using VolturaAir.Host;
using VolturaAir.Host.Features.Connect;

namespace VolturaAir.Host.Tests;

public sealed class PairingLinkControllerTests
{
    [Fact]
    public void GeneratedLinkUsesDedicatedPairRouteAndRequiredParameters()
    {
        using var store = new TempPairingStore();
        var controller = new PairingLinkController(
            new PairingManager(store.Store),
            "http://192.168.68.51:51395",
            clientUrl: null);

        var url = new Uri(controller.Url);
        var parameters = ParseQuery(url);

        Assert.Equal("/pair", url.AbsolutePath);
        Assert.InRange(controller.Url.Length, 1, 256);
        Assert.Matches("^[A-Za-z0-9_-]{32}$", parameters["t"]);
        Assert.Equal(AppVersion.Display, parameters["v"]);
        Assert.Equal(2, parameters.Count);
        Assert.DoesNotContain("h", parameters.Keys);
    }

    [Fact]
    public void RefreshReplacesTheLinkOnlyWhenItsDeadlineIsReached()
    {
        using var store = new TempPairingStore();
        var controller = new PairingLinkController(
            new PairingManager(store.Store),
            "http://192.168.68.51:51395",
            clientUrl: null);
        var initialUrl = controller.Url;

        Assert.False(controller.RefreshIfDue(controller.RefreshAt.AddTicks(-1)));
        Assert.Equal(initialUrl, controller.Url);
        Assert.True(controller.RefreshIfDue(controller.RefreshAt));
        Assert.NotEqual(initialUrl, controller.Url);
    }

    [Fact]
    public void ServerUrlUpdateRotatesTheLinkAndMovesAHostServedClient()
    {
        using var store = new TempPairingStore();
        var controller = new PairingLinkController(
            new PairingManager(store.Store),
            "http://192.168.68.51:51395",
            clientUrl: null);
        var initialUrl = controller.Url;

        Assert.True(controller.UpdateServerUrl("http://192.168.68.52:51395"));

        var updatedUrl = new Uri(controller.Url);
        Assert.NotEqual(initialUrl, controller.Url);
        Assert.Equal("http://192.168.68.52:51395", updatedUrl.GetLeftPart(UriPartial.Authority));
        Assert.DoesNotContain("h", ParseQuery(updatedUrl).Keys);
        Assert.False(controller.UpdateServerUrl("http://192.168.68.52:51395"));
    }

    [Fact]
    public void SeparateClientOriginKeepsItsUrlAndAddsTheHostPortHint()
    {
        using var store = new TempPairingStore();
        var controller = new PairingLinkController(
            new PairingManager(store.Store),
            "http://192.168.68.51:51395",
            "http://192.168.68.51:5173");

        var url = new Uri(controller.Url);
        var parameters = ParseQuery(url);

        Assert.Equal("http://192.168.68.51:5173", url.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/pair", url.AbsolutePath);
        Assert.InRange(controller.Url.Length, 1, 256);
        Assert.Equal("51395", parameters["h"]);
        Assert.Equal(3, parameters.Count);
    }

    [Theory]
    [InlineData("http://192.168.68.51:5173", "http://192.168.68.51:51395", "51395")]
    [InlineData("http://192.168.68.51:5173", "http://10.0.0.20:51395", "http://10.0.0.20:51395")]
    public void HostHintUsesCompactPortOnlyForSameHost(string clientUrl, string serverUrl, string expectedHint)
    {
        Assert.Equal(expectedHint, PairingLinkController.CreateHostHint(clientUrl, serverUrl));
    }

    private static Dictionary<string, string> ParseQuery(Uri url)
    {
        return url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]), StringComparer.Ordinal);
    }
}
