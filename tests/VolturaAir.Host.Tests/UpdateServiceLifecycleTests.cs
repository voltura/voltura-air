using System.Net;
using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host.Tests;

public sealed class UpdateServiceLifecycleTests : IsolatedHostSettingsTest
{
    [Fact]
    public async Task ManualChecksReportUpToDateAndFailure()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var release = UpdateTestSupport.CreateRelease("1.0.9");
        var handler = new UpdateHttpHandler(release);
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(store, directory, handler);
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);

        await service.CheckForUpdatesAsync(manual: true, TestContext.Current.CancellationToken);
        handler.ApiStatusCode = HttpStatusCode.ServiceUnavailable;
        await service.CheckForUpdatesAsync(manual: true, TestContext.Current.CancellationToken);

        Assert.Equal([UpdateNotificationKind.UpToDate, UpdateNotificationKind.CheckFailed], notifications);
        Assert.False(service.HasNetworkClient);
    }

    [Fact]
    public async Task AutomaticDiscoveryFailureIsSilent()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var handler = new UpdateHttpHandler(UpdateTestSupport.CreateRelease("1.0.10"))
        {
            ApiStatusCode = HttpStatusCode.ServiceUnavailable
        };
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(store, directory, handler, schedule: static _ => Task.CompletedTask);
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);

        await service.CheckForUpdatesAsync(manual: false, TestContext.Current.CancellationToken);

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task AutomaticMetadataWaitsForControllerDisconnectWithoutNetwork()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        using var connection = manager.TrackConnection("client-a");
        var handler = new UpdateHttpHandler(UpdateTestSupport.CreateRelease("1.0.9"));
        await using var service = CreateService(store, directory, handler, manager, static _ => Task.CompletedTask);

        var check = service.CheckForUpdatesAsync(manual: false, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(0, handler.ApiRequests);

        connection.Dispose();
        await check;
        Assert.Equal(1, handler.ApiRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadCancelsCleansAndResumesAfterControllerDisconnect(bool automatic)
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(automatic);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        var release = UpdateTestSupport.CreateRelease("1.0.10");
        var handler = new UpdateHttpHandler(release) { BlockFirstInstaller = true };
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(
            store,
            directory,
            handler,
            manager,
            automatic ? static _ => Task.CompletedTask : null);
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);

        var check = service.CheckForUpdatesAsync(manual: !automatic, TestContext.Current.CancellationToken);
        await handler.InstallerReadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var connection = manager.TrackConnection("client-a");
        await check;

        Assert.Equal(UpdateState.WaitingForDevices, service.State);
        Assert.False(File.Exists(Path.Combine(directory.Path, "pending", release.InstallerName + ".partial")));
        if (!automatic) Assert.Contains(UpdateNotificationKind.WaitingForDevices, notifications);

        connection.Dispose();
        await UpdateTestSupport.WaitUntilAsync(() => service.State == UpdateState.Ready);
        Assert.Contains(UpdateNotificationKind.Ready, notifications);
        Assert.Equal(automatic, AppUpdateSettings.AutomaticUpdateDownloadsEnabled());
    }

    [Fact]
    public async Task ReadyUpdateStaysActionableAndApplyCancelsNewerCandidateWithoutNetwork()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        var pending = Path.Combine(directory.Path, "pending");
        UpdateTestSupport.WriteReadyPackage(pending, ready);
        var newer = UpdateTestSupport.CreateRelease("1.0.11");
        var handler = new UpdateHttpHandler(newer) { BlockFirstInstaller = true };
        string? applied = null;
        await using var service = CreateService(
            store,
            directory,
            handler,
            requestApply: path => applied = path);
        Assert.Equal(UpdateState.Ready, service.State);

        var check = service.CheckForUpdatesAsync(manual: true, TestContext.Current.CancellationToken);
        await handler.InstallerReadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal("1.0.10", service.TargetVersion);
        var requestsBeforeApply = handler.ApiRequests;

        await service.ApplyAsync(TestContext.Current.CancellationToken);
        await check;

        Assert.Equal(Path.Combine(pending, ready.InstallerName), applied);
        Assert.Equal(requestsBeforeApply, handler.ApiRequests);
    }

    [Fact]
    public async Task StagedVersionIsNotDownloadedAgain()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        UpdateTestSupport.WriteReadyPackage(Path.Combine(directory.Path, "pending"), ready);
        var handler = new UpdateHttpHandler(ready);
        await using var service = CreateService(store, directory, handler);

        await service.CheckForUpdatesAsync(manual: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal(1, handler.ApiRequests);
        Assert.Equal(0, handler.AssetResponses);
    }

    [Fact]
    public async Task InvalidApplyRemovesPendingAndReportsFailureWithoutLaunching()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        var pending = Path.Combine(directory.Path, "pending");
        var corruptInstaller = ready.Installer.ToArray();
        corruptInstaller[0] ^= 0xff;
        UpdateTestSupport.WriteReadyPackage(pending, ready, corruptInstaller);
        var launched = false;
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(
            store,
            directory,
            new UpdateHttpHandler(ready),
            requestApply: _ => launched = true);
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);

        await service.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.False(launched);
        Assert.False(Directory.Exists(pending));
        Assert.Equal(UpdateState.Idle, service.State);
        Assert.Contains(UpdateNotificationKind.InvalidStagedUpdate, notifications);
    }

    private static UpdateService CreateService(
        TempPairingStore store,
        UpdateTemporaryDirectory directory,
        UpdateHttpHandler handler,
        PairingManager? manager = null,
        Func<CancellationToken, Task>? schedule = null,
        Action<string>? requestApply = null)
    {
        var modifyInstaller = Path.Combine(directory.Path, "VolturaAir-Modify.exe");
        File.WriteAllBytes(modifyInstaller, []);
        return new UpdateService(
            manager ?? new PairingManager(store.Store),
            [],
            requestApply,
            eligibleOverride: true,
            scheduleAutomaticWork: schedule,
            clientFactory: () => new System.Net.Http.HttpClient(handler, disposeHandler: false),
            modifyInstallerOverride: modifyInstaller,
            pendingDirectoryOverride: Path.Combine(directory.Path, "pending"),
            currentVersionOverride: new Version(1, 0, 9),
            manifestVerifier: static (_, _) => true);
    }
}
