namespace VolturaAir.Host.Features.UsageTelemetry;

internal sealed class UsageTelemetrySessionRegistry
{
    internal const int Capacity = 64;
    private readonly UsageTelemetrySession?[] _sessions = new UsageTelemetrySession[Capacity];

    public bool TryRegister(UsageTelemetrySession session)
    {
        for (var index = 0; index < _sessions.Length; index++)
        {
            if (Interlocked.CompareExchange(ref _sessions[index], session, null) is null)
            {
                session.Activate();
                return true;
            }
        }

        return false;
    }

    public void Unregister(UsageTelemetrySession session)
    {
        session.Deactivate();
        for (var index = 0; index < _sessions.Length; index++)
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _sessions[index], null, session), session))
            {
                return;
            }
        }
    }

    public void ResetThrough(long generation)
    {
        for (var index = 0; index < _sessions.Length; index++)
        {
            Volatile.Read(ref _sessions[index])?.ResetThrough(generation);
        }
    }
}

internal sealed class UsageTelemetrySession(IUsageTelemetryRecorder recorder) : IDisposable
{
    private readonly IUsageTelemetryRecorder _recorder = recorder;
    private UsageTelemetrySessionRegistry? _registry;
    private long _closedThroughGeneration;
    private long _recordingGeneration;
    private int _recordedFeatures;
    private int _activeWriters;
    private int _registered;
    private int _disposed;

    public void RecordInputCommand(
        InputCommandKind kind,
        InputCommandContext? context,
        UsageTelemetryRecordingToken token)
    {
        var feature = (kind, context) switch
        {
            (InputCommandKind.PointerMove or InputCommandKind.PointerButton or
                InputCommandKind.PointerWheel or InputCommandKind.PointerZoom,
                InputCommandContext.Trackpad) => UsageFeature.Trackpad,
            (InputCommandKind.PointerMove or InputCommandKind.PointerButton or
                InputCommandKind.PointerWheel or InputCommandKind.PointerZoom,
                InputCommandContext.GyroMouse) => UsageFeature.GyroMouse,
            (InputCommandKind.PointerMove or InputCommandKind.PointerButton or
                InputCommandKind.PointerWheel or InputCommandKind.PointerZoom or
                InputCommandKind.KeyboardText or InputCommandKind.KeyboardSpecial,
                InputCommandContext.Keyboard) => UsageFeature.Keyboard,
            (InputCommandKind.KeyboardText, InputCommandContext.Dictation) => UsageFeature.Dictation,
            (InputCommandKind.KeyboardSpecial, InputCommandContext.MediaControls) => UsageFeature.MediaControls,
            _ => (UsageFeature?)null
        };
        if (feature is { } value)
        {
            RecordOnce(value, token);
        }
    }

    public void RecordOnce(UsageFeature feature, UsageTelemetryRecordingToken token)
    {
        if (!token.IsEnabled)
        {
            return;
        }

        var mask = (int)UsageFeatureMasks.For(feature);
        if (Volatile.Read(ref _recordingGeneration) == token.Generation &&
            (Volatile.Read(ref _recordedFeatures) & mask) != 0)
        {
            return;
        }

        if (!TryEnterWriter(token))
        {
            return;
        }

        try
        {
            if (!TrySelectGeneration(token.Generation) ||
                (Volatile.Read(ref _recordedFeatures) & mask) != 0)
            {
                return;
            }

            if (_recorder.TryRecordFeature(feature, token))
            {
                Interlocked.Or(ref _recordedFeatures, mask);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeWriters);
        }
    }

    public bool NeedsRecord(UsageFeature feature, UsageTelemetryRecordingToken token)
    {
        if (!token.IsEnabled ||
            Volatile.Read(ref _registered) == 0 ||
            Volatile.Read(ref _disposed) != 0 ||
            token.Generation <= Volatile.Read(ref _closedThroughGeneration))
        {
            return false;
        }

        var mask = (int)UsageFeatureMasks.For(feature);
        return Volatile.Read(ref _recordingGeneration) != token.Generation ||
            (Volatile.Read(ref _recordedFeatures) & mask) == 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var registry = Interlocked.Exchange(ref _registry, null);
        if (registry is not null)
        {
            registry.Unregister(this);
        }
        else
        {
            Deactivate();
        }
    }

    internal bool TryRegister(UsageTelemetrySessionRegistry registry)
    {
        if (Volatile.Read(ref _disposed) != 0 || !registry.TryRegister(this))
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _registry, registry, null) is not null)
        {
            registry.Unregister(this);
            return false;
        }

        if (Volatile.Read(ref _disposed) == 0)
        {
            return true;
        }

        if (ReferenceEquals(Interlocked.Exchange(ref _registry, null), registry))
        {
            registry.Unregister(this);
        }
        return false;
    }

    internal void Activate() => Volatile.Write(ref _registered, 1);

    internal void Deactivate()
    {
        Volatile.Write(ref _registered, 0);
        WaitForWritersAndClear();
    }

    internal void ResetThrough(long generation)
    {
        CloseThrough(generation);
        WaitForWritersAndClear();
    }

    internal (long Generation, int Features) ReadStateForTesting() =>
        (Volatile.Read(ref _recordingGeneration), Volatile.Read(ref _recordedFeatures));

    private bool TryEnterWriter(UsageTelemetryRecordingToken token)
    {
        if (Volatile.Read(ref _registered) == 0 ||
            Volatile.Read(ref _disposed) != 0 ||
            token.Generation <= Volatile.Read(ref _closedThroughGeneration))
        {
            return false;
        }

        Interlocked.Increment(ref _activeWriters);
        if (Volatile.Read(ref _registered) != 0 &&
            Volatile.Read(ref _disposed) == 0 &&
            token.Generation > Volatile.Read(ref _closedThroughGeneration))
        {
            return true;
        }

        Interlocked.Decrement(ref _activeWriters);
        return false;
    }

    private bool TrySelectGeneration(long generation)
    {
        var current = Volatile.Read(ref _recordingGeneration);
        if (current == generation)
        {
            return true;
        }

        if (current != 0)
        {
            return false;
        }

        var observed = Interlocked.CompareExchange(ref _recordingGeneration, generation, 0);
        return observed == 0 || observed == generation;
    }

    private void CloseThrough(long generation)
    {
        while (true)
        {
            var current = Volatile.Read(ref _closedThroughGeneration);
            if (current >= generation ||
                Interlocked.CompareExchange(ref _closedThroughGeneration, generation, current) == current)
            {
                return;
            }
        }
    }

    private void WaitForWritersAndClear()
    {
        var spinner = new SpinWait();
        while (Volatile.Read(ref _activeWriters) != 0)
        {
            spinner.SpinOnce();
        }

        Volatile.Write(ref _recordedFeatures, 0);
        Volatile.Write(ref _recordingGeneration, 0);
    }
}
