namespace VolturaAir.Host;

internal enum DirectScreenQualityMode
{
    Automatic = 0,
    Quality = 1,
    DataSaver = 3
}

internal sealed record ScreenViewSettingsSnapshot(
    DirectScreenQualityMode DirectQuality = DirectScreenQualityMode.Automatic);

internal static class AppScreenViewSettings
{
    internal const string ValueName = "ScreenViewSettingsJson";
    internal static ScreenViewSettingsSnapshot Default { get; } = new();

    public static ScreenViewSettingsSnapshot Load() =>
        HostSettingsJsonValue.Load(ValueName, Default, Default, IsValid);

    public static void Save(ScreenViewSettingsSnapshot settings)
    {
        if (!IsValid(settings)) throw new ArgumentException("The Screen View settings are invalid.", nameof(settings));
        HostSettingsJsonValue.Save(ValueName, settings);
    }

    private static bool IsValid(ScreenViewSettingsSnapshot settings) =>
        Enum.IsDefined(settings.DirectQuality);
}
