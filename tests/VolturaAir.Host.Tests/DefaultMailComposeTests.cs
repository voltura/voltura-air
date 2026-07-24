namespace VolturaAir.Host.Tests;

public sealed class DefaultMailComposeTests
{
    [Fact]
    public void BuildMailtoUriEncodesTheBody()
    {
        var uri = DefaultMailCompose.BuildMailtoUri("First line\r\nsecond & third", sendEnter: false);

        Assert.Equal("mailto:?body=First%20line%0D%0Asecond%20%26%20third%0D%0A%0D%0AThis%20email%20may%20contain%20sensitive%20information.%20Please%20handle%20it%20accordingly.", uri);
    }

    [Fact]
    public void BuildMailtoUriAppendsTheRequestedFinalLineBreak()
    {
        var uri = DefaultMailCompose.BuildMailtoUri("Body", sendEnter: true);

        Assert.Equal("mailto:?body=Body%0D%0A%0D%0AThis%20email%20may%20contain%20sensitive%20information.%20Please%20handle%20it%20accordingly.%0D%0A", uri);
    }
}
