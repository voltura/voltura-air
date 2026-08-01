using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenStoreTests
{
    [Fact]
    public void SaveAtomicallyReplacesLibraryWithoutLeavingTemporaryFiles()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var first = CustomScreenService.CreateDraft();
        var second = first with { Name = "Updated screen" };

        Assert.True(store.TrySave([first], out var firstError), firstError);
        Assert.True(store.TrySave([second], out var secondError), secondError);

        Assert.Equal("Updated screen", Assert.Single(store.Load().Screens).Name);
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.Combine(folder.Path, "Voltura Air"),
            "*.tmp"));
    }

    [Fact]
    public void AlphaUnsupportedStoreIsRemovedAndStartsAnEmptyCurrentLibrary()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        const string unsupported = """{"version":2,"screens":[]}""";
        File.WriteAllText(path, unsupported);

        var service = new CustomScreenService(store, new FakeAppLaunchService());

        Assert.Null(service.LoadError);
        Assert.False(File.Exists(path));
        Assert.True(
            service.TrySave(CustomScreenService.CreateDraft(), out _, out var error),
            error);
        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(3, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void CorruptStoreRemainsUntouchedAndReportsRecoverableError()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        const string corrupt = """{"version":1,"screens":[""";
        File.WriteAllText(path, corrupt);

        var result = store.Load();

        Assert.False(result.Succeeded);
        Assert.Contains("left unchanged", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(corrupt, File.ReadAllText(path));
    }

    [Fact]
    public void PersistedDocumentHasExplicitVersion()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);

        Assert.True(store.TrySave([CustomScreenService.CreateDraft()], out var error), error);

        using var document = JsonDocument.Parse(File.ReadAllText(
            System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json")));
        Assert.Equal(3, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void CollapsibleTrackpadStateRoundTripsInCurrentFormat()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var draft = CustomScreenService.CreateCollapsibleTrackpad(
            CustomScreenService.CreateDraft());
        draft = draft with
        {
            Sections =
            [
                draft.Sections[^1] with
                {
                    InitiallyExpanded = false,
                    TrackpadFullscreenControl = true
                }
            ]
        };

        Assert.True(store.TrySave([draft], out var error), error);
        var loaded = Assert.Single(store.Load().Screens).Sections[0];
        Assert.Equal("collapsibleTrackpad", loaded.Kind);
        Assert.False(loaded.InitiallyExpanded);
        Assert.True(loaded.TrackpadFullscreenControl);
    }

    [Fact]
    public void NavigationRingRoundTripsInCurrentFormat()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var draft = CustomScreenService.CreateNavigationRing(
            CustomScreenService.CreateDraft());
        draft = draft with { Sections = [draft.Sections[^1]] };

        Assert.True(store.TrySave([draft], out var error), error);
        var loaded = Assert.Single(store.Load().Screens).Sections[0];
        Assert.Equal("navigationRing", loaded.Kind);
        Assert.Equal(12, loaded.WidthColumns);
        Assert.Empty(loaded.Buttons);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = Directory.CreateTempSubdirectory("VolturaAir-CustomScreens-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
