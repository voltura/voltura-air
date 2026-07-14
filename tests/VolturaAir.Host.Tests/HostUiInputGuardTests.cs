using VolturaAir.Host;
using System.Text.Json;

namespace VolturaAir.Host.Tests;

public sealed class HostUiInputGuardTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(20, true)]
    public void WindowCommandHitTestAllowsCaptionButtonsOnly(int hitTest, bool expected)
    {
        Assert.Equal(expected, HostUiInputGuard.IsWindowCommandHitTest(hitTest));
    }

    [Theory]
    [InlineData("""{ "key": "ArrowDown", "modifiers": ["Win"] }""", true)]
    [InlineData("""{ "key": "arrowdown", "modifiers": ["win"] }""", true)]
    [InlineData("""{ "key": "ArrowDown", "modifiers": ["Win", "Shift"] }""", false)]
    [InlineData("""{ "key": "ArrowDown", "modifiers": ["Control"] }""", false)]
    [InlineData("""{ "key": "D", "modifiers": ["Win"] }""", false)]
    public void MinimizeWindowShortcutRequiresOnlyWinArrowDown(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, HostUiInputGuard.IsMinimizeWindowShortcut(document.RootElement));
    }

    [Theory]
    [InlineData("""{ "key": "D", "modifiers": ["Win"] }""", true)]
    [InlineData("""{ "key": "d", "modifiers": ["win"] }""", true)]
    [InlineData("""{ "key": "D", "modifiers": ["Win", "Shift"] }""", false)]
    [InlineData("""{ "key": "D", "modifiers": [] }""", false)]
    [InlineData("""{ "key": "ArrowDown", "modifiers": ["Win"] }""", false)]
    public void ShowDesktopShortcutRequiresOnlyWinD(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, HostUiInputGuard.IsShowDesktopShortcut(document.RootElement));
    }

}
