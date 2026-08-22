using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace VolturaAir.Host.Features.UsageTelemetry;

internal sealed record UsageStatisticsTransitionResult(
    bool EffectiveEnabled,
    bool Saved,
    bool IdentityRemoved);

internal sealed class UsageTelemetryServiceOptions
{
    public Uri Endpoint { get; init; } = new("https://voltura.se/air/telemetry/v1/ingest.php");
    public bool NetworkAllowed { get; init; } = true;
    public TimeSpan SealInterval { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    public Func<TimeSpan> InitialDelay { get; init; } = static () =>
        TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(5 * 60, 15 * 60 + 1));

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    public Func<Guid> BatchIdFactory { get; init; } = Guid.NewGuid;

    public HttpMessageHandler? HttpHandler { get; init; }
}

internal sealed class UsageTelemetryService : IUsageTelemetryRecorder, IUsageStatisticsControl, IAsyncDisposable
{
    private const string AcceptedResponse = "{\"schemaVersion\":1,\"status\":\"accepted\"}";
    private readonly IUsageStatisticsSettings _settings;
    private readonly IAppLog _appLog;
    private readonly UsageTelemetryServiceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private UsageTelemetryWorker? _worker;
    private long _nextGeneration;
    private int _runtimeState;
    private int _disposed;

    public UsageTelemetryService(
        IUsageStatisticsSettings settings,
        IAppLog appLog,
        UsageTelemetryServiceOptions? options = null)
    {
        _settings = settings;
        _appLog = appLog;
        _options = options ?? new UsageTelemetryServiceOptions();
#pragma warning disable CA2000 // HttpClient owns the production handler through disposeHandler: true.
        _httpClient = _options.HttpHandler is null
            ? new HttpClient(CreateProductionHttpHandler(), disposeHandler: true)
            : new HttpClient(_options.HttpHandler, disposeHandler: false);
#pragma warning restore CA2000
    }

    internal static HttpClientHandler CreateProductionHttpHandler() => new() { AllowAutoRedirect = false };

    public bool IsEnabled => Volatile.Read(ref _worker) is not null;

    public UsageTelemetryRecordingToken CurrentRecordingToken
    {
        get
        {
            var worker = Volatile.Read(ref _worker);
            return worker is null
                ? default
                : new UsageTelemetryRecordingToken(worker.Generation);
        }
    }

    public UsageTelemetrySessionRegistry SessionRegistry { get; } = new();

    public UsageStatisticsRuntimeState State =>
        (UsageStatisticsRuntimeState)Volatile.Read(ref _runtimeState);

    public UsageStatisticsDistribution Distribution { get; private set; } = UsageStatisticsDistribution.Portable;

    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await Task.Run(_settings.Read, cancellationToken).ConfigureAwait(false);
            Distribution = state.Distribution;
            if (state.Consent != UsageStatisticsConsent.Allowed)
            {
                var identityRemoved = await Task.Run(
                    _settings.DeleteStaleIdentity,
                    cancellationToken).ConfigureAwait(false);
                SetState(identityRemoved
                    ? UsageStatisticsRuntimeState.Off
                    : UsageStatisticsRuntimeState.OffIdentityCleanupPending);
                if (!identityRemoved)
                {
                    WriteLifecycleLog("consent_transition", "disabled_identity_cleanup_failed");
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var identity = state.InstallationId is { } validId
                ? new UsageStatisticsSettingsResult(true, validId)
                : await Task.Run(_settings.RepairAllowedIdentity, cancellationToken).ConfigureAwait(false);
            if (identity.Succeeded && identity.InstallationId is { } installationId)
            {
                if (!_options.NetworkAllowed)
                {
                    SetState(UsageStatisticsRuntimeState.On);
                }
                else if (!IsEnabled)
                {
                    StartWorker(installationId);
                }
            }
            else
            {
                SetState(UsageStatisticsRuntimeState.Off);
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public void RecordConnection(UsageConnectionMethod method)
    {
        _ = TryRecordConnection(method, CurrentRecordingToken);
    }

    public void RecordFeature(UsageFeature feature)
    {
        _ = TryRecordFeature(feature, CurrentRecordingToken);
    }

    public bool TryRecordConnection(
        UsageConnectionMethod method,
        UsageTelemetryRecordingToken token)
    {
        if (!token.IsEnabled)
        {
            return false;
        }

        var worker = Volatile.Read(ref _worker);
        if (worker is null || worker.Generation != token.Generation)
        {
            return false;
        }

        var accumulator = worker.Accumulator;
        if (accumulator.TryRecordConnection(method))
        {
            return true;
        }

        var replacement = worker.Accumulator;
        return !ReferenceEquals(accumulator, replacement) &&
            replacement.TryRecordConnection(method);
    }

    public bool TryRecordFeature(
        UsageFeature feature,
        UsageTelemetryRecordingToken token)
    {
        if (!token.IsEnabled)
        {
            return false;
        }

        var worker = Volatile.Read(ref _worker);
        if (worker is null || worker.Generation != token.Generation)
        {
            return false;
        }

        var accumulator = worker.Accumulator;
        if (accumulator.TryRecordFeature(feature))
        {
            return true;
        }

        var replacement = worker.Accumulator;
        return !ReferenceEquals(accumulator, replacement) &&
            replacement.TryRecordFeature(feature);
    }

    public async Task<UsageStatisticsTransitionResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (enabled)
            {
                if (State == UsageStatisticsRuntimeState.On)
                {
                    return new UsageStatisticsTransitionResult(true, true, true);
                }

                var saved = await Task.Run(_settings.AllowWithNewIdentity, cancellationToken).ConfigureAwait(false);
                if (!saved.Succeeded || saved.InstallationId is not { } installationId)
                {
                    SetState(UsageStatisticsRuntimeState.Off);
                    WriteLifecycleLog("consent_transition", "enable_failed");
                    return new UsageStatisticsTransitionResult(false, false, false);
                }

                if (!_options.NetworkAllowed)
                {
                    SetState(UsageStatisticsRuntimeState.On);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return new UsageStatisticsTransitionResult(true, true, true);
                }

                StartWorker(installationId);
                WriteLifecycleLog("consent_transition", "enabled");
                StateChanged?.Invoke(this, EventArgs.Empty);
                return new UsageStatisticsTransitionResult(true, true, true);
            }

            await StopWorkerAsync(flushOnShutdown: false).ConfigureAwait(false);
            var denied = await Task.Run(_settings.DenyAndDeleteIdentity, cancellationToken).ConfigureAwait(false);
            SetState(!denied.Succeeded
                ? UsageStatisticsRuntimeState.OffChoiceNotSaved
                : denied.IdentityRemoved
                    ? UsageStatisticsRuntimeState.Off
                    : UsageStatisticsRuntimeState.OffIdentityCleanupPending);
            var outcome = !denied.Succeeded
                ? "disabled_not_saved"
                : denied.IdentityRemoved
                    ? "disabled"
                    : "disabled_identity_cleanup_failed";
            WriteLifecycleLog("consent_transition", outcome);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new UsageStatisticsTransitionResult(false, denied.Succeeded, denied.IdentityRemoved);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopWorkerAsync(flushOnShutdown: true).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
            _transitionGate.Dispose();
            _httpClient.Dispose();
        }
    }

    internal bool SealNowForTesting()
    {
        var worker = Volatile.Read(ref _worker);
        return worker is not null && SealAndQueue(worker);
    }

    private void StartWorker(Guid installationId)
    {
#pragma warning disable CA2000 // UsageTelemetryWorker owns and disposes the source after both worker tasks stop.
        var cancellation = new CancellationTokenSource();
#pragma warning restore CA2000
        var channel = Channel.CreateBounded<UsageTelemetryBatch>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        var accumulator = new UsageTelemetryAccumulator();
        accumulator.RecordHostStart();
        var generation = Interlocked.Increment(ref _nextGeneration);
        var worker = new UsageTelemetryWorker(
            installationId,
            generation,
            accumulator,
            channel,
            cancellation);
        Volatile.Write(ref _worker, worker);
        SetState(UsageStatisticsRuntimeState.On);
        worker.SchedulerTask = Task.Run(() => RunSchedulerAsync(worker));
        worker.SenderTask = Task.Run(() => RunSenderAsync(worker));
        WriteLifecycleLog("worker_start", "enabled");
    }

    private async Task StopWorkerAsync(bool flushOnShutdown)
    {
        var worker = Interlocked.Exchange(ref _worker, null);
        SetState(UsageStatisticsRuntimeState.Off);
        if (worker is null)
        {
            return;
        }

        SessionRegistry.ResetThrough(worker.Generation);

        await worker.Cancellation.CancelAsync().ConfigureAwait(false);
        worker.Channel.Writer.TryComplete();
        try
        {
            await Task.WhenAll(worker.SchedulerTask, worker.SenderTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal disable and shutdown path.
        }
        finally
        {
            var queuedBatchesRemoved = 0;
            while (worker.Channel.Reader.TryRead(out _))
            {
                queuedBatchesRemoved++;
            }
            if (flushOnShutdown)
            {
                await FlushAccumulatorOnShutdownAsync(worker).ConfigureAwait(false);
            }
            worker.Accumulator.Clear();
            worker.Cancellation.Dispose();
            if (queuedBatchesRemoved != 0)
            {
                WriteLifecycleLog("queued_state_removal", "discarded");
            }
            WriteLifecycleLog("worker_stop", "cancelled");
        }
    }

    private async Task FlushAccumulatorOnShutdownAsync(UsageTelemetryWorker worker)
    {
        var snapshot = worker.Accumulator.Seal(
            worker.InstallationId,
            _options.BatchIdFactory(),
            AppVersion.Display);
        if (snapshot.Overflowed)
        {
            WriteDeliveryLog("counter_saturated", "local_limit", snapshot.Batch);
        }

        if (!snapshot.Batch.HasCounts)
        {
            return;
        }

        // Shutdown makes one bounded attempt and deliberately does not enter the retry schedule.
        var result = await SendOnceAsync(snapshot.Batch, CancellationToken.None).ConfigureAwait(false);
        WriteDeliveryLog(result.LogOutcome, result.StatusCode, snapshot.Batch);
    }

    private async Task RunSchedulerAsync(UsageTelemetryWorker worker)
    {
        try
        {
            await _options.DelayAsync(_options.InitialDelay(), worker.Cancellation.Token).ConfigureAwait(false);
            while (!worker.Cancellation.IsCancellationRequested)
            {
                SealAndQueue(worker);
                await _options.DelayAsync(_options.SealInterval, worker.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (worker.Cancellation.IsCancellationRequested)
        {
        }
    }

    private bool SealAndQueue(UsageTelemetryWorker worker)
    {
        if (!ReferenceEquals(Volatile.Read(ref _worker), worker))
        {
            return false;
        }

        var sealedAccumulator = worker.Accumulator;
        var replacement = new UsageTelemetryAccumulator();
        if (!worker.TryReplaceAccumulator(sealedAccumulator, replacement))
        {
            return false;
        }

        var snapshot = sealedAccumulator.Seal(
            worker.InstallationId,
            _options.BatchIdFactory(),
            AppVersion.Display);
        if (snapshot.Overflowed)
        {
            WriteDeliveryLog("counter_saturated", "local_limit", snapshot.Batch);
        }

        if (!snapshot.Batch.HasCounts)
        {
            return true;
        }

        if (worker.Channel.Writer.TryWrite(snapshot.Batch))
        {
            return true;
        }

        WriteDeliveryLog("backpressure_dropped", "local_limit", snapshot.Batch);
        return false;
    }

    private async Task RunSenderAsync(UsageTelemetryWorker worker)
    {
        try
        {
            await foreach (var batch in worker.Channel.Reader.ReadAllAsync(worker.Cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await DeliverBatchAsync(batch, worker.Cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (worker.Cancellation.IsCancellationRequested)
                {
                    WriteDeliveryLog("cancelled", "cancelled", batch);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (worker.Cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task DeliverBatchAsync(UsageTelemetryBatch batch, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= _options.RetryDelays.Count; attempt++)
        {
            var result = await SendOnceAsync(batch, cancellationToken).ConfigureAwait(false);
            WriteDeliveryLog(result.LogOutcome, result.StatusCode, batch);
            if (!result.ShouldRetry)
            {
                return;
            }

            if (attempt == _options.RetryDelays.Count)
            {
                WriteDeliveryLog("retry_exhausted", result.StatusCode, batch);
                return;
            }

            await _options.DelayAsync(_options.RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<UsageTelemetryDeliveryResult> SendOnceAsync(
        UsageTelemetryBatch batch,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(batch), Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            var body = await ReadBoundedResponseAsync(response, timeout.Token).ConfigureAwait(false);
            var status = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (response.StatusCode == HttpStatusCode.Accepted && string.Equals(body, AcceptedResponse, StringComparison.Ordinal))
            {
                return new UsageTelemetryDeliveryResult(false, "accepted", status);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new UsageTelemetryDeliveryResult(true, "rate_limited", status);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
            {
                return new UsageTelemetryDeliveryResult(false, "client_rejected", status);
            }

            return new UsageTelemetryDeliveryResult(true, "server_failed", status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new UsageTelemetryDeliveryResult(true, "timeout", "timeout");
        }
        catch (HttpRequestException)
        {
            return new UsageTelemetryDeliveryResult(true, "network_failed", "network");
        }
        catch (IOException)
        {
            return new UsageTelemetryDeliveryResult(true, "network_failed", "network");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new UsageTelemetryDeliveryResult(true, "network_failed", "network");
        }
    }

    private static async Task<string?> ReadBoundedResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[1025];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length > 1024 ? null : Encoding.UTF8.GetString(buffer, 0, length);
    }

    private void WriteLifecycleLog(string action, string outcome)
    {
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: $"usage_statistics_{action}",
            Outcome: outcome,
            Detail: "destination=official_telemetry"));
    }

    private void WriteDeliveryLog(string outcome, string statusCode, UsageTelemetryBatch batch)
    {
        _appLog.Write(new AppLogEntry(
            Event: "action_taken",
            Source: "windows_host",
            Action: "usage_statistics_delivery",
            Outcome: outcome,
            Code: statusCode,
            Detail: BuildSafeCountSummary(batch)));
    }

    private static string BuildSafeCountSummary(UsageTelemetryBatch batch) => string.Join(
        ',',
        "destination=official_telemetry",
        $"hostStarts={batch.HostStarts}",
        $"standardLocal={batch.Connections.StandardLocal}",
        $"enhancedDirect={batch.Connections.EnhancedDirect}",
        $"relay={batch.Connections.Relay}",
        $"trackpad={batch.Features.Trackpad}",
        $"keyboard={batch.Features.Keyboard}",
        $"dictation={batch.Features.Dictation}",
        $"mediaControls={batch.Features.MediaControls}",
        $"presentation={batch.Features.Presentation}",
        $"customScreens={batch.Features.CustomScreens}",
        $"files={batch.Features.Files}",
        $"screenViewing={batch.Features.ScreenViewing}",
        $"phoneWebcam={batch.Features.PhoneWebcam}",
        $"gyroMouse={batch.Features.GyroMouse}");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private void SetState(UsageStatisticsRuntimeState state) =>
        Volatile.Write(ref _runtimeState, (int)state);

    private sealed class UsageTelemetryWorker(
        Guid installationId,
        long generation,
        UsageTelemetryAccumulator accumulator,
        Channel<UsageTelemetryBatch> channel,
        CancellationTokenSource cancellation)
    {
        private UsageTelemetryAccumulator _accumulator = accumulator;

        public Guid InstallationId { get; } = installationId;

        public long Generation { get; } = generation;

        public UsageTelemetryAccumulator Accumulator => Volatile.Read(ref _accumulator);

        public Channel<UsageTelemetryBatch> Channel { get; } = channel;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task SchedulerTask { get; set; } = Task.CompletedTask;

        public Task SenderTask { get; set; } = Task.CompletedTask;

        public bool TryReplaceAccumulator(
            UsageTelemetryAccumulator expected,
            UsageTelemetryAccumulator replacement) =>
            ReferenceEquals(Interlocked.CompareExchange(ref _accumulator, replacement, expected), expected);
    }

    private sealed record UsageTelemetryDeliveryResult(bool ShouldRetry, string LogOutcome, string StatusCode);
}
