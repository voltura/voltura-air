using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AppLaunchSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"voltura-launch-{Guid.NewGuid():N}");
    private readonly string _executablePath;

    public AppLaunchSettingsTests()
    {
        Directory.CreateDirectory(_directory);
        _executablePath = Path.Combine(_directory, "Example.exe");
        File.WriteAllBytes(_executablePath, []);
    }

    [Fact]
    public void CustomActionRequiresAnExistingAbsoluteExePath()
    {
        Assert.True(AppLaunchSettings.TryNormalizeCustom("Example", _executablePath, "--safe value", out var label, out var path, out var arguments, out _));
        Assert.Equal("Example", label);
        Assert.Equal(_executablePath, path);
        Assert.Equal("--safe value", arguments);

        Assert.False(AppLaunchSettings.TryNormalizeCustom("Example", "Example.exe", null, out _, out _, out _, out var relativeError));
        Assert.Contains("absolute", relativeError, StringComparison.OrdinalIgnoreCase);

        Assert.False(AppLaunchSettings.TryNormalizeCustom("Example", Path.Combine(_directory, "Example.cmd"), null, out _, out _, out _, out var extensionError));
        Assert.Contains(".exe", extensionError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomActionRejectsControlCharactersAndMissingFiles()
    {
        Assert.False(AppLaunchSettings.TryNormalizeCustom("Bad\nLabel", _executablePath, null, out _, out _, out _, out _));
        Assert.False(AppLaunchSettings.TryNormalizeCustom("ElevenChars", _executablePath, null, out _, out _, out _, out var labelError));
        Assert.Contains("10", labelError, StringComparison.Ordinal);
        Assert.False(AppLaunchSettings.TryNormalizeCustom("Example", _executablePath, "first\nsecond", out _, out _, out _, out _));
        Assert.False(AppLaunchSettings.TryNormalizeCustom("Example", Path.Combine(_directory, "Missing.exe"), null, out _, out _, out _, out var error));
        Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StoredActionsKeepKnownPresetsAndValidCustomEntriesOnly()
    {
        var actions = new[]
        {
            new AppLaunchAction("preset.browser", "Web", AppLaunchKind.Browser),
            new AppLaunchAction("custom.valid", "Example", AppLaunchKind.Custom, _executablePath, "--safe"),
            new AppLaunchAction("custom.missing", "Missing", AppLaunchKind.Custom, Path.Combine(_directory, "Missing.exe")),
            new AppLaunchAction("preset.browser", "Duplicate", AppLaunchKind.Browser),
            new AppLaunchAction("preset.unknown", "Unknown", AppLaunchKind.Spotify)
        };

        var parsed = AppLaunchSettings.Parse(AppLaunchSettings.Serialize(actions));

        Assert.Collection(
            parsed,
            preset =>
            {
                Assert.Equal("preset.browser", preset.Id);
                Assert.Equal("Web", preset.Label);
            },
            custom =>
            {
                Assert.Equal("custom.valid", custom.Id);
                Assert.Equal(_executablePath, custom.ExecutablePath);
            });
    }

    [Fact]
    public void PresetsUseCompactDefaultLabels()
    {
        Assert.Collection(
            AppLaunchSettings.GetPresets(),
            browser => Assert.Equal("WWW", browser.Label),
            spotify => Assert.Equal("Spotify", spotify.Label),
            vlc => Assert.Equal("VLC", vlc.Label),
            powerPoint => Assert.Equal("PPT", powerPoint.Label));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void InvalidStoredDataProducesNoActions(string stored)
    {
        Assert.Empty(AppLaunchSettings.Parse(stored));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
