using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host.Tests;

public sealed class UpdateServiceTests : IsolatedHostSettingsTest
{
    [Fact]
    public async Task RapidAutomaticDownloadChangesKeepOneScheduledWorker()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        var running = 0;
        var maximumRunning = 0;

        Task Schedule(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref running);
            UpdateMaximum(ref maximumRunning, current);
            return WaitForCancellationAsync(cancellationToken);
        }

        await using var updates = new UpdateService(
            new PairingManager(store.Store),
            [],
            eligibleOverride: true,
            scheduleAutomaticWork: Schedule);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(index % 2 == 0))));
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        await WaitUntilAsync(() => Volatile.Read(ref running) == 1);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref maximumRunning));

        async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Interlocked.Decrement(ref running); }
        }
    }

    [Fact]
    public async Task MetadataReadStopsAtTheConfiguredCap()
    {
        await using var stream = new MemoryStream(new byte[17]);

        await Assert.ThrowsAsync<IOException>(() => UpdatePackageStager.ReadCappedAsync(stream, 16, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishFailureAfterVerifiedNewerDownloadLeavesReadyUpdateUntouched()
    {
        using var directory = new TemporaryDirectory();
        var existingManifest = "existing manifest"u8.ToArray();
        var existingSignature = "existing signature"u8.ToArray();
        var existingInstaller = "existing installer"u8.ToArray();
        var newerInstaller = "new installer"u8.ToArray();
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var signaturePath = Path.Combine(directory.Path, "VolturaAir-Update-1.0.0.sig");
        var installerPath = Path.Combine(directory.Path, "VolturaAir-Setup-1.0.0-win-x64.exe");
        await File.WriteAllBytesAsync(manifestPath, existingManifest, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(signaturePath, existingSignature, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(installerPath, existingInstaller, TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(directory.Path, "manifest.json.pending"));

        using var store = new TempPairingStore();
        using var handler = new RecordingHandler(attempt => attempt switch
        {
            1 => RedirectToDownload(),
            2 => new System.Net.Http.HttpResponseMessage(global::System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent(newerInstaller)
            },
            _ => throw new InvalidOperationException("Unexpected update request.")
        });
        using var client = new System.Net.Http.HttpClient(handler);
        var state = UpdateState.Ready;
        var stager = new UpdatePackageStager(
            client,
            new PairingManager(store.Store),
            static () => "VolturaAir-Setup-2.0.0-win-x64.exe",
            static (_, _) => true,
            (next, _) => state = next);

        var packageType = typeof(UpdatePackageStager).GetNestedType(
            "Package",
            System.Reflection.BindingFlags.NonPublic) ?? throw new InvalidOperationException("Package type was not found.");
        var package = Activator.CreateInstance(
            packageType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [
                "VolturaAir-Setup-2.0.0-win-x64.exe",
                "https://github.com/voltura/voltura-air/releases/download/v2.0.0/VolturaAir-Setup-2.0.0-win-x64.exe",
                new Version(2, 0, 0),
                (long)newerInstaller.Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(newerInstaller)).ToLowerInvariant()],
            culture: null) ?? throw new InvalidOperationException("Package could not be created.");
        var stage = (Task)(typeof(UpdatePackageStager).GetMethod(
            "DownloadAndPublishAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(
                stager,
                [package, Array.Empty<byte>(), Array.Empty<byte>(), directory.Path, TestContext.Current.CancellationToken])
            ?? throw new InvalidOperationException("Staging method was not found."));

        await stage;

        Assert.Equal(UpdateState.Idle, state);
        Assert.Equal(existingManifest, await File.ReadAllBytesAsync(manifestPath, TestContext.Current.CancellationToken));
        Assert.Equal(existingSignature, await File.ReadAllBytesAsync(signaturePath, TestContext.Current.CancellationToken));
        Assert.Equal(existingInstaller, await File.ReadAllBytesAsync(installerPath, TestContext.Current.CancellationToken));
        Assert.Equal(newerInstaller, await File.ReadAllBytesAsync(Path.Combine(directory.Path, "VolturaAir-Setup-2.0.0-win-x64.exe"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(directory.Path, "VolturaAir-Update-2.0.0.sig")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "VolturaAir-Setup-2.0.0-win-x64.exe.partial")));

        static System.Net.Http.HttpResponseMessage RedirectToDownload()
        {
            var response = new System.Net.Http.HttpResponseMessage(global::System.Net.HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://objects.githubusercontent.com/voltura-air-update-test");
            return response;
        }
    }

    private static void UpdateMaximum(ref int maximum, int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (current <= observed || Interlocked.CompareExchange(ref maximum, current, observed) == observed) return;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("Scheduled worker did not settle.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VolturaAir-UpdateTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
