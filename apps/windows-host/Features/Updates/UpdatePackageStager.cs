using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace VolturaAir.Host.Features.Updates;

internal enum UpdateStageStatus { Ready, Deferred, Cancelled, Failed }

internal sealed record UpdateReadyPackage(
    Version Version,
    string InstallerName,
    string SignatureName,
    long Size,
    string Hash);

internal readonly record struct UpdateStageResult(UpdateStageStatus Status, UpdateReadyPackage? ReadyPackage = null);

/// <summary>Owns the bounded, authenticated remote-package-to-pending-directory transition.</summary>
internal sealed class UpdatePackageStager(
    System.Net.Http.HttpClient client,
    PairingManager pairingManager,
    string installerName,
    Func<byte[], byte[], bool> verifyManifest,
    string pendingDirectory)
{
    internal const long MaxManifestBytes = 16 * 1024;
    internal const long MaxSignatureBytes = 4 * 1024;
    private const long MaxInstallerBytes = 200L * 1024 * 1024;

    internal async Task<UpdateStageResult> StageAsync(
        JsonElement releaseAssets,
        Version version,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryFindReleaseAssets(releaseAssets, version, installerName, out var assets))
            {
                return new(UpdateStageStatus.Failed);
            }

            if (pairingManager.HasActiveController)
            {
                return new(UpdateStageStatus.Deferred);
            }

            var manifest = await DownloadSmallAsync(assets.Manifest.Url, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
            var signature = await DownloadSmallAsync(assets.Signature.Url, MaxSignatureBytes, cancellationToken).ConfigureAwait(false);
            if (!verifyManifest(manifest, signature) ||
                !TryReadPackage(manifest, assets.Installer, version, installerName, out var package))
            {
                return new(UpdateStageStatus.Failed);
            }

            if (pairingManager.HasActiveController)
            {
                return new(UpdateStageStatus.Deferred);
            }

            return await DownloadAndPublishAsync(
                package,
                assets.Installer.Url,
                manifest,
                signature,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(pairingManager.HasActiveController ? UpdateStageStatus.Deferred : UpdateStageStatus.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            return new(UpdateStageStatus.Failed);
        }
    }

    internal static bool TryFindReleaseAssets(
        JsonElement releaseAssets,
        Version version,
        string selectedInstallerName,
        out ReleaseAssets assets)
    {
        assets = default;
        if (releaseAssets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var allAssets = releaseAssets.EnumerateArray().ToArray();
        var prefix = $"VolturaAir-Update-{version.ToString(3)}";
        if (!TryFindSingleAsset(allAssets, prefix + ".json", out var manifest) ||
            !TryFindSingleAsset(allAssets, prefix + ".sig", out var signature) ||
            !TryFindSingleAsset(allAssets, selectedInstallerName, out var installer) ||
            !TryReadReleaseAsset(manifest, requireSize: false, out var manifestAsset) ||
            !TryReadReleaseAsset(signature, requireSize: false, out var signatureAsset) ||
            !TryReadReleaseAsset(installer, requireSize: true, out var installerAsset))
        {
            return false;
        }

        assets = new(manifestAsset, signatureAsset, installerAsset);
        return true;
    }

    internal static bool TryReadPackage(
        byte[] manifest,
        ReleaseAsset apiInstaller,
        Version expectedVersion,
        string selectedInstallerName,
        out UpdateReadyPackage package)
    {
        package = null!;
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schema", out var schema) ||
            !schema.TryGetInt32(out var schemaValue) ||
            schemaValue != 1 ||
            !root.TryGetProperty("version", out var versionElement) ||
            versionElement.GetString() != expectedVersion.ToString(3) ||
            !root.TryGetProperty("assets", out var manifestAssets) ||
            manifestAssets.ValueKind != JsonValueKind.Array ||
            !TryFindSingleAsset(manifestAssets.EnumerateArray(), selectedInstallerName, out var selected) ||
            !selected.TryGetProperty("size", out var sizeElement) ||
            !sizeElement.TryGetInt64(out var size) ||
            !selected.TryGetProperty("sha256", out var hashElement))
        {
            return false;
        }

        var hash = hashElement.GetString();
        if (!string.Equals(apiInstaller.Name, selectedInstallerName, StringComparison.Ordinal) ||
            size <= 0 || size > MaxInstallerBytes || size != apiInstaller.Size || !IsLowerSha256(hash))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(apiInstaller.Digest) &&
            !string.Equals(apiInstaller.Digest, $"sha256:{hash}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        package = new(
            expectedVersion,
            selectedInstallerName,
            $"VolturaAir-Update-{expectedVersion.ToString(3)}.sig",
            size,
            hash!);
        return true;
    }

    internal static bool TryReadRestoredPackage(
        byte[] manifest,
        string selectedInstallerName,
        out UpdateReadyPackage package)
    {
        package = null!;
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        if (!root.TryGetProperty("version", out var versionElement) ||
            !UpdateService.TryParseVersion(versionElement.GetString(), requireVPrefix: false, out var version))
        {
            return false;
        }

        var apiAsset = new ReleaseAsset(null, selectedInstallerName, ReadManifestSize(root, selectedInstallerName), null);
        return apiAsset.Size > 0 && TryReadPackage(manifest, apiAsset, version, selectedInstallerName, out package);
    }

    private async Task<UpdateStageResult> DownloadAndPublishAsync(
        UpdateReadyPackage package,
        string? installerUrl,
        byte[] manifest,
        byte[] signature,
        CancellationToken cancellationToken)
    {
        var safePending = GetSafePendingDirectory();
        Directory.CreateDirectory(safePending);
        if (new DriveInfo(Path.GetPathRoot(safePending)!).AvailableFreeSpace < package.Size + 32L * 1024 * 1024)
        {
            return new(UpdateStageStatus.Failed);
        }

        var partial = Path.Combine(safePending, package.InstallerName + ".partial");
        var installer = Path.Combine(safePending, package.InstallerName);
        var signaturePath = Path.Combine(safePending, package.SignatureName);
        var pendingManifest = Path.Combine(safePending, "manifest.json.pending");
        try
        {
            await DownloadInstallerAsync(package, installerUrl, partial, cancellationToken).ConfigureAwait(false);
            File.Move(partial, installer, overwrite: true);
            await File.WriteAllBytesAsync(signaturePath, signature, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(pendingManifest, manifest, cancellationToken).ConfigureAwait(false);
            File.Move(pendingManifest, Path.Combine(safePending, "manifest.json"), overwrite: true);
            CleanupSupersededFiles(safePending, package);
            return new(UpdateStageStatus.Ready, package);
        }
        catch (OperationCanceledException)
        {
            CleanupFailedCandidate(partial, installer, signaturePath, pendingManifest);
            return new(pairingManager.HasActiveController ? UpdateStageStatus.Deferred : UpdateStageStatus.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CleanupFailedCandidate(partial, installer, signaturePath, pendingManifest);
            return new(UpdateStageStatus.Failed);
        }
    }

    private async Task DownloadInstallerAsync(
        UpdateReadyPackage package,
        string? installerUrl,
        string partial,
        CancellationToken cancellationToken)
    {
        await using var source = await OpenAssetAsync(installerUrl, cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            partial,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            65536,
            useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (pairingManager.HasActiveController) throw new OperationCanceledException(cancellationToken);
            received += read;
            if (received > package.Size) throw new IOException("Update package exceeds signed size.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (received != package.Size || !string.Equals(actualHash, package.Hash, StringComparison.Ordinal))
        {
            throw new IOException("Update package hash mismatch.");
        }
    }

    private async Task<byte[]> DownloadSmallAsync(string? url, long cap, CancellationToken cancellationToken)
    {
        await using var stream = await OpenAssetAsync(url, cancellationToken).ConfigureAwait(false);
        return await ReadCappedAsync(stream, cap, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadCappedAsync(Stream stream, long cap, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > cap) throw new IOException("Update response is too large.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private async Task<Stream> OpenAssetAsync(string? value, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var source) ||
            source.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(source.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !source.AbsolutePath.StartsWith("/voltura/voltura-air/releases/download/", StringComparison.Ordinal))
        {
            throw new IOException("Unexpected update asset URL.");
        }

        using var redirect = await client.GetAsync(source, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (redirect.Headers.Location is null ||
            redirect.StatusCode is not HttpStatusCode.Redirect and not HttpStatusCode.MovedPermanently and not HttpStatusCode.Found)
        {
            throw new IOException("Update asset redirect was rejected.");
        }

        var target = redirect.Headers.Location.IsAbsoluteUri ? redirect.Headers.Location : new Uri(source, redirect.Headers.Location);
        if (target.Scheme != Uri.UriSchemeHttps ||
            (target.Host != "objects.githubusercontent.com" && target.Host != "release-assets.githubusercontent.com"))
        {
            throw new IOException("Update asset redirect was rejected.");
        }

        var response = await client.GetAsync(target, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            return new ResponseStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private string GetSafePendingDirectory()
    {
        var pending = Path.GetFullPath(pendingDirectory);
        var updatesRoot = Path.GetFullPath(Path.GetDirectoryName(pending) ?? throw new IOException("Invalid update path."));
        if (!IsWithin(pending, updatesRoot)) throw new IOException("Invalid update path.");
        return pending;
    }

    private static bool TryFindSingleAsset(IEnumerable<JsonElement> assets, string name, out JsonElement asset)
    {
        var matches = assets.Where(item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("name", out var itemName) &&
            itemName.GetString() == name).Take(2).ToArray();
        asset = matches.Length == 1 ? matches[0] : default;
        return matches.Length == 1;
    }

    private static bool TryReadReleaseAsset(JsonElement element, bool requireSize, out ReleaseAsset asset)
    {
        asset = default;
        if (!element.TryGetProperty("name", out var nameElement) ||
            !element.TryGetProperty("browser_download_url", out var urlElement))
        {
            return false;
        }

        var name = nameElement.GetString();
        var url = urlElement.GetString();
        long size = 0;
        if (requireSize && (!element.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out size)))
        {
            return false;
        }

        var digest = element.TryGetProperty("digest", out var digestElement) && digestElement.ValueKind != JsonValueKind.Null
            ? digestElement.GetString()
            : null;
        asset = new(url, name ?? string.Empty, size, digest);
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url);
    }

    private static long ReadManifestSize(JsonElement root, string selectedInstallerName)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
            !TryFindSingleAsset(assets.EnumerateArray(), selectedInstallerName, out var selected) ||
            !selected.TryGetProperty("size", out var size) || !size.TryGetInt64(out var value))
        {
            return 0;
        }

        return value;
    }

    private static bool IsLowerSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private void CleanupFailedCandidate(params string[] paths)
    {
        foreach (var path in paths) TryDeleteOwned(path);
    }

    private void CleanupSupersededFiles(string safePending, UpdateReadyPackage package)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(safePending, "manifest.json"),
            Path.Combine(safePending, package.InstallerName),
            Path.Combine(safePending, package.SignatureName)
        };
        foreach (var path in Directory.EnumerateFiles(safePending))
        {
            var name = Path.GetFileName(path);
            var updaterOwned = name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
                name == "manifest.json.pending" ||
                name.StartsWith("VolturaAir-Setup-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("VolturaAir-Update-", StringComparison.OrdinalIgnoreCase);
            if (updaterOwned && !keep.Contains(path)) TryDeleteOwned(path);
        }
    }

    private void TryDeleteOwned(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsWithin(fullPath, GetSafePendingDirectory())) return;
        try { File.Delete(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsWithin(string path, string root) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal readonly record struct ReleaseAsset(string? Url, string Name, long Size, string? Digest);
    internal readonly record struct ReleaseAssets(ReleaseAsset Manifest, ReleaseAsset Signature, ReleaseAsset Installer);

    private sealed class ResponseStream(Stream stream, System.Net.Http.HttpResponseMessage response) : Stream
    {
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => stream.Position = value; }
        public override void Flush() => stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
