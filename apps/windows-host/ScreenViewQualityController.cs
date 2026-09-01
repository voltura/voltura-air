namespace VolturaAir.Host;

internal readonly record struct ScreenViewQualityProfile(
    int Width,
    int Height,
    int FramesPerSecond,
    int RequiredBitrate,
    int TargetBitrate)
{
    public ScreenViewCaptureProfile CaptureProfile => new(Width, Height, true, FramesPerSecond);
}

internal readonly record struct ScreenViewReceiverQuality(
    int Width,
    int Height,
    double FramesPerSecond,
    int FramesDecoded,
    int FramesDropped,
    int FreezeCount,
    int PacketsLost);

internal sealed class ScreenViewQualityController
{
    private const int DirectMaximumBitrate = 100_000_000;
    private const int DataSaverMaximumBitrate = 4_000_000;
    private const int DataSaverMaximumWidth = 1920;
    private const int DataSaverMaximumHeight = 1080;
    private static readonly double[] ResolutionScales = [1d, 0.875d, 0.75d, 2d / 3d, 0.625d, 0.5d, 0.375d, 1d / 3d, 0.25d];
    private static readonly int[] FrameRates = [60, 30, 20, 15, 10, 5];
    private static readonly TimeSpan BackpressureWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackpressureChangeSpacing = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HealthyUpgradeDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UpgradeTrial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailedUpgradeCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UnsupportedProfileRetryCooldown = TimeSpan.FromSeconds(60);
    private readonly Lock _gate = new();
    private readonly DirectScreenQualityMode _mode;
    private readonly int _maximumBitrate;
    private readonly bool _isBitrateConstrained;
    private List<ScreenViewQualityProfile> _profiles;
    private DateTimeOffset[] _unsupportedUntil;
    private int _index;
    private int _consecutiveUnhealthy;
    private int _backpressureCount;
    private DateTimeOffset _backpressureWindowStarted;
    private DateTimeOffset _lastBackpressureChange = DateTimeOffset.MinValue;
    private DateTimeOffset? _healthySince;
    private int? _upgradeTrialPreviousIndex;
    private DateTimeOffset _upgradeTrialUntil;

    public ScreenViewQualityController(
        ScreenViewSource source,
        DirectScreenQualityMode mode,
        int? maximumBitrate = null,
        int reservedBitrate = 0)
    {
        _mode = mode;
        _isBitrateConstrained = maximumBitrate is not null;
        _maximumBitrate = Math.Max(500_000, Math.Min(
            maximumBitrate ?? DirectMaximumBitrate,
            mode == DirectScreenQualityMode.DataSaver ? DataSaverMaximumBitrate : DirectMaximumBitrate) - Math.Max(0, reservedBitrate));
        _profiles = CreateProfiles(source, mode, _maximumBitrate);
        _unsupportedUntil = new DateTimeOffset[_profiles.Count];
        _index = maximumBitrate is null ? FindInitialDirectProfile(source) : FindHighestAffordableProfile(_maximumBitrate);
    }

    public ScreenViewQualityProfile Current
    {
        get { lock (_gate) return _profiles[_index]; }
    }

    public bool SetSource(ScreenViewSource source)
    {
        lock (_gate)
        {
            ScreenViewQualityProfile previous = _profiles[_index];
            _profiles = CreateProfiles(source, _mode, _maximumBitrate);
            _unsupportedUntil = new DateTimeOffset[_profiles.Count];
            _index = _isBitrateConstrained ? FindHighestAffordableProfile(_maximumBitrate) : FindInitialDirectProfile(source);
            ResetObservations();
            return _profiles[_index] != previous;
        }
    }

    public bool ReportBackpressure(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_backpressureCount == 0 || now - _backpressureWindowStarted > BackpressureWindow)
            {
                _backpressureWindowStarted = now;
                _backpressureCount = 1;
                return false;
            }
            _backpressureCount++;
            if (_backpressureCount < 3) return false;
            _backpressureCount = 0;
            if (now - _lastBackpressureChange < BackpressureChangeSpacing) return false;
            if (!MoveWorse()) return false;
            _lastBackpressureChange = now;
            return true;
        }
    }

    public bool ReportReceiverQuality(ScreenViewReceiverQuality quality, DateTimeOffset now)
    {
        lock (_gate)
        {
            bool decoded = quality.FramesDecoded > 0;
            bool unhealthy = quality.FreezeCount > 0 ||
                quality.FramesDropped > Math.Max(3, quality.FramesDecoded / 10);

            _consecutiveUnhealthy = unhealthy ? _consecutiveUnhealthy + 1 : 0;
            if (_consecutiveUnhealthy >= 2)
            {
                _consecutiveUnhealthy = 0;
                _healthySince = null;
                if (_upgradeTrialPreviousIndex is int previous)
                {
                    int failedIndex = _index;
                    _index = previous;
                    _unsupportedUntil[failedIndex] = now + FailedUpgradeCooldown;
                    _upgradeTrialPreviousIndex = null;
                    return true;
                }
                return MoveWorse();
            }

            if (!decoded || unhealthy)
            {
                _healthySince = null;
                return false;
            }

            if (_upgradeTrialPreviousIndex is not null)
            {
                if (now >= _upgradeTrialUntil) _upgradeTrialPreviousIndex = null;
                return false;
            }

            _healthySince ??= now;
            if (now - _healthySince < HealthyUpgradeDelay) return false;
            _healthySince = null;
            int better = FindNextAvailableBetter(now);
            if (better < 0) return false;
            int previousIndex = _index;
            _index = better;
            _upgradeTrialPreviousIndex = previousIndex;
            _upgradeTrialUntil = now + UpgradeTrial;
            return true;
        }
    }

    public bool ReportProfileUnsupported(DateTimeOffset now)
    {
        lock (_gate)
        {
            _unsupportedUntil[_index] = now + UnsupportedProfileRetryCooldown;
            _upgradeTrialPreviousIndex = null;
            return MoveWorse();
        }
    }

    internal static List<ScreenViewQualityProfile> CreateProfiles(
        ScreenViewSource source,
        DirectScreenQualityMode mode,
        int maximumBitrate)
    {
        (int sourceWidth, int sourceHeight) = OrientedSize(source);
        (sourceWidth, sourceHeight) = FitWithinFrameLimit(sourceWidth, sourceHeight, ScreenViewH264Limits.Level52.MaximumMacroblocksPerFrame);
        (int floorWidth, int floorHeight) = ReadabilityFloor(source, sourceWidth, sourceHeight);

        if (mode == DirectScreenQualityMode.DataSaver)
        {
            double saverScale = Math.Min(1d, Math.Min(
                (double)DataSaverMaximumWidth / sourceWidth,
                (double)DataSaverMaximumHeight / sourceHeight));
            sourceWidth = Even(sourceWidth * saverScale);
            sourceHeight = Even(sourceHeight * saverScale);
            double minimumScale = Math.Min(1d, Math.Max(320d / sourceWidth, 180d / sourceHeight));
            floorWidth = Even(sourceWidth * minimumScale);
            floorHeight = Even(sourceHeight * minimumScale);
            maximumBitrate = Math.Min(maximumBitrate, DataSaverMaximumBitrate);
        }

        var dimensions = new List<(int Width, int Height)> { (sourceWidth, sourceHeight) };
        if (mode != DirectScreenQualityMode.Quality)
        {
            dimensions.AddRange(ResolutionScales
                .Select(scale => (Width: Even(sourceWidth * scale), Height: Even(sourceHeight * scale)))
                .Where(size => size.Width >= floorWidth && size.Height >= floorHeight));
            dimensions.Add((floorWidth, floorHeight));
        }

        int initialRequiredBitrate = TargetBitrate(sourceWidth, sourceHeight, 30);
        List<ScreenViewQualityProfile> candidates = [.. dimensions
            .Distinct()
            .SelectMany(size => FrameRates.Select(fps => (size.Width, size.Height, Fps: fps)))
            .Where(item => MacroblocksPerFrame(item.Width, item.Height) * item.Fps <= ScreenViewH264Limits.Level52.MaximumMacroblocksPerSecond)
            .Select(item =>
            {
                int required = TargetBitrate(item.Width, item.Height, item.Fps);
                return new ScreenViewQualityProfile(item.Width, item.Height, item.Fps, required, Math.Min(maximumBitrate, required));
            })
            .Where(profile =>
                profile.RequiredBitrate <= initialRequiredBitrate ||
                (profile.Width == sourceWidth && profile.Height == sourceHeight))
            .OrderByDescending(profile => profile.RequiredBitrate)
            .ThenByDescending(profile => (long)profile.Width * profile.Height)
            .ThenByDescending(profile => profile.FramesPerSecond)
            .Distinct()];

        var profiles = new List<ScreenViewQualityProfile>(candidates.Count);
        foreach (ScreenViewQualityProfile candidate in candidates)
        {
            if (profiles.Count == 0 || candidate.RequiredBitrate < profiles[^1].RequiredBitrate)
                profiles.Add(candidate);
        }
        if (profiles.Count == 0)
            throw new InvalidOperationException("The selected display does not permit a supported H.264 screen profile.");
        return profiles;
    }

    internal static (int Width, int Height) ReadabilityFloor(ScreenViewSource source)
    {
        (int width, int height) = OrientedSize(source);
        (width, height) = FitWithinFrameLimit(width, height, ScreenViewH264Limits.Level52.MaximumMacroblocksPerFrame);
        return ReadabilityFloor(source, width, height);
    }

    private static (int Width, int Height) ReadabilityFloor(ScreenViewSource source, int maximumWidth, int maximumHeight)
    {
        double scale = Math.Min(1d, Math.Max(96d / Math.Max(96, source.EffectiveDpiX), 96d / Math.Max(96, source.EffectiveDpiY)));
        (int orientedWidth, int orientedHeight) = OrientedSize(source);
        return (
            Math.Min(maximumWidth, Even(orientedWidth * scale)),
            Math.Min(maximumHeight, Even(orientedHeight * scale)));
    }

    private int FindInitialDirectProfile(ScreenViewSource source)
    {
        (int width, int height) = OrientedSize(source);
        (width, height) = FitWithinFrameLimit(width, height, ScreenViewH264Limits.Level52.MaximumMacroblocksPerFrame);
        int exact = _profiles.FindIndex(profile => profile.Width == width && profile.Height == height && profile.FramesPerSecond == 30);
        return exact >= 0 ? exact : FindHighestAffordableProfile(_maximumBitrate);
    }

    private int FindHighestAffordableProfile(int budget)
    {
        int found = _profiles.FindIndex(profile => profile.RequiredBitrate <= budget);
        return found < 0 ? _profiles.Count - 1 : found;
    }

    private int FindNextAvailableBetter(DateTimeOffset now)
    {
        int candidate = _index - 1;
        return candidate >= 0 && now >= _unsupportedUntil[candidate] ? candidate : -1;
    }

    private bool MoveWorse()
    {
        _healthySince = null;
        _upgradeTrialPreviousIndex = null;
        if (_index >= _profiles.Count - 1) return false;
        _index++;
        return true;
    }

    private void ResetObservations()
    {
        _consecutiveUnhealthy = 0;
        _backpressureCount = 0;
        _lastBackpressureChange = DateTimeOffset.MinValue;
        _healthySince = null;
        _upgradeTrialPreviousIndex = null;
    }

    private static int TargetBitrate(int width, int height, int framesPerSecond) =>
        (int)Math.Clamp((long)Math.Round(width * (double)height * framesPerSecond * (8_000_000d / (1920d * 1080d * 30d))), 500_000L, DirectMaximumBitrate);

    private static (int Width, int Height) OrientedSize(ScreenViewSource source) =>
        source.Rotation is ScreenViewRotation.Rotate90 or ScreenViewRotation.Rotate270
            ? (source.Height, source.Width)
            : (source.Width, source.Height);

    private static (int Width, int Height) FitWithinFrameLimit(int width, int height, int maximumMacroblocksPerFrame)
    {
        if (MacroblocksPerFrame(width, height) <= maximumMacroblocksPerFrame) return (width, height);
        double lower = 0;
        double upper = 1;
        for (int iteration = 0; iteration < 48; iteration++)
        {
            double scale = (lower + upper) / 2;
            if (MacroblocksPerFrame(Even(width * scale), Even(height * scale)) <= maximumMacroblocksPerFrame) lower = scale;
            else upper = scale;
        }
        return (Even(width * lower), Even(height * lower));
    }

    private static long MacroblocksPerFrame(int width, int height) =>
        (long)((width + 15) / 16) * ((height + 15) / 16);

    private static int Even(double value) => Math.Max(2, (int)Math.Floor(value) & ~1);
}
