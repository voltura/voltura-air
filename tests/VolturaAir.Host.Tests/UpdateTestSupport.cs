using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace VolturaAir.Host.Tests;

internal static class UpdateTestSupport
{
    internal static UpdateTestRelease CreateRelease(string version, byte[]? installer = null)
    {
        var installerBytes = installer ?? Encoding.UTF8.GetBytes($"installer-{version}");
        var installerName = $"VolturaAir-Setup-{version}-win-x64.exe";
        var hash = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1,
            version,
            assets = new[] { new { name = installerName, size = installerBytes.LongLength, sha256 = hash } }
        });
        return new(version, installerName, installerBytes, manifest, [1, 2, 3], hash);
    }

    internal static void WriteReadyPackage(string pendingDirectory, UpdateTestRelease release, byte[]? installer = null)
    {
        Directory.CreateDirectory(pendingDirectory);
        File.WriteAllBytes(Path.Combine(pendingDirectory, "manifest.json"), release.Manifest);
        File.WriteAllBytes(Path.Combine(pendingDirectory, $"VolturaAir-Update-{release.Version}.sig"), release.Signature);
        File.WriteAllBytes(Path.Combine(pendingDirectory, release.InstallerName), installer ?? release.Installer);
    }

    internal static JsonElement GetAssets(UpdateTestRelease release, bool duplicateInstaller = false)
    {
        using var document = JsonDocument.Parse(release.Metadata(duplicateInstaller));
        return document.RootElement.GetProperty("assets").Clone();
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for updater state.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}

internal sealed record UpdateTestRelease(
    string Version,
    string InstallerName,
    byte[] Installer,
    byte[] Manifest,
    byte[] Signature,
    string Hash)
{
    internal string Metadata(bool duplicateInstaller = false)
    {
        var assets = new List<object>
        {
            Asset($"VolturaAir-Update-{Version}.json", Manifest.LongLength, null),
            Asset($"VolturaAir-Update-{Version}.sig", Signature.LongLength, null),
            Asset(InstallerName, Installer.LongLength, $"sha256:{Hash}")
        };
        if (duplicateInstaller) assets.Add(Asset(InstallerName, Installer.LongLength, $"sha256:{Hash}"));
        return JsonSerializer.Serialize(new { prerelease = false, draft = false, tag_name = $"v{Version}", assets });
    }

    private object Asset(string name, long size, string? digest) => new
    {
        name,
        size,
        digest,
        browser_download_url = $"https://github.com/voltura/voltura-air/releases/download/v{Version}/{name}"
    };
}

internal sealed class UpdateHttpHandler(UpdateTestRelease release) : HttpMessageHandler
{
    private int _firstInstaller = 1;

    internal UpdateTestRelease Release { get; set; } = release;
    internal bool BlockFirstInstaller { get; set; }
    internal HttpStatusCode ApiStatusCode { get; set; } = HttpStatusCode.OK;
    internal int ApiRequests { get; private set; }
    internal int AssetResponses { get; private set; }
    internal TaskCompletionSource InstallerReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ContinueInstallerRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
        if (uri.Host == "api.github.com")
        {
            ApiRequests++;
            return Task.FromResult(new HttpResponseMessage(ApiStatusCode)
            {
                Content = ApiStatusCode == HttpStatusCode.OK ? new StringContent(Release.Metadata()) : null
            });
        }

        var name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (uri.Host == "github.com")
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri($"https://objects.githubusercontent.com/{Uri.EscapeDataString(name)}");
            return Task.FromResult(redirect);
        }

        if (uri.Host != "objects.githubusercontent.com")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        AssetResponses++;
        var bytes = name switch
        {
            var value when value.EndsWith(".json", StringComparison.Ordinal) => Release.Manifest,
            var value when value.EndsWith(".sig", StringComparison.Ordinal) => Release.Signature,
            _ => Release.Installer
        };
        HttpContent content = BlockFirstInstaller && name == Release.InstallerName && Interlocked.Exchange(ref _firstInstaller, 0) == 1
            ? new StreamContent(new GatedReadStream(bytes, InstallerReadStarted, ContinueInstallerRead))
            : new ByteArrayContent(bytes);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class GatedReadStream(
        byte[] bytes,
        TaskCompletionSource started,
        TaskCompletionSource continueRead) : Stream
    {
        private int _position;
        private int _gated;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _gated, 1) == 0)
            {
                started.TrySetResult();
                await continueRead.Task.WaitAsync(cancellationToken);
            }
            if (_position == bytes.Length) return 0;
            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class UpdateTemporaryDirectory : IDisposable
{
    internal UpdateTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VolturaAir-UpdateTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
