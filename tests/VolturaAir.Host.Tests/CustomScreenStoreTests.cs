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
    public void CorruptStoreIsRejectedAndRemainsUntouched()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        const string corrupt = """{"screens":[""";
        File.WriteAllText(path, corrupt);

        var result = store.Load();

        Assert.False(result.Succeeded);
        Assert.Contains("left unchanged", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(corrupt, File.ReadAllText(path));
    }

    [Fact]
    public void PersistedDocumentUsesOnlyTheCurrentShape()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);

        Assert.True(store.TrySave([CustomScreenService.CreateDraft()], out var error), error);

        using var document = JsonDocument.Parse(File.ReadAllText(
            System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json")));
        Assert.Equal(4, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(
            ["version", "screens"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("{\"version\":4,\"Screens\":[]}")]
    [InlineData("{\"version\":4,\"screens\":[],\"unknown\":true}")]
    [InlineData("{\"screens\":[]}")]
    [InlineData("{\"version\":5,\"screens\":[]}")]
    public void StoreRejectsAnyJsonShapeOtherThanTheExactCurrentContract(string json)
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        File.WriteAllText(path, json);

        var result = store.Load();

        Assert.False(result.Succeeded);
        Assert.Empty(result.Screens);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void StoreAcceptsTheExactCurrentVersion()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        File.WriteAllText(path, "{\"version\":4,\"screens\":[]}");

        var result = store.Load();

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(result.Screens);
    }

    [Fact]
    public void InvalidStoreCanBeDeletedAndServiceRecoversToAnEmptyLibrary()
    {
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"version\":5,\"screens\":[]}");
        var service = new CustomScreenService(
            new CustomScreenStore(folder.Path),
            new FakeAppLaunchService());

        Assert.NotNull(service.LoadError);
        Assert.True(service.TryDeleteInvalidData(out var error), error);

        Assert.False(File.Exists(path));
        Assert.Null(service.LoadError);
        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void DeleteInvalidRefusesToDeleteACurrentStore()
    {
        using var folder = new TemporaryFolder();
        var store = new CustomScreenStore(folder.Path);
        Assert.True(store.TrySave([CustomScreenService.CreateDraft()], out var saveError), saveError);
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");

        Assert.False(store.TryDeleteInvalid(out var error));

        Assert.Contains("no longer invalid", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
        Assert.True(store.Load().Succeeded);
    }

    [Fact]
    public void FailedInvalidStoreDeleteLeavesTheFileAndErrorStateIntact()
    {
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "Voltura Air", "custom-screens.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "invalid");
        var service = new CustomScreenService(
            new CustomScreenStore(folder.Path),
            new FakeAppLaunchService());
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(service.TryDeleteInvalidData(out var error));

        Assert.Contains("could not be deleted", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
        Assert.NotNull(service.LoadError);
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
