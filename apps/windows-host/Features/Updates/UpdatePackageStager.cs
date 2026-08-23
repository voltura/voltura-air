using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace VolturaAir.Host.Features.Updates;

/// <summary>Owns the bounded, authenticated remote-package-to-pending-directory transition.</summary>
internal sealed class UpdatePackageStager(
    System.Net.Http.HttpClient client,
    PairingManager pairingManager,
    Func<string> selectInstallerName,
    Func<byte[], byte[], bool> verifyManifest,
    Action<UpdateState, string?> setState)
{
    private const long MaxManifestBytes = 16 * 1024;
    private const long MaxSignatureBytes = 4 * 1024;
    private const long MaxInstallerBytes = 200L * 1024 * 1024;

    internal async Task StageAsync(JsonElement releaseAssets, Version version, CancellationToken cancellationToken)
    {
        if (!TryFindReleaseAssets(releaseAssets, version, out var assets))
        {
            setState(UpdateState.Idle, null);
            return;
        }

        var manifest = await DownloadSmallAsync(assets.ManifestUrl, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
        var signature = await DownloadSmallAsync(assets.SignatureUrl, MaxSignatureBytes, cancellationToken).ConfigureAwait(false);
        if (!verifyManifest(manifest, signature) || !TryReadPackage(manifest, assets, version, out var package))
        {
            setState(UpdateState.Idle, null);
            return;
        }

        if (pairingManager.HasActiveController)
        {
            setState(UpdateState.WaitingForDevices, version.ToString());
            return;
        }

        var pendingDirectory = GetPendingDirectory();
        if (new DriveInfo(Path.GetPathRoot(pendingDirectory)!).AvailableFreeSpace < package.Size + 32L * 1024 * 1024)
        {
            setState(UpdateState.Idle, null);
            return;
        }

        setState(UpdateState.Downloading, version.ToString());
        await DownloadAndPublishAsync(package, manifest, signature, pendingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private bool TryFindReleaseAssets(JsonElement releaseAssets, Version version, out ReleaseAssets assets)
    {
        var prefix = $"VolturaAir-Update-{version}";
        var allAssets = releaseAssets.EnumerateArray().ToArray();
        var manifest = FindAsset(allAssets, prefix + ".json");
        var signature = FindAsset(allAssets, prefix + ".sig");

        if (manifest.ValueKind == JsonValueKind.Undefined || signature.ValueKind == JsonValueKind.Undefined)
        {
            assets = default;
            return false;
        }

        var installerName = selectInstallerName();
        var installer = FindAsset(allAssets, installerName);
        if (installer.ValueKind == JsonValueKind.Undefined)
        {
            assets = default;
            return false;
        }

        assets = new ReleaseAssets(
            manifest.GetProperty("browser_download_url").GetString(),
            signature.GetProperty("browser_download_url").GetString(),
            installer.GetProperty("browser_download_url").GetString(),
            installerName);
        return true;
    }

    private static JsonElement FindAsset(IEnumerable<JsonElement> assets, string name) =>
        assets.SingleOrDefault(asset => asset.GetProperty("name").GetString() == name);

    private static bool TryReadPackage(byte[] manifest, ReleaseAssets assets, Version version, out Package package)
    {
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var manifestPackage = FindAsset(root.GetProperty("assets").EnumerateArray(), assets.InstallerName);

        if (root.GetProperty("schema").GetInt32() != 1 ||
            root.GetProperty("version").GetString() != version.ToString() ||
            manifestPackage.ValueKind == JsonValueKind.Undefined)
        {
            package = default;
            return false;
        }

        var size = manifestPackage.GetProperty("size").GetInt64();
        var hash = manifestPackage.GetProperty("sha256").GetString();
        if (size <= 0 || size > MaxInstallerBytes || string.IsNullOrWhiteSpace(hash))
        {
            package = default;
            return false;
        }

        package = new Package(assets.InstallerName, assets.InstallerUrl, version, size, hash);
        return true;
    }

    private async Task DownloadAndPublishAsync(
        Package package,
        byte[] manifest,
        byte[] signature,
        string pendingDirectory,
        CancellationToken cancellationToken)
    {
        var partial = Path.Combine(pendingDirectory, package.Name + ".partial");
        try
        {
            await DownloadInstallerAsync(package, partial, cancellationToken).ConfigureAwait(false);
            Publish(package, partial, manifest, signature, pendingDirectory);
            setState(UpdateState.Ready, null);
        }
        catch
        {
            TryDelete(partial);
            setState(UpdateState.Idle, null);
        }
    }

    private async Task DownloadInstallerAsync(Package package, string partial, CancellationToken cancellationToken)
    {
        await using var source = await OpenAssetAsync(package.Url, cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
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

        if (received != package.Size ||
            !string.Equals(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), package.Hash, StringComparison.Ordinal))
        {
            throw new IOException("Update package hash mismatch.");
        }
    }

    private static void Publish(Package package, string partial, byte[] manifest, byte[] signature, string pendingDirectory)
    {
        File.Move(partial, Path.Combine(pendingDirectory, package.Name), overwrite: true);
        File.WriteAllBytes(Path.Combine(pendingDirectory, $"VolturaAir-Update-{package.Version}.sig"), signature);
        var pendingManifest = Path.Combine(pendingDirectory, "manifest.json.pending");
        File.WriteAllBytes(pendingManifest, manifest);
        File.Move(pendingManifest, Path.Combine(pendingDirectory, "manifest.json"), overwrite: true);
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

    private static string GetPendingDirectory()
    {
        var updates = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura Air", "Updates");
        var pending = Path.Combine(updates, "pending");
        Directory.CreateDirectory(pending);
        return pending;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }

    private readonly record struct ReleaseAssets(string? ManifestUrl, string? SignatureUrl, string? InstallerUrl, string InstallerName);
    private readonly record struct Package(string Name, string? Url, Version Version, long Size, string Hash);

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
