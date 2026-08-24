using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host.Tests;

public sealed class UpdateServiceTests : IsolatedHostSettingsTest
{
    [Fact]
    public void EligibilityUsesInstalledDirectoryAndQuotedModifyPath()
    {
        using var install = new UpdateTemporaryDirectory();
        using var maintenance = new UpdateTemporaryDirectory();
        var modifyInstaller = Path.Combine(maintenance.Path, "VolturaAir-Modify.exe");
        File.WriteAllBytes(modifyInstaller, []);

        Assert.True(UpdatePolicy.IsInstalledHost(
            install.Path,
            install.Path,
            $"\"{modifyInstaller}\"",
            out var selected));
        Assert.Equal(modifyInstaller, selected);
        Assert.False(UpdatePolicy.IsInstalledHost(null, install.Path, modifyInstaller, out _));
        Assert.False(UpdatePolicy.IsInstalledHost(install.Path, maintenance.Path, modifyInstaller, out _));
        Assert.False(UpdatePolicy.IsInstalledHost(install.Path, install.Path, Path.Combine(maintenance.Path, "missing.exe"), out _));
        Assert.Equal(
            "VolturaAir-Setup-1.0.10-win-x64.exe",
            UpdatePolicy.SelectInstallerName(new Version(1, 0, 10), "VolturaAir-Setup.exe"));
        Assert.Equal(
            "VolturaAir-Setup-1.0.10-win-x64-full.exe",
            UpdatePolicy.SelectInstallerName(new Version(1, 0, 10), "VolturaAir-Setup-full.exe"));
    }

    [Theory]
    [InlineData("--isolated-test-mode")]
    [InlineData("--site-screenshot-mode")]
    [InlineData("--installer-health-check")]
    public void SpecialModesAreIneligible(string argument)
    {
        Assert.True(UpdatePolicy.IsSpecialExecution([argument], developmentSupervisor: false));
        Assert.True(UpdatePolicy.IsSpecialExecution([], developmentSupervisor: true));
    }

    [Fact]
    public async Task DisabledAndIneligibleServicesCreateNoIdleWorkOrNetworkClient()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        var clientCreations = 0;
        await using var disabled = new UpdateService(
            new PairingManager(store.Store),
            [],
            eligibleOverride: true,
            clientFactory: () =>
            {
                clientCreations++;
                return new System.Net.Http.HttpClient();
            });

        Assert.False(disabled.HasNetworkClient);
        Assert.False(disabled.HasPairingSubscription);
        Assert.False(disabled.HasScheduledWork);
        Assert.Equal(0, clientCreations);

        await using var ineligible = new UpdateService(new PairingManager(store.Store), [], eligibleOverride: false);
        Assert.False(ineligible.HasNetworkClient);
        Assert.False(ineligible.HasPairingSubscription);
        Assert.False(ineligible.HasScheduledWork);
    }

    [Fact]
    public async Task RapidAutomaticDownloadChangesKeepOneScheduledWorker()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        var running = 0;
        var maximumRunning = 0;

        async Task Schedule(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref running);
            UpdateMaximum(ref maximumRunning, current);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Interlocked.Decrement(ref running); }
        }

        await using var updates = new UpdateService(
            new PairingManager(store.Store),
            [],
            eligibleOverride: true,
            scheduleAutomaticWork: Schedule);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(index % 2 == 0))));
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        await UpdateTestSupport.WaitUntilAsync(() => Volatile.Read(ref running) == 1);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref maximumRunning));
    }

    [Fact]
    public async Task MetadataReadStopsAtTheConfiguredCap()
    {
        await using var stream = new MemoryStream(new byte[17]);
        await Assert.ThrowsAsync<IOException>(() => UpdatePackageStager.ReadCappedAsync(
            stream,
            16,
            TestContext.Current.CancellationToken));
    }

    private static void UpdateMaximum(ref int maximum, int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (current <= observed || Interlocked.CompareExchange(ref maximum, current, observed) == observed) return;
        }
    }
}
