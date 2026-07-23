using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PresentationCommandsTests
{
    public static TheoryData<string, string, string, string[]> SupportedCommands => new()
    {
        { "powerpoint", "next", "ArrowRight", [] },
        { "powerpoint", "previous", "ArrowLeft", [] },
        { "powerpoint", "start", "F5", [] },
        { "powerpoint", "end", "Escape", [] },
        { "powerpoint", "black", "B", [] },
        { "google-slides", "next", "ArrowRight", [] },
        { "google-slides", "previous", "ArrowLeft", [] },
        { "google-slides", "end", "Escape", [] },
        { "google-slides", "black", "B", [] },
        { "pdf", "next", "ArrowRight", [] },
        { "pdf", "previous", "ArrowLeft", [] },
        { "pdf", "end", "Escape", [] }
    };

    [Theory]
    [MemberData(nameof(SupportedCommands))]
    public void ResolvesReviewedTargetMappings(string target, string action, string expectedKey, string[] expectedModifiers)
    {
        var resolved = PresentationCommands.TryResolve(target, action, out var shortcut);

        Assert.True(resolved);
        Assert.Equal(expectedKey, shortcut.Key);
        Assert.Equal(expectedModifiers, shortcut.Modifiers);
    }

    [Theory]
    [InlineData("google-slides", "start")]
    [InlineData("pdf", "start")]
    [InlineData("pdf", "black")]
    [InlineData("powerpoint", "pointer")]
    [InlineData("google-slides", "pointer")]
    [InlineData("pdf", "pointer")]
    public void HidesUncertainOrUnavailableTargetMappings(string target, string action)
    {
        Assert.False(PresentationCommands.TryResolve(target, action, out _));
    }
}
