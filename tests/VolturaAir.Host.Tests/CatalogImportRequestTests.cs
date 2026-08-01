using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CatalogImportRequestTests
{
    private const string Id = "23547b1d-5c7b-4448-84d6-c632f67a4492";

    [Fact]
    public void ProtocolWithoutSourceUsesFixedProductionCatalog()
    {
        var request = CatalogImportRequestStore.Find(
            [$"voltura-air://import?id={Id}"]);

        Assert.Equal(Id, request?.Id);
        Assert.Equal(
            CatalogImportRequestStore.ProductionCatalogBaseUrl,
            request?.CatalogBaseUrl);
    }

    [Fact]
    public void ProductCommunityLinkUsesProductionCatalog()
    {
        Assert.Equal(
            CatalogImportRequestStore.ProductionCatalogBaseUrl + "/",
            ProductWebsite.CustomScreenLibraryUrl);
    }

#if DEBUG
    [Fact]
    public void DebugProtocolAcceptsLocalSiteDevelopmentCatalog()
    {
        var source = Uri.EscapeDataString("http://127.0.0.1:8765/screens");
        var request = CatalogImportRequestStore.Find(
            [$"voltura-air://import?id={Id}&source={source}"]);

        Assert.Equal(Id, request?.Id);
        Assert.Equal(
            "http://127.0.0.1:8765/screens",
            request?.CatalogBaseUrl);
    }
#endif

    [Theory]
    [InlineData("https://example.com/screens")]
    [InlineData("http://voltura.se/air/screens")]
    [InlineData("http://127.0.0.1:8765/not-screens")]
    public void ProtocolRejectsUnapprovedCatalogSource(string source)
    {
        var encodedSource = Uri.EscapeDataString(source);

        Assert.Null(CatalogImportRequestStore.Find(
            [$"voltura-air://import?id={Id}&source={encodedSource}"]));
    }
}
