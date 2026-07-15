using System.ComponentModel;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class UrlOpenServiceTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData(" example.com/page?q=test ", "https://example.com/page?q=test")]
    [InlineData("192.168.1.1", "https://192.168.1.1/")]
    [InlineData("localhost:3000/path", "https://localhost:3000/path")]
    [InlineData("http://192.168.1.1", "http://192.168.1.1/")]
    [InlineData("https://example.com", "https://example.com/")]
    public void ExecuteNormalizesAndLaunchesHttpUrls(string input, string expected)
    {
        var launcher = new RecordingUrlShellLauncher();
        var result = new UrlOpenService(launcher).Execute(input);

        Assert.True(result.Succeeded);
        Assert.Equal("accepted", result.Code);
        Assert.Equal("Open request sent.", result.Message);
        Assert.Equal(expected, result.NormalizedUrl);
        Assert.Equal(new Uri(expected), Assert.Single(launcher.Opened));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://")]
    [InlineData("https://exa\u0001mple.com")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:80")]
    [InlineData("data:text/plain,test")]
    [InlineData("data:123")]
    [InlineData("mailto:user@example.com")]
    public void ExecuteRejectsInvalidOrUnsupportedValuesWithoutLaunching(string input)
    {
        var launcher = new RecordingUrlShellLauncher();
        var result = new UrlOpenService(launcher).Execute(input);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Code, new[] { "invalid-url", "unsupported-scheme" });
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public void ExecuteMapsShellFailureWithoutExposingNativeDetails()
    {
        var result = new UrlOpenService(new ThrowingUrlShellLauncher()).Execute("example.com");

        Assert.False(result.Succeeded);
        Assert.Equal("launch-failed", result.Code);
        Assert.Contains("default browser", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("native failure detail", result.Message, StringComparison.Ordinal);
        Assert.Equal("https://example.com/", result.NormalizedUrl);
    }

    private sealed class RecordingUrlShellLauncher : IUrlShellLauncher
    {
        public List<Uri> Opened { get; } = [];

        public void Open(Uri uri) => Opened.Add(uri);
    }

    private sealed class ThrowingUrlShellLauncher : IUrlShellLauncher
    {
        public void Open(Uri uri) => throw new Win32Exception(2, "native failure detail");
    }
}
