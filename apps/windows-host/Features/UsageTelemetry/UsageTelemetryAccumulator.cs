namespace VolturaAir.Host.Features.UsageTelemetry;

internal sealed class UsageTelemetryAccumulator
{
    private const int MaximumCount = ushort.MaxValue;
    private int _hostStarts;
    private int _standardLocal;
    private int _enhancedDirect;
    private int _relay;
    private int _trackpad;
    private int _keyboard;
    private int _dictation;
    private int _mediaControls;
    private int _presentation;
    private int _customScreens;
    private int _files;
    private int _screenViewing;
    private int _phoneWebcam;
    private int _gyroMouse;
    private int _overflowed;
    private int _activeWriters;
    private int _sealed;

    public void RecordHostStart() => SaturatingIncrement(ref _hostStarts, 1);

    public bool TryRecordConnection(UsageConnectionMethod method)
    {
        if (!TryBeginWrite())
        {
            return false;
        }

        try
        {
            switch (method)
            {
                case UsageConnectionMethod.StandardLocal:
                    SaturatingIncrement(ref _standardLocal, MaximumCount);
                    break;
                case UsageConnectionMethod.EnhancedDirect:
                    SaturatingIncrement(ref _enhancedDirect, MaximumCount);
                    break;
                case UsageConnectionMethod.Relay:
                    SaturatingIncrement(ref _relay, MaximumCount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported connection method.");
            }
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _activeWriters);
        }
    }

    public bool TryRecordFeature(UsageFeature feature)
    {
        if (!TryBeginWrite())
        {
            return false;
        }

        try
        {
            switch (feature)
            {
                case UsageFeature.Trackpad:
                    SaturatingIncrement(ref _trackpad, MaximumCount);
                    break;
                case UsageFeature.Keyboard:
                    SaturatingIncrement(ref _keyboard, MaximumCount);
                    break;
                case UsageFeature.Dictation:
                    SaturatingIncrement(ref _dictation, MaximumCount);
                    break;
                case UsageFeature.MediaControls:
                    SaturatingIncrement(ref _mediaControls, MaximumCount);
                    break;
                case UsageFeature.Presentation:
                    SaturatingIncrement(ref _presentation, MaximumCount);
                    break;
                case UsageFeature.CustomScreens:
                    SaturatingIncrement(ref _customScreens, MaximumCount);
                    break;
                case UsageFeature.Files:
                    SaturatingIncrement(ref _files, MaximumCount);
                    break;
                case UsageFeature.ScreenViewing:
                    SaturatingIncrement(ref _screenViewing, MaximumCount);
                    break;
                case UsageFeature.PhoneWebcam:
                    SaturatingIncrement(ref _phoneWebcam, MaximumCount);
                    break;
                case UsageFeature.GyroMouse:
                    SaturatingIncrement(ref _gyroMouse, MaximumCount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unsupported usage feature.");
            }
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _activeWriters);
        }
    }

    public UsageTelemetrySnapshot Seal(Guid installationId, Guid batchId, string hostVersion)
    {
        CloseAndWaitForWriters();
        var batch = new UsageTelemetryBatch(
            1,
            installationId,
            batchId,
            hostVersion,
            ReadAndClear(ref _hostStarts),
            new UsageTelemetryConnections(
                ReadAndClear(ref _standardLocal),
                ReadAndClear(ref _enhancedDirect),
                ReadAndClear(ref _relay)),
            new UsageTelemetryFeatures(
                ReadAndClear(ref _trackpad),
                ReadAndClear(ref _keyboard),
                ReadAndClear(ref _dictation),
                ReadAndClear(ref _mediaControls),
                ReadAndClear(ref _presentation),
                ReadAndClear(ref _customScreens),
                ReadAndClear(ref _files),
                ReadAndClear(ref _screenViewing),
                ReadAndClear(ref _phoneWebcam),
                ReadAndClear(ref _gyroMouse)));
        return new UsageTelemetrySnapshot(batch, Interlocked.Exchange(ref _overflowed, 0) != 0);
    }

    public void Clear()
    {
        _ = Seal(Guid.Empty, Guid.Empty, string.Empty);
    }

    private void SaturatingIncrement(ref int counter, int maximum)
    {
        if (Interlocked.Increment(ref counter) > maximum)
        {
            Volatile.Write(ref _overflowed, 1);
            Interlocked.Exchange(ref counter, maximum);
        }
    }

    private bool TryBeginWrite()
    {
        if (Volatile.Read(ref _sealed) != 0)
        {
            return false;
        }

        Interlocked.Increment(ref _activeWriters);
        if (Volatile.Read(ref _sealed) == 0)
        {
            return true;
        }

        Interlocked.Decrement(ref _activeWriters);
        return false;
    }

    private void CloseAndWaitForWriters()
    {
        Volatile.Write(ref _sealed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _activeWriters) != 0)
        {
            spinner.SpinOnce();
        }
    }

    private static ushort ReadAndClear(ref int counter) => checked((ushort)Interlocked.Exchange(ref counter, 0));
}

internal sealed record UsageTelemetrySnapshot(UsageTelemetryBatch Batch, bool Overflowed);
