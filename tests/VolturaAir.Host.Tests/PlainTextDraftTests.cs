namespace VolturaAir.Host.Tests;

public sealed class PlainTextDraftTests
{
    [Fact]
    public void PrepareContentsPreservesTextWithoutFinalLineBreak()
    {
        Assert.Equal("First\r\nsecond", PlainTextDraft.PrepareContents("First\r\nsecond", sendEnter: false));
    }

    [Fact]
    public void PrepareContentsAppendsRequestedFinalLineBreak()
    {
        Assert.Equal($"Body{Environment.NewLine}", PlainTextDraft.PrepareContents("Body", sendEnter: true));
    }
}
