using Microsoft.AspNetCore.Http;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WebHostStaticFilesTests
{
    [Fact]
    public void ResolveStaticRootSupportsStandardAndCliBuildLayouts()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"voltura-air-static-root-{Guid.NewGuid():N}");
        var staticRoot = Path.Combine(repositoryRoot, "apps", "mobile-web", "dist");
        var standardBase = Path.Combine(repositoryRoot, "apps", "windows-host", "bin", "Debug", "net10.0-windows");
        var cliBase = Path.Combine(repositoryRoot, "apps", "windows-host", "bin", "cli", "Debug", "net10.0-windows");
        Directory.CreateDirectory(staticRoot);

        try
        {
            Assert.Equal(staticRoot, WebHostStaticFiles.ResolveStaticRoot(standardBase));
            Assert.Equal(staticRoot, WebHostStaticFiles.ResolveStaticRoot(cliBase));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveStaticFilePathKeepsRequestsInsideStaticRoot()
    {
        var staticRoot = Path.Combine(Path.GetTempPath(), $"voltura-air-static-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staticRoot);

        try
        {
            var expected = Path.GetFullPath(Path.Combine(staticRoot, "assets", "app.js"));

            Assert.Equal(expected, WebHostStaticFiles.ResolveStaticFilePath(staticRoot, "/assets/app.js"));
            Assert.Null(WebHostStaticFiles.ResolveStaticFilePath(staticRoot, "/../secret.txt"));
        }
        finally
        {
            Directory.Delete(staticRoot, recursive: true);
        }
    }

    [Fact]
    public void StaticCacheHeadersKeepEntryPointsFreshAndFingerprintAssetsImmutable()
    {
        var indexContext = new DefaultHttpContext();
        var buildIdContext = new DefaultHttpContext();
        var assetContext = new DefaultHttpContext();

        WebHostStaticFiles.SetStaticCacheHeaders(indexContext.Response, "index.html");
        WebHostStaticFiles.SetStaticCacheHeaders(buildIdContext.Response, "web-build-id.txt");
        WebHostStaticFiles.SetStaticCacheHeaders(assetContext.Response, "/assets/app.js");

        Assert.Equal("no-store, no-cache, must-revalidate", indexContext.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", indexContext.Response.Headers.Pragma.ToString());
        Assert.Equal("0", indexContext.Response.Headers.Expires.ToString());
        Assert.Equal("no-store, no-cache, must-revalidate", buildIdContext.Response.Headers.CacheControl.ToString());
        Assert.Equal("public, max-age=31536000, immutable", assetContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void ReadsTrimmedWebClientBuildId()
    {
        var staticRoot = Path.Combine(Path.GetTempPath(), $"voltura-air-static-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staticRoot);

        try
        {
            File.WriteAllText(Path.Combine(staticRoot, "web-build-id.txt"), " build-a\n");

            Assert.Equal("build-a", WebHostStaticFiles.ReadWebClientBuildId(staticRoot));
        }
        finally
        {
            Directory.Delete(staticRoot, recursive: true);
        }
    }
}
