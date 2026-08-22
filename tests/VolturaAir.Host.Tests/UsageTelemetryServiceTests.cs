using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using VolturaAir.Host.Features.UsageTelemetry;
using static VolturaAir.Host.Tests.UsageTelemetryTestSupport;

namespace VolturaAir.Host.Tests;

public sealed class UsageTelemetryServiceTests
{
    [Fact]
    public void ProductionTransportNeverFollowsRedirects()
    {
        using var handler = UsageTelemetryService.CreateProductionHttpHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task DisabledServiceOwnsNoRecorderOrNetworkWork()
    {
        var handler = new RecordingHandler(_ => Accepted());
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Portable,
            UsageStatisticsConsent.Unset,
            null));
        await using var service = CreateService(settings, handler);

        await service.InitializeAsync();
        service.RecordConnection(UsageConnectionMethod.Relay);
        service.RecordFeature(UsageFeature.Files);

        Assert.False(service.IsEnabled);
        Assert.False(service.SealNowForTesting());
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task SendsOnlyTheClosedAggregateContract()
    {
        var handler = new RecordingHandler(_ => Accepted());
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Allowed,
            InstallationId));
        await using var service = CreateService(settings, handler);
        await service.InitializeAsync();

        service.RecordConnection(UsageConnectionMethod.EnhancedDirect);
        service.RecordFeature(UsageFeature.Dictation);
        service.RecordFeature(UsageFeature.ScreenViewing);
        Assert.True(service.SealNowForTesting());
        await handler.WaitForCountAsync(1);

        using var document = JsonDocument.Parse(handler.Bodies.Single());
        var root = document.RootElement;
        Assert.Equal(
            ["schemaVersion", "installationId", "batchId", "hostVersion", "hostStarts", "connections", "features"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(InstallationId.ToString("D"), root.GetProperty("installationId").GetString());
        Assert.Equal(1, root.GetProperty("hostStarts").GetInt32());
        Assert.Equal(1, root.GetProperty("connections").GetProperty("enhancedDirect").GetInt32());
        Assert.Equal(1, root.GetProperty("features").GetProperty("dictation").GetInt32());
        Assert.Equal(1, root.GetProperty("features").GetProperty("screenViewing").GetInt32());
        Assert.DoesNotContain("text", handler.Bodies.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coordinate", handler.Bodies.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShutdownFlushesTheCurrentAggregateOnce()
    {
        var handler = new RecordingHandler(_ => Accepted());
        await using var service = CreateService(EnabledSettings(), handler);
        await service.InitializeAsync();

        service.RecordFeature(UsageFeature.GyroMouse);
        await service.DisposeAsync();

        await handler.WaitForCountAsync(1);
        using var document = JsonDocument.Parse(handler.Bodies.Single());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("hostStarts").GetInt32());
        Assert.Equal(1, root.GetProperty("features").GetProperty("gyroMouse").GetInt32());
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task ShutdownFlushMakesOneBoundedAttemptWithoutRetrying()
    {
        var log = new RecordingAppLog();
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Accepted();
        });
        await using var service = CreateService(
            EnabledSettings(),
            handler,
            retryDelays: [TimeSpan.FromMinutes(1)],
            appLog: log,
            requestTimeout: TimeSpan.FromMilliseconds(25));
        await service.InitializeAsync();

        service.RecordFeature(UsageFeature.GyroMouse);
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(handler.Bodies);
        Assert.Contains(log.Entries, entry =>
            entry.Action == "usage_statistics_delivery" && entry.Outcome == "timeout");
        Assert.DoesNotContain(log.Entries, entry => entry.Outcome == "retry_exhausted");
    }

    [Fact]
    public async Task DisableDiscardsUnsealedCountersWithoutSending()
    {
        var settings = EnabledSettings();
        var handler = new RecordingHandler(_ => Accepted());
        await using var service = CreateService(settings, handler);
        await service.InitializeAsync();

        service.RecordFeature(UsageFeature.GyroMouse);
        var result = await service.SetEnabledAsync(false);

        Assert.False(result.EffectiveEnabled);
        Assert.True(result.Saved);
        Assert.Empty(handler.Bodies);
        Assert.Null(settings.State.InstallationId);
    }

    [Fact]
    public async Task RetryReusesTheBatchIdAndNeverBlocksProducers()
    {
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (attempt, cancellationToken) =>
        {
            if (attempt == 1)
            {
                await releaseFirstRequest.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return Accepted();
        });
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Allowed,
            InstallationId));
        await using var service = CreateService(
            settings,
            handler,
            retryDelays: [TimeSpan.Zero]);
        await service.InitializeAsync();
        service.RecordFeature(UsageFeature.Trackpad);
        Assert.True(service.SealNowForTesting());
        await handler.WaitForCountAsync(1);

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 100_000; index++)
        {
            service.RecordFeature(UsageFeature.GyroMouse);
        }
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        releaseFirstRequest.SetResult();
        await handler.WaitForCountAsync(2);
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        using var second = JsonDocument.Parse(handler.Bodies[1]);
        Assert.Equal(
            first.RootElement.GetProperty("batchId").GetString(),
            second.RootElement.GetProperty("batchId").GetString());
    }

    [Fact]
    public async Task DisableCancelsInflightDeliveryDiscardsStateAndRotatesIdentity()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Accepted();
        });
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Allowed,
            InstallationId));
        await using var service = CreateService(settings, handler);
        await service.InitializeAsync();
        var originalToken = service.CurrentRecordingToken;
        using var session = new UsageTelemetrySession(service);
        Assert.True(session.TryRegister(service.SessionRegistry));
        session.RecordOnce(UsageFeature.Keyboard, originalToken);
        Assert.NotEqual(default, session.ReadStateForTesting());
        Assert.True(service.SealNowForTesting());
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disabled = await service.SetEnabledAsync(false).WaitAsync(TimeSpan.FromSeconds(5));
        service.RecordFeature(UsageFeature.Files);

        Assert.False(disabled.EffectiveEnabled);
        Assert.True(disabled.Saved);
        Assert.False(service.IsEnabled);
        Assert.False(service.SealNowForTesting());
        Assert.Null(settings.State.InstallationId);
        Assert.Equal(default, session.ReadStateForTesting());

        var enabled = await service.SetEnabledAsync(true);
        Assert.True(enabled.EffectiveEnabled);
        Assert.NotEqual(InstallationId, settings.State.InstallationId);
        var replacementToken = service.CurrentRecordingToken;
        Assert.NotEqual(originalToken, replacementToken);
        Assert.False(service.TryRecordFeature(UsageFeature.Presentation, originalToken));

        session.RecordOnce(UsageFeature.Files, replacementToken);
        Assert.True(service.SealNowForTesting());
        await handler.WaitForCountAsync(2);
        using var reenabledBatch = JsonDocument.Parse(handler.Bodies[1]);
        Assert.Equal(1, reenabledBatch.RootElement.GetProperty("hostStarts").GetInt32());
        Assert.Equal(0, reenabledBatch.RootElement.GetProperty("features").GetProperty("presentation").GetInt32());
        Assert.Equal(1, reenabledBatch.RootElement.GetProperty("features").GetProperty("files").GetInt32());
    }

    [Fact]
    public async Task DurableDenialWithIdentityCleanupFailureStaysOffAndReportsTheStaleIdentity()
    {
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Allowed,
            InstallationId))
        {
            DenyIdentityRemoved = false
        };
        await using var service = CreateService(settings, new RecordingHandler(_ => Accepted()));
        await service.InitializeAsync();

        var result = await service.SetEnabledAsync(false);

        Assert.False(result.EffectiveEnabled);
        Assert.True(result.Saved);
        Assert.False(result.IdentityRemoved);
        Assert.Equal(UsageStatisticsRuntimeState.OffIdentityCleanupPending, service.State);
        Assert.False(service.IsEnabled);
        Assert.Equal(UsageStatisticsConsent.Denied, settings.State.Consent);
        Assert.Equal(InstallationId, settings.State.InstallationId);
    }

    [Fact]
    public async Task FailedDisablePersistenceStaysPendingUntilDenialIsDurable()
    {
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Allowed,
            InstallationId))
        {
            DenyFailuresRemaining = 1
        };
        await using var service = CreateService(settings, new RecordingHandler(_ => Accepted()));
        await service.InitializeAsync();

        var failed = await service.SetEnabledAsync(false);

        Assert.False(failed.EffectiveEnabled);
        Assert.False(failed.Saved);
        Assert.Equal(UsageStatisticsRuntimeState.OffChoiceNotSaved, service.State);
        Assert.False(service.IsEnabled);
        Assert.Equal(UsageStatisticsConsent.Allowed, settings.State.Consent);
        Assert.Equal(InstallationId, settings.State.InstallationId);

        var retried = await service.SetEnabledAsync(false);

        Assert.True(retried.Saved);
        Assert.True(retried.IdentityRemoved);
        Assert.Equal(UsageStatisticsRuntimeState.Off, service.State);
        Assert.Equal(2, settings.DenyCalls);
        Assert.Equal(UsageStatisticsConsent.Denied, settings.State.Consent);
        Assert.Null(settings.State.InstallationId);
    }

    [Fact]
    public async Task StartupStaleIdentityCleanupFailureRemainsVisibleAndIsRetried()
    {
        var settings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Denied,
            InstallationId))
        {
            StaleIdentityRemoved = false
        };
        await using var service = CreateService(settings, new RecordingHandler(_ => Accepted()));
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        await service.InitializeAsync();
        await service.InitializeAsync();

        Assert.False(service.IsEnabled);
        Assert.Equal(UsageStatisticsRuntimeState.OffIdentityCleanupPending, service.State);
        Assert.Equal(2, settings.DeleteStaleCalls);
        Assert.Equal(2, stateChanges);
        Assert.Equal(InstallationId, settings.State.InstallationId);
    }

    [Fact]
    public async Task AccumulatorSealPreservesEveryAcceptedConcurrentIncrement()
    {
        var accumulator = new UsageTelemetryAccumulator();
        var start = new ManualResetEventSlim(false);
        var accepted = 0;
        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait();
            for (var index = 0; index < 10_000; index++)
            {
                if (!accumulator.TryRecordFeature(UsageFeature.Trackpad))
                {
                    return;
                }

                Interlocked.Increment(ref accepted);
            }
        })).ToArray();

        start.Set();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref accepted) >= 100, TimeSpan.FromSeconds(5)));
        var snapshot = accumulator.Seal(InstallationId, Guid.NewGuid(), "1.0.5");
        await Task.WhenAll(writers);

        Assert.Equal(accepted, snapshot.Batch.Features.Trackpad);
        Assert.False(accumulator.TryRecordFeature(UsageFeature.Trackpad));
    }

    [Fact]
    public void AccumulatorCountersSaturateWithoutWrapping()
    {
        var accumulator = new UsageTelemetryAccumulator();
        for (var index = 0; index <= ushort.MaxValue; index++)
        {
            Assert.True(accumulator.TryRecordConnection(UsageConnectionMethod.Relay));
        }

        var snapshot = accumulator.Seal(InstallationId, Guid.NewGuid(), "1.0.5");

        Assert.Equal(ushort.MaxValue, snapshot.Batch.Connections.Relay);
        Assert.True(snapshot.Overflowed);
    }

    [Fact]
    public async Task RetryableResponsesUseTheBoundedScheduleAndPermanentClientErrorsDoNotRetry()
    {
        var retryLog = new RecordingAppLog();
        var retryHandler = new RecordingHandler(attempt => attempt switch
        {
            1 => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            2 => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("malformed") },
            3 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => Accepted()
        });
        var retrySettings = EnabledSettings();
        await using (var service = CreateService(
            retrySettings,
            retryHandler,
            retryDelays: [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
            appLog: retryLog))
        {
            await service.InitializeAsync();
            Assert.True(service.SealNowForTesting());
            await retryHandler.WaitForCountAsync(4);
            await WaitUntilAsync(() => retryLog.Entries.Any(entry => entry.Outcome == "accepted"));
        }

        Assert.Contains(retryLog.Entries, entry => entry.Outcome == "rate_limited" && entry.Code == "429");
        Assert.Contains(retryLog.Entries, entry => entry.Outcome == "server_failed" && entry.Code == "202");
        Assert.Contains(retryLog.Entries, entry => entry.Outcome == "server_failed" && entry.Code == "503");

        var rejectionLog = new RecordingAppLog();
        var rejectionHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        await using (var service = CreateService(
            EnabledSettings(),
            rejectionHandler,
            retryDelays: [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
            appLog: rejectionLog))
        {
            await service.InitializeAsync();
            Assert.True(service.SealNowForTesting());
            await WaitUntilAsync(() => rejectionLog.Entries.Any(entry => entry.Outcome == "client_rejected"));
            Assert.Single(rejectionHandler.Bodies);
        }
    }

    [Fact]
    public async Task TimeoutIsClassifiedAndRetryExhaustionDoesNotExposeSensitiveData()
    {
        var log = new RecordingAppLog();
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Accepted();
        });
        await using var service = CreateService(
            EnabledSettings(),
            handler,
            retryDelays: [],
            appLog: log,
            requestTimeout: TimeSpan.FromMilliseconds(25));
        await service.InitializeAsync();
        service.RecordFeature(UsageFeature.Keyboard);
        Assert.True(service.SealNowForTesting());
        await WaitUntilAsync(() => log.Entries.Any(entry => entry.Outcome == "retry_exhausted"));

        var serializedLog = JsonSerializer.Serialize(log.Entries);
        Assert.Contains(log.Entries, entry => entry.Outcome == "timeout" && entry.Code == "timeout");
        Assert.DoesNotContain(InstallationId.ToString("D"), serializedLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voltura.se", serializedLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keyboard.text", serializedLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkFailureRetriesWithoutLoggingExceptionContent()
    {
        var log = new RecordingAppLog();
        var handler = new RecordingHandler((attempt, _) => attempt == 1
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("installationId=secret"))
            : Task.FromResult(Accepted()));
        await using var service = CreateService(
            EnabledSettings(),
            handler,
            retryDelays: [TimeSpan.Zero],
            appLog: log);
        await service.InitializeAsync();
        Assert.True(service.SealNowForTesting());
        await handler.WaitForCountAsync(2);
        await WaitUntilAsync(() => log.Entries.Any(entry => entry.Outcome == "accepted"));

        Assert.Contains(log.Entries, entry => entry.Outcome == "network_failed" && entry.Code == "network");
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(log.Entries), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnexpectedTransportFailureIsContainedAndDoesNotFaultShutdownOrLogs()
    {
        var log = new RecordingAppLog();
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("installationId=secret")));
        await using var service = CreateService(
            EnabledSettings(),
            handler,
            retryDelays: [],
            appLog: log);
        await service.InitializeAsync();
        Assert.True(service.SealNowForTesting());
        await WaitUntilAsync(() => log.Entries.Any(entry => entry.Outcome == "retry_exhausted"));

        var result = await service.SetEnabledAsync(false).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.EffectiveEnabled);
        Assert.Contains(log.Entries, entry => entry.Outcome == "network_failed" && entry.Code == "network");
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(log.Entries), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisableCancelsRetryBackoffImmediately()
    {
        var backoffStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var service = CreateService(
            EnabledSettings(),
            handler,
            retryDelays: [TimeSpan.FromMinutes(1)],
            delayAsync: (delay, cancellationToken) =>
            {
                if (delay != TimeSpan.FromDays(1))
                {
                    backoffStarted.TrySetResult();
                }
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        await service.InitializeAsync();
        Assert.True(service.SealNowForTesting());
        await backoffStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await service.SetEnabledAsync(false).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.EffectiveEnabled);
        Assert.False(service.IsEnabled);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task FullBatchChannelDropsTheNewSnapshotWithoutBlockingTheProducer()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Accepted();
        });
        var log = new RecordingAppLog();
        await using var service = CreateService(EnabledSettings(), handler, appLog: log);
        await service.InitializeAsync();
        service.RecordFeature(UsageFeature.Trackpad);
        Assert.True(service.SealNowForTesting());
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        service.RecordFeature(UsageFeature.Files);
        Assert.True(service.SealNowForTesting());
        service.RecordFeature(UsageFeature.Presentation);
        var stopwatch = Stopwatch.StartNew();
        Assert.False(service.SealNowForTesting());
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Contains(log.Entries, entry => entry.Outcome == "backpressure_dropped");
    }

    [Fact]
    public async Task InitializationRepairsAllowedIdentityCleansDisabledStateAndIsIdempotent()
    {
        var repairedSettings = new FakeSettings(new(
            UsageStatisticsDistribution.Installed,
            UsageStatisticsConsent.Allowed,
            null));
        var handler = new RecordingHandler(_ => Accepted());
        await using (var service = CreateService(repairedSettings, handler))
        {
            await service.InitializeAsync();
            await service.InitializeAsync();
            Assert.True(service.IsEnabled);
            Assert.Equal(1, repairedSettings.RepairCalls);
            Assert.True(service.SealNowForTesting());
            await handler.WaitForCountAsync(1);
            using var body = JsonDocument.Parse(handler.Bodies.Single());
            Assert.Equal(1, body.RootElement.GetProperty("hostStarts").GetInt32());
        }

        var disabledSettings = new FakeSettings(new(
            UsageStatisticsDistribution.Portable,
            UsageStatisticsConsent.Denied,
            InstallationId));
        await using var disabledService = CreateService(disabledSettings, new RecordingHandler(_ => Accepted()));
        await disabledService.InitializeAsync();
        Assert.Equal(1, disabledSettings.DeleteStaleCalls);
        Assert.Null(disabledSettings.State.InstallationId);
        Assert.False(disabledService.IsEnabled);
    }

    [Fact]
    public async Task IsolatedNetworkModeShowsConsentButNeverStartsAWorkerOrSends()
    {
        var handler = new RecordingHandler(_ => Accepted());
        var settings = EnabledSettings();
        await using var service = CreateService(settings, handler, networkAllowed: false);

        await service.InitializeAsync();
        service.RecordFeature(UsageFeature.PhoneWebcam);

        Assert.Equal(UsageStatisticsRuntimeState.On, service.State);
        Assert.False(service.IsEnabled);
        Assert.False(service.SealNowForTesting());
        Assert.Empty(handler.Bodies);

        var disabled = await service.SetEnabledAsync(false);
        Assert.False(disabled.EffectiveEnabled);
        Assert.Equal(UsageStatisticsRuntimeState.Off, service.State);
        Assert.Null(settings.State.InstallationId);

        var enabled = await service.SetEnabledAsync(true);
        Assert.True(enabled.EffectiveEnabled);
        Assert.Equal(UsageStatisticsRuntimeState.On, service.State);
        Assert.NotNull(settings.State.InstallationId);
        Assert.False(service.IsEnabled);
        Assert.False(service.SealNowForTesting());
        Assert.Empty(handler.Bodies);
    }

}
