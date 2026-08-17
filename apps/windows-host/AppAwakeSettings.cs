namespace VolturaAir.Host;

public static class AppAwakeSettings
{
    internal const string ValueName = "AwakeStateJson";
    private static AwakeState Default { get; } = new(AwakeMode.Off, false, 60, null);

    public static AwakeState Load()
    {
        AwakeState state = HostSettingsJsonValue.Load(ValueName, Default, Default, IsValid);
        if (state.Mode is AwakeMode.Timed or AwakeMode.Expiration &&
            state.ExpiresAt <= DateTimeOffset.Now)
        {
            return state with { Mode = AwakeMode.Off, ExpiresAt = null };
        }
        return state;
    }

    public static void Save(AwakeState state)
    {
        if (!IsValid(state)) throw new ArgumentException("The Awake settings are invalid.", nameof(state));
        HostSettingsJsonValue.Save(ValueName, state);
    }

    internal static int NormalizeIntervalMinutes(int value) => Math.Clamp(value, 1, 525_600);

    private static bool IsValid(AwakeState state) =>
        Enum.IsDefined(state.Mode) &&
        state.IntervalMinutes == NormalizeIntervalMinutes(state.IntervalMinutes) &&
        (state.Mode is AwakeMode.Timed or AwakeMode.Expiration
            ? state.ExpiresAt is not null
            : state.ExpiresAt is null);
}
