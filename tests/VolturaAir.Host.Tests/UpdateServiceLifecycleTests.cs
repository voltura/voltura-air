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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualRequestTimeoutRestoresCachedUpdate(bool afterHeaders)
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        UpdateTestSupport.WriteReadyPackage(Path.Combine(directory.Path, "pending"), ready);
        using var handler = new UpdateHttpHandler(ready)
        {
            BeforeMetadata = afterHeaders ? null : token => Task.Delay(Timeout.InfiniteTimeSpan, token),
            BlockMetadataRead = afterHeaders
        };
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(store, directory, handler, requestTimeout: TimeSpan.FromMilliseconds(100));
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await service.CheckForUpdatesAsync(manual: true, timeout.Token);

        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal("1.0.10", service.TargetVersion);
        Assert.Equal([UpdateNotificationKind.CheckFailed], notifications);
        Assert.False(service.HasNetworkClient);
    }

    [Fact]
    public async Task InstallerReadTimeoutPreservesCachedUpdateAndCleansPartial()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        var candidate = UpdateTestSupport.CreateRelease("1.0.11");
        var pending = Path.Combine(directory.Path, "pending");
        UpdateTestSupport.WriteReadyPackage(pending, ready);
        using var handler = new UpdateHttpHandler(candidate) { BlockFirstInstaller = true };
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(store, directory, handler, requestTimeout: TimeSpan.FromMilliseconds(100));
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        await service.CheckForUpdatesAsync(manual: true, timeout.Token);

        Assert.True(handler.InstallerReadStarted.Task.IsCompleted);
        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal(ready.Version, service.TargetVersion);
        Assert.Equal([UpdateNotificationKind.CheckFailed], notifications);
        Assert.True(File.Exists(Path.Combine(pending, ready.InstallerName)));
        Assert.False(File.Exists(Path.Combine(pending, candidate.InstallerName + ".partial")));
        Assert.False(File.Exists(Path.Combine(pending, candidate.InstallerName)));
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

    [Fact]
    public async Task ConcurrentAutomaticChecksObserveTheSameControllerDisconnect()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        using var connection = manager.TrackConnection("client-a");
        using var handler = new UpdateHttpHandler(UpdateTestSupport.CreateRelease("1.0.9"));
        await using var service = CreateService(store, directory, handler, manager, static _ => Task.CompletedTask);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var first = service.CheckForUpdatesAsync(manual: false, timeout.Token);
        var second = service.CheckForUpdatesAsync(manual: false, timeout.Token);
        Assert.Equal(0, handler.ApiRequests);

        connection.Dispose();
        await Task.WhenAll(first, second);
        Assert.Equal(2, handler.ApiRequests);
    }

    [Fact]
    public async Task AutomaticCheckYieldsSingleFlightIfAControllerConnectedWhileItWaited()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new UpdateHttpHandler(UpdateTestSupport.CreateRelease("1.0.9"))
        {
            BeforeMetadata = token =>
            {
                checking.TrySetResult();
                return finishCheck.Task.WaitAsync(token);
            }
        };
        await using var service = CreateService(store, directory, handler, manager, static _ => Task.CompletedTask);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var first = service.CheckForUpdatesAsync(manual: true, deadline.Token);
        await checking.Task.WaitAsync(deadline.Token);
        var automatic = service.CheckForUpdatesAsync(manual: false, deadline.Token);
        using var connection = manager.TrackConnection("client-a");
        var nextManual = service.CheckForUpdatesAsync(manual: true, deadline.Token);

        finishCheck.TrySetResult();
        await Task.WhenAll(first, nextManual).WaitAsync(deadline.Token);

        Assert.Equal(2, handler.ApiRequests);
        Assert.False(automatic.IsCompleted);
        connection.Dispose();
        await automatic.WaitAsync(deadline.Token);
        Assert.Equal(3, handler.ApiRequests);
    }

    [Fact]
    public async Task ShutdownCancelsAndAwaitsActiveManualCheck()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new UpdateHttpHandler(UpdateTestSupport.CreateRelease("1.0.10"))
        {
            BeforeMetadata = async token =>
            {
                requestStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally
                {
                    requestCancelled.TrySetResult();
                    await finishRequest.Task;
                }
            }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var service = CreateService(store, directory, handler);
        var check = service.CheckForUpdatesAsync(manual: true, timeout.Token);
        await requestStarted.Task.WaitAsync(timeout.Token);

        var shutdown = service.DisposeAsync().AsTask();
        try
        {
            await requestCancelled.Task.WaitAsync(timeout.Token);
            Assert.False(shutdown.IsCompleted);
        }
        finally
        {
            finishRequest.TrySetResult();
            await shutdown;
            await check;
        }

        Assert.False(service.HasNetworkClient);
        Assert.False(service.HasPairingSubscription);
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
        var readyNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = CreateService(
            store,
            directory,
            handler,
            manager,
            automatic ? static _ => Task.CompletedTask : null);
        service.NotificationRequested += (_, e) =>
        {
            notifications.Add(e.Kind);
            if (e.Kind == UpdateNotificationKind.Ready) readyNotification.TrySetResult();
        };

        var check = service.CheckForUpdatesAsync(manual: !automatic, TestContext.Current.CancellationToken);
        await handler.InstallerReadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var connection = manager.TrackConnection("client-a");
        await check;

        Assert.Equal(UpdateState.WaitingForDevices, service.State);
        Assert.False(File.Exists(Path.Combine(directory.Path, "pending", release.InstallerName + ".partial")));
        if (!automatic) Assert.Contains(UpdateNotificationKind.WaitingForDevices, notifications);

        connection.Dispose();
        await readyNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal(automatic, AppUpdateSettings.AutomaticUpdateDownloadsEnabled());
    }

    [Fact]
    public async Task DownloadResumesWhenControllerDisconnectsBeforeCancellationSettles()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new UpdateHttpHandler(UpdateTestSupport.CreateRelease("1.0.10"))
        {
            BlockFirstInstaller = true,
            OnInstallerReadCancelled = () =>
            {
                cancelled.TrySetResult();
                return allowCancellation.Task;
            }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var service = CreateService(store, directory, handler, manager);

        var check = service.CheckForUpdatesAsync(manual: true, timeout.Token);
        await handler.InstallerReadStarted.Task.WaitAsync(timeout.Token);
        try
        {
            using var connection = manager.TrackConnection("client-a");
            await cancelled.Task.WaitAsync(timeout.Token);
        }
        finally { allowCancellation.TrySetResult(); }

        await check;
        await UpdateTestSupport.WaitUntilAsync(() => service.State == UpdateState.Ready);
        Assert.Equal(1, handler.ApiRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingAutomaticDownloadsCancelsOnlyScheduledDeferral(bool manual)
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        var release = UpdateTestSupport.CreateRelease("1.0.10");
        using var handler = new UpdateHttpHandler(release) { BlockFirstInstaller = true };
        CancellationToken automaticLifetime = default;
        Task Schedule(CancellationToken token)
        {
            automaticLifetime = token;
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        await using var service = CreateService(store, directory, handler, manager, Schedule);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var check = service.CheckForUpdatesAsync(manual, manual ? timeout.Token : automaticLifetime);
        await handler.InstallerReadStarted.Task.WaitAsync(timeout.Token);
        using var connection = manager.TrackConnection("client-a");
        await check.WaitAsync(timeout.Token);
        Assert.Equal(UpdateState.WaitingForDevices, service.State);

        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        if (!manual) await UpdateTestSupport.WaitUntilAsync(() => service.State == UpdateState.Idle);
        connection.Dispose();
        if (manual) await UpdateTestSupport.WaitUntilAsync(() => service.State == UpdateState.Ready);
        await service.DisposeAsync();

        var installer = Path.Combine(directory.Path, "pending", release.InstallerName);
        Assert.Equal(manual, File.Exists(installer));
        Assert.Equal(manual ? 6 : 3, handler.AssetResponses);
    }

    [Fact]
    public async Task NewerCandidateShowsDownloadProgressAndApplyCancelsWithoutNetwork()
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
        Assert.Equal(UpdateState.Downloading, service.State);
        Assert.Equal("1.0.11", service.TargetVersion);
        var requestsBeforeApply = handler.ApiRequests;

        await service.ApplyAsync(TestContext.Current.CancellationToken);
        await check;

        Assert.Equal(Path.Combine(pending, ready.InstallerName), applied);
        Assert.Equal(requestsBeforeApply, handler.ApiRequests);
    }

    [Fact]
    public async Task SuccessfulApplyKeepsTheInstallerWhileQueuedChecksRetire()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        using var connection = manager.TrackConnection("client-a");
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        var newer = UpdateTestSupport.CreateRelease("1.0.11");
        var pending = Path.Combine(directory.Path, "pending");
        UpdateTestSupport.WriteReadyPackage(pending, ready);
        using var handler = new UpdateHttpHandler(newer);
        var handoff = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var finishHandoff = new ManualResetEventSlim();
        var applied = 0;
        await using var service = CreateService(store, directory, handler, manager, static _ => Task.CompletedTask,
            requestApply: path =>
            {
                Interlocked.Increment(ref applied);
                handoff.TrySetResult(path);
                Assert.True(finishHandoff.Wait(TimeSpan.FromSeconds(5)));
            });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));

        var applying = Task.Run(() => service.ApplyAsync(deadline.Token));
        try
        {
            Assert.Equal(Path.Combine(pending, ready.InstallerName), await handoff.Task.WaitAsync(deadline.Token));
            var manual = service.CheckForUpdatesAsync(manual: true, deadline.Token);
            var automatic = service.CheckForUpdatesAsync(manual: false, deadline.Token);
            Assert.False(manual.IsCompleted);
            Assert.False(automatic.IsCompleted);
            finishHandoff.Set();
            await Task.WhenAll(applying, manual, automatic).WaitAsync(deadline.Token);
        }
        finally { finishHandoff.Set(); }

        connection.Dispose();
        await service.CheckForUpdatesAsync(manual: true, deadline.Token);
        await service.ApplyAsync(deadline.Token);
        Assert.Equal(0, handler.ApiRequests);
        Assert.Equal(1, applied);
        Assert.True(File.Exists(Path.Combine(pending, ready.InstallerName)));
        Assert.False(File.Exists(Path.Combine(pending, newer.InstallerName)));
    }

    [Fact]
    public async Task FailedApplyCallbackLeavesTheInstallerAvailableForRetry()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var directory = new UpdateTemporaryDirectory();
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        var pending = Path.Combine(directory.Path, "pending");
        UpdateTestSupport.WriteReadyPackage(pending, ready);
        using var handler = new UpdateHttpHandler(ready);
        var applied = 0;
        var notifications = new List<UpdateNotificationKind>();
        await using var service = CreateService(store, directory, handler, requestApply: _ =>
        {
            if (++applied == 1) throw new IOException("The test launcher did not receive the package.");
        });
        service.NotificationRequested += (_, e) => notifications.Add(e.Kind);

        await service.ApplyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal([UpdateNotificationKind.InstallFailed], notifications);
        await service.ApplyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, applied);
        Assert.True(File.Exists(Path.Combine(pending, ready.InstallerName)));
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
    public async Task FailedDeferredInstallerRequestRestoresTheCachedUpdateAndAllowsRetry()
    {
        AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        using var directory = new UpdateTemporaryDirectory();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing("client-a", "Phone", manager.CreatePairingToken(), reconnectPublicKey: key.PublicKey);
        var ready = UpdateTestSupport.CreateRelease("1.0.10");
        var candidate = UpdateTestSupport.CreateRelease("1.0.11");
        var pending = Path.Combine(directory.Path, "pending");
        UpdateTestSupport.WriteReadyPackage(pending, ready);
        using var connection = manager.TrackConnection("client-a");
        using var handler = new UpdateHttpHandler(candidate) { InstallerStatusCode = HttpStatusCode.ServiceUnavailable };
        await using var service = CreateService(store, directory, handler, manager);

        await service.CheckForUpdatesAsync(manual: true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.WaitingForDevices, service.State);
        connection.Dispose();
        await UpdateTestSupport.WaitUntilAsync(() => service.State == UpdateState.Ready && !service.HasNetworkClient);
        Assert.Equal(ready.Version, service.TargetVersion);
        Assert.True(File.Exists(Path.Combine(pending, ready.InstallerName)));
        Assert.False(File.Exists(Path.Combine(pending, candidate.InstallerName + ".partial")));

        handler.InstallerStatusCode = HttpStatusCode.OK;
        await service.CheckForUpdatesAsync(manual: true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, service.State);
        Assert.Equal(candidate.Version, service.TargetVersion);
        Assert.True(File.Exists(Path.Combine(pending, candidate.InstallerName)));
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
        Action<string>? requestApply = null,
        TimeSpan? requestTimeout = null)
    {
        var modifyInstaller = Path.Combine(directory.Path, "VolturaAir-Modify.exe");
        File.WriteAllBytes(modifyInstaller, []);
        return new UpdateService(
            manager ?? new PairingManager(store.Store),
            [],
            requestApply,
            eligibleOverride: true,
            scheduleAutomaticWork: schedule,
            clientFactory: () => new System.Net.Http.HttpClient(handler, disposeHandler: false)
            {
                Timeout = requestTimeout ?? TimeSpan.FromSeconds(20)
            },
            modifyInstallerOverride: modifyInstaller,
            pendingDirectoryOverride: Path.Combine(directory.Path, "pending"),
            currentVersionOverride: new Version(1, 0, 9),
            manifestVerifier: static (_, _) => true);
    }
}
