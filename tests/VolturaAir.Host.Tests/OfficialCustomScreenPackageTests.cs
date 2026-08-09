using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class OfficialCustomScreenPackageTests
{
    private static readonly HashSet<string> PortableKinds =
        ["text", "shortcut", "builtIn", "urlOpen", "knownApp", "hostAction"];

    [Fact]
    public void GeneratedCatalogPassesTheRealPackageReaderAndPortableContract()
    {
        var directory = Environment.GetEnvironmentVariable("VOLTURA_OFFICIAL_SCREEN_DIRECTORY") ??
            Path.Combine(RepositoryRoot(), "artifacts", "custom-screens", "official");
        var packages = Directory.GetFiles(directory, "*.volturascreen").Order().ToArray();
        Assert.Equal(14, packages.Length);

        using var catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "catalog.json")));
        var entries = catalog.RootElement.GetProperty("screens").EnumerateArray().ToArray();
        Assert.Equal(14, entries.Length);

        var catalogFiles = entries.Select(entry => entry.GetProperty("packageFilename").GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(packages.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal), catalogFiles);
        Assert.All(entries, entry =>
        {
            Assert.True(entry.GetProperty("official").GetBoolean());
            Assert.Equal("0.8.10", entry.GetProperty("minimumVolturaAirVersion").GetString());
            Assert.False(entry.TryGetProperty("researchReferences", out _));
        });

        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in packages)
        {
            Assert.True(CustomScreenPackages.TryRead(path, out var inspection, out var error), error);
            var screen = inspection!.Package.Screen;
            Assert.Empty(screen.AssignedClientIds);
            Assert.StartsWith("official.", screen.Id, StringComparison.Ordinal);
            Assert.True(allIds.Add(screen.Id), $"Duplicate screen ID {screen.Id}.");
            Assert.True(allIds.Add(screen.Revision), $"Duplicate revision {screen.Revision}.");
            foreach (var section in screen.Sections)
            {
                Assert.True(allIds.Add(section.Id), $"Duplicate panel ID {section.Id}.");
                Assert.Equal(0, section.RowLimit);
                foreach (var button in section.Buttons)
                {
                    Assert.True(allIds.Add(button.Id), $"Duplicate button ID {button.Id}.");
                    Assert.Contains(button.Action.Kind, PortableKinds);
                    Assert.NotEqual("appLaunch", button.Action.Kind);
                }
            }
        }
    }

    [Fact]
    public void NewPortableActionsAreValidatedByTheCurrentPackageContract()
    {
        var source = CustomScreenService.CreateDraft();
        var actions = new[]
        {
            new CustomScreenAction("urlOpen", Url: "https://example.com/"),
            new CustomScreenAction("knownApp", ActionId: "blender"),
            new CustomScreenAction("hostAction", ActionId: "power.shutdown"),
            new CustomScreenAction("shortcut", Key: "NumpadDecimal", Modifiers: [])
        };
        var buttons = actions.Select((action, index) => source.Sections[0].Buttons[0] with
        {
            Id = $"button.portable.{index}",
            Name = $"Portable {index}",
            Label = $"Action {index}",
            Presentation = action.Kind == "shortcut" ? "label" : "iconLabel",
            Action = action
        }).ToArray();
        source = source with { Sections = [source.Sections[0] with { Buttons = buttons }] };

        Assert.True(CustomScreenPackages.TryRead(
            CustomScreenPackages.Serialize(source),
            out _,
            out var error), error);

        foreach (var invalid in new[]
        {
            new CustomScreenAction("urlOpen", Url: "file:///C:/Windows/System32/calc.exe"),
            new CustomScreenAction("knownApp", ActionId: "arbitrary.exe"),
            new CustomScreenAction("hostAction", ActionId: "command.run"),
            new CustomScreenAction("shortcut", Key: "UnsupportedKey", Modifiers: [])
        })
        {
            var invalidSource = source with
            {
                Sections = [source.Sections[0] with { Buttons = [buttons[0] with { Action = invalid }] }]
            };
            Assert.False(CustomScreenPackages.TryRead(
                CustomScreenPackages.Serialize(invalidSource),
                out _,
                out _));
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "package.json")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
