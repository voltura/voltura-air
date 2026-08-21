namespace VolturaAir.Host.Tests;

public sealed class ScreenViewFramePacerTests
{
    [Fact]
    public void ContinuousHighRefreshSourceCannotExceedSelectedFrameRate()
    {
        var pacer = new ScreenViewFramePacer(timestampFrequency: 120);
        int encoded = 0;

        for (long timestamp = 0; timestamp < 120; timestamp++)
        {
            if (pacer.ShouldEncode(timestamp, framesPerSecond: 30)) encoded++;
        }

        Assert.Equal(30, encoded);
    }

    [Fact]
    public void ResetAllowsTheNextFrameImmediately()
    {
        var pacer = new ScreenViewFramePacer(timestampFrequency: 1_000);
        Assert.True(pacer.ShouldEncode(100, framesPerSecond: 30));
        Assert.False(pacer.ShouldEncode(101, framesPerSecond: 30));

        pacer.Reset();

        Assert.True(pacer.ShouldEncode(101, framesPerSecond: 30));
    }

    [Theory]
    [InlineData(75)]
    [InlineData(144)]
    public void NonDivisibleDisplayRefreshRetainsTheSelectedFrameRate(int sourceFramesPerSecond)
    {
        const int frequency = 360_000;
        var pacer = new ScreenViewFramePacer(frequency);
        int sourceInterval = frequency / sourceFramesPerSecond;
        int encoded = 0;

        for (long timestamp = 0; timestamp < frequency; timestamp += sourceInterval)
        {
            if (pacer.ShouldEncode(timestamp, framesPerSecond: 60)) encoded++;
        }

        Assert.InRange(encoded, 59, 60);
    }
}
