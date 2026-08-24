using System.Text.Json;
using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host.Tests;

public sealed class UpdatePackageStagerTests
{
    [Fact]
    public void RestoreRequiresValidSignatureInstallerExistenceAndSignedSize()
    {
        using var directory = new UpdateTemporaryDirectory();
        var pending = Path.Combine(directory.Path, "pending");
        var release = UpdateTestSupport.CreateRelease("1.0.10");
        UpdateTestSupport.WriteReadyPackage(pending, release);

        Assert.True(UpdatePolicy.TryRestoreReadyPackage(
            pending,
            _ => release.InstallerName,
            static (_, _) => true,
            out var ready));
        Assert.Equal(release.Installer.LongLength, ready.Size);

        File.Delete(Path.Combine(pending, release.InstallerName));
        Assert.False(UpdatePolicy.TryRestoreReadyPackage(
            pending,
            _ => release.InstallerName,
            static (_, _) => true,
            out _));

        File.WriteAllBytes(Path.Combine(pending, release.InstallerName), [1]);
        Assert.False(UpdatePolicy.TryRestoreReadyPackage(
            pending,
            _ => release.InstallerName,
            static (_, _) => true,
            out _));
    }

    [Fact]
    public async Task SuccessfulReplacementRemovesSupersededUpdaterFiles()
    {
        using var directory = new UpdateTemporaryDirectory();
        using var store = new TempPairingStore();
        var pending = Path.Combine(directory.Path, "pending");
        var old = UpdateTestSupport.CreateRelease("1.0.10");
        UpdateTestSupport.WriteReadyPackage(pending, old);
        File.WriteAllBytes(Path.Combine(pending, "stale.partial"), [1]);
        var current = UpdateTestSupport.CreateRelease("1.0.11");
        using var handler = new UpdateHttpHandler(current);
        using var client = new System.Net.Http.HttpClient(handler);
        var stager = new UpdatePackageStager(
            client,
            new PairingManager(store.Store),
            current.InstallerName,
            static (_, _) => true,
            pending);

        var result = await stager.StageAsync(
            UpdateTestSupport.GetAssets(current),
            new Version(1, 0, 11),
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStageStatus.Ready, result.Status);
        Assert.False(File.Exists(Path.Combine(pending, old.InstallerName)));
        Assert.False(File.Exists(Path.Combine(pending, "VolturaAir-Update-1.0.10.sig")));
        Assert.False(File.Exists(Path.Combine(pending, "stale.partial")));
        Assert.True(File.Exists(Path.Combine(pending, current.InstallerName)));
        Assert.True(File.Exists(Path.Combine(pending, "VolturaAir-Update-1.0.11.sig")));
    }

    [Fact]
    public async Task PublishFailureLeavesExistingReadySetUntouchedAndRemovesCandidate()
    {
        using var directory = new UpdateTemporaryDirectory();
        using var store = new TempPairingStore();
        var pending = Path.Combine(directory.Path, "pending");
        var old = UpdateTestSupport.CreateRelease("1.0.10");
        UpdateTestSupport.WriteReadyPackage(pending, old);
        var oldManifest = File.ReadAllBytes(Path.Combine(pending, "manifest.json"));
        Directory.CreateDirectory(Path.Combine(pending, "manifest.json.pending"));
        var candidate = UpdateTestSupport.CreateRelease("1.0.11");
        using var handler = new UpdateHttpHandler(candidate);
        using var client = new System.Net.Http.HttpClient(handler);
        var stager = new UpdatePackageStager(
            client,
            new PairingManager(store.Store),
            candidate.InstallerName,
            static (_, _) => true,
            pending);

        var result = await stager.StageAsync(
            UpdateTestSupport.GetAssets(candidate),
            new Version(1, 0, 11),
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStageStatus.Failed, result.Status);
        Assert.Equal(oldManifest, File.ReadAllBytes(Path.Combine(pending, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(pending, old.InstallerName)));
        Assert.True(File.Exists(Path.Combine(pending, "VolturaAir-Update-1.0.10.sig")));
        Assert.False(File.Exists(Path.Combine(pending, candidate.InstallerName)));
        Assert.False(File.Exists(Path.Combine(pending, "VolturaAir-Update-1.0.11.sig")));
    }

    [Theory]
    [InlineData("v1.0.9", true)]
    [InlineData("1.0.9", false)]
    [InlineData("v1.0", false)]
    [InlineData("v1.0.9.0", false)]
    [InlineData("v01.0.9", false)]
    [InlineData("v1.0.9-beta", false)]
    public void LatestVersionRequiresExactThreePartVSemver(string value, bool expected)
    {
        Assert.Equal(expected, UpdatePolicy.TryParseVersion(value, requireVPrefix: true, out _));
    }

    [Fact]
    public void DuplicateAssetsAndApiManifestDisagreementAreRejected()
    {
        var release = UpdateTestSupport.CreateRelease("1.0.10");
        var missingAsset = JsonSerializer.SerializeToElement(
            UpdateTestSupport.GetAssets(release).EnumerateArray().Take(2).Select(asset => asset.Clone()).ToArray());
        Assert.False(UpdatePackageStager.TryFindReleaseAssets(
            missingAsset,
            new Version(1, 0, 10),
            release.InstallerName,
            out _));
        Assert.False(UpdatePackageStager.TryFindReleaseAssets(
            UpdateTestSupport.GetAssets(release, duplicateInstaller: true),
            new Version(1, 0, 10),
            release.InstallerName,
            out _));

        var wrongSize = new UpdatePackageStager.ReleaseAsset(
            "https://example.test/installer",
            release.InstallerName,
            release.Installer.LongLength + 1,
            $"sha256:{release.Hash}");
        Assert.False(UpdatePackageStager.TryReadPackage(
            release.Manifest,
            wrongSize,
            new Version(1, 0, 10),
            release.InstallerName,
            out _));

        var wrongName = wrongSize with { Name = "another-installer.exe", Size = release.Installer.LongLength };
        Assert.False(UpdatePackageStager.TryReadPackage(
            release.Manifest,
            wrongName,
            new Version(1, 0, 10),
            release.InstallerName,
            out _));

        var wrongDigest = wrongSize with { Size = release.Installer.LongLength, Digest = "sha256:00" };
        Assert.False(UpdatePackageStager.TryReadPackage(
            release.Manifest,
            wrongDigest,
            new Version(1, 0, 10),
            release.InstallerName,
            out _));

        using var document = JsonDocument.Parse(release.Manifest);
        var root = document.RootElement;
        var unknownSchema = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 2,
            version = release.Version,
            assets = root.GetProperty("assets")
        });
        var matchingApi = wrongSize with { Size = release.Installer.LongLength, Digest = $"sha256:{release.Hash}" };
        Assert.False(UpdatePackageStager.TryReadPackage(
            unknownSchema,
            matchingApi,
            new Version(1, 0, 10),
            release.InstallerName,
            out _));
    }
}
