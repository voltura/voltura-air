using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenPackageTests
{
    [Fact]
    public void SerializeAndReadStripsAssignmentsAndRegeneratesIds()
    {
        var source = CustomScreenService.CreateDraft() with { AssignedClientIds = ["phone-a"] };

        Assert.True(
            CustomScreenPackages.TryRead(
                CustomScreenPackages.Serialize(source),
                out var inspection,
                out var error),
            error);

        Assert.NotNull(inspection);
        Assert.Empty(inspection.ImportedScreen.AssignedClientIds);
        Assert.NotEqual(source.Id, inspection.ImportedScreen.Id);
        Assert.Equal(source.Name, inspection.ImportedScreen.Name);
    }

    [Fact]
    public void LaserPointerColorRoundTripsThroughPortablePackage()
    {
        var source = CustomScreenService.CreateDraft();
        source = CustomScreenService.CreateLaserPointer(source, source.Sections[0].Id);
        var laser = source.Sections[0].Buttons[^1];
        source = source with
        {
            Sections = [source.Sections[0] with
            {
                Buttons = [.. source.Sections[0].Buttons.Take(source.Sections[0].Buttons.Count - 1), laser with
                {
                    Action = new CustomScreenAction("laserPointer", Color: "blue")
                }]
            }]
        };

        Assert.True(CustomScreenPackages.TryRead(
            CustomScreenPackages.Serialize(source),
            out var inspection,
            out var error), error);

        var importedLaser = inspection!.ImportedScreen.Sections[0].Buttons[^1];
        Assert.Equal("laserPointer", importedLaser.Action.Kind);
        Assert.Equal("blue", importedLaser.Action.Color);
    }

    [Fact]
    public void PortablePackagesRejectHostLocalApplicationActions()
    {
        var source = CustomScreenService.CreateDraft();
        var button = source.Sections[0].Buttons[0] with
        {
            Action = new CustomScreenAction("appLaunch", ActionId: "preset.browser")
        };
        source = source with
        {
            Sections = [source.Sections[0] with { Buttons = [button] }]
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CustomScreenPackages.Serialize(source));
        Assert.Contains("Host-local", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRejectsUnsupportedVersionInvalidJsonAndOversizedInput()
    {
        var source = CustomScreenService.CreateDraft();
        var unsupported = JsonSerializer.SerializeToUtf8Bytes(
            new CustomScreenPackage(99, CustomScreenPackages.Format, source),
            JsonOptions.Default);
        Assert.False(CustomScreenPackages.TryRead(unsupported, out _, out var versionError));
        Assert.Contains("unsupported", versionError, StringComparison.OrdinalIgnoreCase);

        Assert.False(CustomScreenPackages.TryRead("not json"u8, out _, out var jsonError));
        Assert.Contains("valid JSON", jsonError, StringComparison.OrdinalIgnoreCase);

        Assert.False(CustomScreenPackages.TryRead(
            new byte[CustomScreenLimits.MaxStoreBytes + 1],
            out _,
            out var sizeError));
        Assert.Contains("too large", sizeError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadAcceptsOnlyTheExactCurrentJsonContract()
    {
        var source = CustomScreenService.CreateDraft();
        var json = JsonSerializer.Serialize(
            new CustomScreenPackage(
                CustomScreenPackages.CurrentPackageVersion,
                CustomScreenPackages.Format,
                source),
            CustomScreenJson.Exact);

        Assert.False(CustomScreenPackages.TryRead(
            System.Text.Encoding.UTF8.GetBytes(json.Replace("\"screen\"", "\"Screen\"", StringComparison.Ordinal)),
            out _,
            out var casingError));
        Assert.Contains("valid JSON", casingError, StringComparison.OrdinalIgnoreCase);

        Assert.False(CustomScreenPackages.TryRead(
            System.Text.Encoding.UTF8.GetBytes(json.Replace("\"format\":", "\"unknown\": true, \"format\":", StringComparison.Ordinal)),
            out _,
            out var unknownFieldError));
        Assert.Contains("valid JSON", unknownFieldError, StringComparison.OrdinalIgnoreCase);

        var assigned = source with { AssignedClientIds = ["phone-a"] };
        var assignedBytes = JsonSerializer.SerializeToUtf8Bytes(
            new CustomScreenPackage(
                CustomScreenPackages.CurrentPackageVersion,
                CustomScreenPackages.Format,
                assigned),
            CustomScreenJson.Exact);
        Assert.False(CustomScreenPackages.TryRead(assignedBytes, out _, out var assignmentError));
        Assert.Contains("cannot contain device assignments", assignmentError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportDetectsMatchingPortableContentDespiteRegeneratedIds()
    {
        var store = new InMemoryCustomScreenStore();
        var service = new CustomScreenService(store, new FakeAppLaunchService());
        var existing = CustomScreenService.CreateDraft();
        Assert.True(service.TrySave(existing, out _, out var saveError), saveError);
        Assert.True(CustomScreenPackages.TryRead(
            CustomScreenPackages.Serialize(existing),
            out var inspection,
            out var readError), readError);

        Assert.NotNull(service.FindPortableDuplicate(inspection!));
        Assert.False(service.TryImport(inspection!, out _, out var importError));
        Assert.Contains("already", importError, StringComparison.OrdinalIgnoreCase);
        Assert.Single(service.GetAll());

        Assert.True(service.TryImport(
            inspection!,
            allowPortableDuplicate: true,
            out var imported,
            out importError), importError);
        Assert.Equal(2, service.GetAll().Count);
        Assert.NotEqual(existing.Id, imported!.Id);
        Assert.Empty(imported.AssignedClientIds);
    }

    [Fact]
    public void ReviewUsesPlainAssignmentGuidanceAndDuplicateWarning()
    {
        var source = CustomScreenService.CreateDraft();
        Assert.True(CustomScreenPackages.TryRead(
            CustomScreenPackages.Serialize(source),
            out var inspection,
            out var error), error);

        var review = CustomScreenPackages.ReviewText(inspection!);
        Assert.Contains("Device assignments: none\n", review);
        Assert.Contains("Assign this screen to a device after import.", review);
        Assert.DoesNotContain("(this screen", review);

        var duplicateReview = CustomScreenPackages.DuplicateReviewText(
            inspection!,
            source);
        Assert.Contains("already in your library", duplicateReview);
        Assert.Contains("Import another copy?", duplicateReview);
    }
}
