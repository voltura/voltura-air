using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AppRemoteSettingsTests
{
    [Theory]
    [InlineData("https://youtube.com", "https://youtube.com/")]
    [InlineData("http://localhost:8080/youtube", "http://localhost:8080/youtube")]
    public void TryNormalizeYoutubeUrlAcceptsAbsoluteHttpUrls(string value, string expected)
    {
        Assert.True(AppRemoteSettings.TryNormalizeYoutubeUrl(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("youtube.com")]
    [InlineData("ftp://youtube.com")]
    [InlineData("not a url")]
    public void TryNormalizeYoutubeUrlRejectsInvalidOrUnsupportedUrls(string value)
    {
        Assert.False(AppRemoteSettings.TryNormalizeYoutubeUrl(value, out _));
    }
}
