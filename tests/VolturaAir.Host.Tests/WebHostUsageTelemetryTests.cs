using VolturaAir.Host.Features.UsageTelemetry;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostUsageTelemetryTests : WebHostServiceTestBase
{
    [Fact]
    public async Task AuthenticatedSessionRecordsItsTransportAndEachFeatureContextOnce()
    {
        var recorder = new RecordingUsageTelemetry();
        await using var fixture = await WebHostFixture.StartAsync(usageTelemetry: recorder);
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);
        var clientId = $"client-{Guid.NewGuid():N}";

        var accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());

        await SendAsync(socket, new { type = "pointer.move", dx = 1, dy = 1, inputContext = "trackpad" });
        await SendAsync(socket, new { type = "pointer.move", dx = 2, dy = 2, inputContext = "trackpad" });
        await SendAsync(socket, new { type = "keyboard.text", text = "never recorded", inputContext = "dictation" });
        await SendAsync(socket, new { type = "keyboard.special", key = "MediaPlayPause", inputContext = "media-controls" });
        await SendAsync(socket, new { type = "keyboard.special", key = "Enter" });
        var pong = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        Assert.Equal("health.pong", pong.GetProperty("type").GetString());
        Assert.Equal([UsageConnectionMethod.StandardLocal], recorder.Connections);
        Assert.Equal([UsageFeature.Trackpad, UsageFeature.Dictation, UsageFeature.MediaControls], recorder.Features);
    }

    [Fact]
    public async Task DisabledRecorderIsNeverCalled()
    {
        var recorder = new RecordingUsageTelemetry { IsEnabled = false };
        await using var fixture = await WebHostFixture.StartAsync(usageTelemetry: recorder);
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        await SendAsync(socket, new { type = "pointer.move", dx = 1, dy = 1, inputContext = "gyro-mouse" });
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        Assert.Empty(recorder.Connections);
        Assert.Empty(recorder.Features);
    }

    [Fact]
    public async Task DisableImmediatelyResetsTheLiveSessionAndReenableRecordsAFeatureOnceAgain()
    {
        var recorder = new RecordingUsageTelemetry();
        await using var fixture = await WebHostFixture.StartAsync(usageTelemetry: recorder);
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        await SendAsync(socket, new { type = "pointer.move", dx = 1, dy = 1, inputContext = "trackpad" });
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        var stateBeforeDisable = recorder.ReadSessionState();
        Assert.NotEqual(0, stateBeforeDisable.Generation);
        Assert.NotEqual(0, stateBeforeDisable.Features);

        recorder.SetEnabled(false);
        var stateAfterDisable = recorder.ReadSessionState();
        Assert.Equal(0, stateAfterDisable.Generation);
        Assert.Equal(0, stateAfterDisable.Features);
        await SendAsync(socket, new { type = "pointer.move", dx = 2, dy = 2, inputContext = "trackpad" });
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });
        recorder.SetEnabled(true);
        await SendAsync(socket, new { type = "pointer.move", dx = 3, dy = 3, inputContext = "trackpad" });
        await SendAsync(socket, new { type = "pointer.move", dx = 4, dy = 4, inputContext = "trackpad" });
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        Assert.Equal([UsageFeature.Trackpad, UsageFeature.Trackpad], recorder.Features);
    }

    [Fact]
    public async Task CommandCapturedBeforeIdentityRotationCannotRecordIntoTheReplacementGeneration()
    {
        var recorder = new RecordingUsageTelemetry();
        await using var fixture = await WebHostFixture.StartAsync(usageTelemetry: recorder);
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        recorder.PauseNextTokenCapture();
        await SendAsync(socket, new { type = "pointer.move", dx = 1, dy = 1, inputContext = "trackpad" });
        await recorder.WaitForPausedCaptureAsync();

        recorder.SetEnabled(false);
        recorder.SetEnabled(true);
        recorder.ReleaseTokenCapture();
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        Assert.Empty(recorder.Features);

        await SendAsync(socket, new { type = "pointer.move", dx = 2, dy = 2, inputContext = "trackpad" });
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        Assert.Equal([UsageFeature.Trackpad], recorder.Features);
    }

    [Fact]
    public async Task AuthenticationCapturedBeforeIdentityRotationCannotRecordIntoTheReplacementGeneration()
    {
        var recorder = new RecordingUsageTelemetry();
        await using var fixture = await WebHostFixture.StartAsync(usageTelemetry: recorder);
        using var firstKey = new PairingTestKey();
        using var firstSocket = await ConnectAsync(fixture.WebHost);
        recorder.PauseNextTokenCapture();

        var firstAcceptedTask = SendAndReceiveAsync(firstSocket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = firstKey.PublicKey
        });
        await recorder.WaitForPausedCaptureAsync();
        recorder.SetEnabled(false);
        recorder.SetEnabled(true);
        recorder.ReleaseTokenCapture();

        var firstAccepted = await firstAcceptedTask;
        Assert.Equal("pair.accepted", firstAccepted.GetProperty("type").GetString());
        Assert.Empty(recorder.Connections);

        using var secondKey = new PairingTestKey();
        using var secondSocket = await ConnectAsync(fixture.WebHost);
        var secondAccepted = await SendAndReceiveAsync(secondSocket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = secondKey.PublicKey
        });

        Assert.Equal("pair.accepted", secondAccepted.GetProperty("type").GetString());
        Assert.Equal([UsageConnectionMethod.StandardLocal], recorder.Connections);
    }

    [Fact]
    public async Task AuthorizedMediaUseIsCountedWithoutDependingOnTheDownstreamAudioOutcome()
    {
        var recorder = new RecordingUsageTelemetry();
        await using var fixture = await WebHostFixture.StartAsync(
            audioController: new UnavailableAudioController(),
            usageTelemetry: recorder);
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        await SendAsync(socket, new { type = "audio.mute.toggle", inputContext = "media-controls" });
        _ = await SendAndReceiveAsync(socket, new { type = "health.ping" });

        Assert.Equal([UsageFeature.MediaControls], recorder.Features);
    }

    [Fact]
    public async Task FirstFeatureUseCompletesWhileDisableWaitsOnlyOnTheControlPath()
    {
        var recorder = new RecordingUsageTelemetry();
        using var session = new UsageTelemetrySession(recorder);
        Assert.True(session.TryRegister(recorder.SessionRegistry));
        var token = recorder.CurrentRecordingToken;
        recorder.PauseNextFeatureRecord();

        var recordTask = Task.Run(() => session.RecordOnce(UsageFeature.Trackpad, token));
        await recorder.WaitForFeatureRecordPauseAsync();
        var disableTask = Task.Run(() => recorder.SetEnabled(false));
        await recorder.WaitForResetStartAsync();
        Assert.False(disableTask.IsCompleted);

        recorder.ReleaseFeatureRecord();
        await recordTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disableTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(default, session.ReadStateForTesting());
    }

    [Fact]
    public void FirstFeatureUseInAReplacementGenerationAllocatesNoManagedState()
    {
        var recorder = new RecordingUsageTelemetry();
        using var session = new UsageTelemetrySession(recorder);
        Assert.True(session.TryRegister(recorder.SessionRegistry));
        session.RecordOnce(UsageFeature.Trackpad, recorder.CurrentRecordingToken);
        recorder.SetEnabled(false);
        recorder.SetEnabled(true);

        var replacementToken = recorder.CurrentRecordingToken;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        session.RecordOnce(UsageFeature.Keyboard, replacementToken);
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void SessionDisposeReleasesItsSlotEvenWhenTheOwningScopeThrows()
    {
        var recorder = new RecordingUsageTelemetry();

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var failedSession = new UsageTelemetrySession(recorder);
            Assert.True(failedSession.TryRegister(recorder.SessionRegistry));
            throw new InvalidOperationException("Injected disconnect cleanup failure.");
        }));

        var replacements = Enumerable.Range(0, UsageTelemetrySessionRegistry.Capacity)
            .Select(_ => new UsageTelemetrySession(recorder))
            .ToArray();
        try
        {
            Assert.All(replacements, session => Assert.True(session.TryRegister(recorder.SessionRegistry)));
            using var overflow = new UsageTelemetrySession(recorder);
            Assert.False(overflow.TryRegister(recorder.SessionRegistry));
        }
        finally
        {
            foreach (var session in replacements)
            {
                session.Dispose();
            }
        }
    }

    [Fact]
    public void SessionClassifierAcceptsOnlyContentFreeInputEnums()
    {
        var sessionType = typeof(UsageTelemetrySession);
        var classifier = sessionType.GetMethod(
            "RecordInputCommand",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert.NotNull(classifier);
        var parameters = classifier.GetParameters();

        Assert.Equal(
            [
                typeof(InputCommandKind),
                typeof(InputCommandContext?),
                typeof(UsageTelemetryRecordingToken)
            ],
            parameters.Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType == typeof(ValidatedInputCommand));
        Assert.DoesNotContain(
            sessionType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(Lock));
    }

    private sealed class RecordingUsageTelemetry : IUsageTelemetryRecorder
    {
        private TaskCompletionSource _capturePaused = NewCapturePausedSource();
        private TaskCompletionSource _captureRelease = CompletedCaptureSource();
        private TaskCompletionSource _featureRecordPaused = NewCapturePausedSource();
        private TaskCompletionSource _featureRecordRelease = CompletedCaptureSource();
        private TaskCompletionSource _resetStarted = NewCapturePausedSource();
        private long _generation = 1;
        private int _enabled = 1;
        private int _pauseNextCapture;
        private int _pauseNextFeatureRecord;

        public bool IsEnabled
        {
            get => Volatile.Read(ref _enabled) != 0;
            set => SetEnabled(value);
        }

        public UsageTelemetryRecordingToken CurrentRecordingToken
        {
            get
            {
                var token = Volatile.Read(ref _enabled) == 0
                    ? default
                    : new UsageTelemetryRecordingToken(Volatile.Read(ref _generation));
                if (Interlocked.Exchange(ref _pauseNextCapture, 0) != 0)
                {
                    _capturePaused.TrySetResult();
                    Assert.True(_captureRelease.Task.Wait(TimeSpan.FromSeconds(5)));
                }
                return token;
            }
        }

        public UsageTelemetrySessionRegistry SessionRegistry { get; } = new();

        public List<UsageConnectionMethod> Connections { get; } = new(capacity: 3);

        public List<UsageFeature> Features { get; } = new(capacity: 10);

        public bool TryRecordConnection(
            UsageConnectionMethod method,
            UsageTelemetryRecordingToken token)
        {
            if (!IsCurrent(token))
            {
                return false;
            }

            Connections.Add(method);
            return true;
        }

        public bool TryRecordFeature(
            UsageFeature feature,
            UsageTelemetryRecordingToken token)
        {
            if (!IsCurrent(token))
            {
                return false;
            }

            if (Interlocked.Exchange(ref _pauseNextFeatureRecord, 0) != 0)
            {
                _featureRecordPaused.TrySetResult();
                Assert.True(_featureRecordRelease.Task.Wait(TimeSpan.FromSeconds(5)));
            }

            Features.Add(feature);
            return true;
        }

        public void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                var disabledGeneration = Volatile.Read(ref _generation);
                Volatile.Write(ref _enabled, 0);
                _resetStarted.TrySetResult();
                SessionRegistry.ResetThrough(disabledGeneration);
                return;
            }

            if (Volatile.Read(ref _enabled) == 0)
            {
                Interlocked.Increment(ref _generation);
                Volatile.Write(ref _enabled, 1);
            }
        }

        public void PauseNextTokenCapture()
        {
            _capturePaused = NewCapturePausedSource();
            _captureRelease = NewCapturePausedSource();
            Volatile.Write(ref _pauseNextCapture, 1);
        }

        public Task WaitForPausedCaptureAsync() =>
            _capturePaused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void ReleaseTokenCapture() => _captureRelease.TrySetResult();

        public void PauseNextFeatureRecord()
        {
            _featureRecordPaused = NewCapturePausedSource();
            _featureRecordRelease = NewCapturePausedSource();
            _resetStarted = NewCapturePausedSource();
            Volatile.Write(ref _pauseNextFeatureRecord, 1);
        }

        public Task WaitForFeatureRecordPauseAsync() =>
            _featureRecordPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForResetStartAsync() =>
            _resetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void ReleaseFeatureRecord() => _featureRecordRelease.TrySetResult();

        public (long Generation, int Features) ReadSessionState()
        {
            var sessionsField = typeof(UsageTelemetrySessionRegistry).GetField(
                "_sessions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(sessionsField);
            var sessions = Assert.IsType<UsageTelemetrySession?[]>(sessionsField.GetValue(SessionRegistry));
            return Assert.Single(sessions.OfType<UsageTelemetrySession>()).ReadStateForTesting();
        }

        private bool IsCurrent(UsageTelemetryRecordingToken token) =>
            token.IsEnabled &&
            Volatile.Read(ref _enabled) != 0 &&
            token.Generation == Volatile.Read(ref _generation);

        private static TaskCompletionSource NewCapturePausedSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource CompletedCaptureSource()
        {
            var source = NewCapturePausedSource();
            source.SetResult();
            return source;
        }
    }

    private sealed class UnavailableAudioController : ISystemAudioController
    {
        public AudioState GetState() => throw new InvalidOperationException("Unavailable");

        public AudioState ToggleMute() => throw new InvalidOperationException("Unavailable");

        public AudioState SetVolume(int volume) => throw new InvalidOperationException("Unavailable");
    }
}
