namespace VolturaAir.Host;

internal enum DirectScreenQualityMode
{
    Automatic = 0,
    Quality = 1,
    DataSaver = 3
}

public enum ScreenViewSoundQuality
{
    High = 0,
    Standard = 1,
    Low = 2
}

internal readonly record struct ScreenViewSoundEncodingProfile(int Bitrate, int Channels);

internal static class ScreenViewSoundQualityProfile
{
    public static ScreenViewSoundEncodingProfile Encoding(ScreenViewSoundQuality quality) => quality switch
    {
        ScreenViewSoundQuality.High => new(96_000, 2),
        ScreenViewSoundQuality.Standard => new(64_000, 2),
        ScreenViewSoundQuality.Low => new(48_000, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };

    public static string ToProtocolId(ScreenViewSoundQuality quality) => quality switch
    {
        ScreenViewSoundQuality.High => "high",
        ScreenViewSoundQuality.Standard => "standard",
        ScreenViewSoundQuality.Low => "low",
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };

    public static bool TryParseProtocolId(string? value, out ScreenViewSoundQuality quality)
    {
        quality = value switch
        {
            "high" => ScreenViewSoundQuality.High,
            "standard" => ScreenViewSoundQuality.Standard,
            "low" => ScreenViewSoundQuality.Low,
            _ => default
        };
        return value is "high" or "standard" or "low";
    }

    public static ScreenViewSoundQuality ParseProtocolId(string? value)
    {
        if (TryParseProtocolId(value, out ScreenViewSoundQuality quality)) return quality;
        throw new ArgumentException("The Screen View sound quality is invalid.", nameof(value));
    }

    public static string DisplayName(ScreenViewSoundQuality quality) => quality switch
    {
        ScreenViewSoundQuality.High => "High",
        ScreenViewSoundQuality.Standard => "Standard",
        ScreenViewSoundQuality.Low => "Low",
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };
}

internal sealed record ScreenViewSettingsSnapshot(
    DirectScreenQualityMode DirectQuality = DirectScreenQualityMode.Automatic);

internal sealed record ScreenViewSoundSettingsSnapshot(
    ScreenViewSoundQuality SoundQuality = ScreenViewSoundQuality.High);

internal static class AppScreenViewSettings
{
    internal const string ValueName = "ScreenViewSettingsJson";
    internal const string SoundValueName = "ScreenViewSoundSettingsJson";
    internal static ScreenViewSettingsSnapshot Default { get; } = new();
    internal static ScreenViewSoundSettingsSnapshot SoundDefault { get; } = new();

    public static event EventHandler? SoundQualityChanged;

    public static ScreenViewSettingsSnapshot Load() =>
        HostSettingsJsonValue.Load(ValueName, Default, Default, IsValid);

    public static void Save(ScreenViewSettingsSnapshot settings)
    {
        if (!IsValid(settings)) throw new ArgumentException("The Screen View settings are invalid.", nameof(settings));
        HostSettingsJsonValue.Save(ValueName, settings);
    }

    public static ScreenViewSoundQuality LoadSoundQuality() =>
        HostSettingsJsonValue.Load(SoundValueName, SoundDefault, SoundDefault, IsValid).SoundQuality;

    public static void SaveSoundQuality(ScreenViewSoundQuality soundQuality)
    {
        var settings = new ScreenViewSoundSettingsSnapshot(soundQuality);
        if (!IsValid(settings)) throw new ArgumentException("The Screen View sound setting is invalid.", nameof(soundQuality));
        ScreenViewSoundQuality current = LoadSoundQuality();
        HostSettingsJsonValue.Save(SoundValueName, settings);
        if (current != soundQuality) SoundQualityChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsValid(ScreenViewSettingsSnapshot settings) =>
        Enum.IsDefined(settings.DirectQuality);

    private static bool IsValid(ScreenViewSoundSettingsSnapshot settings) =>
        Enum.IsDefined(settings.SoundQuality);
}
