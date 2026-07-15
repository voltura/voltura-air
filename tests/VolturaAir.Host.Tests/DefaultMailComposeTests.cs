namespace VolturaAir.Host.Tests;

public sealed class DefaultMailComposeTests
{
    [Fact]
    public void BuildMailtoUriEncodesTheBody()
    {
        var uri = DefaultMailCompose.BuildMailtoUri("First line\r\nsecond & third", sendEnter: false);

        Assert.Equal("mailto:?body=First%20line%0D%0Asecond%20%26%20third", uri);
    }

    [Fact]
    public void BuildMailtoUriAppendsTheRequestedFinalLineBreak()
    {
        var uri = DefaultMailCompose.BuildMailtoUri("Body", sendEnter: true);

        Assert.Equal("mailto:?body=Body%0D%0A", uri);
    }
}
