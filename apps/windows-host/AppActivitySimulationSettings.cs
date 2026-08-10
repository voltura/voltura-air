using Microsoft.Win32;

namespace VolturaAir.Host;

internal static class AppActivitySimulationSettings
{
    private const string EnabledValueName = "SimulateActivityEnabled";

    public static bool Load() => Load(OpenSettingsKey);

    internal static bool Load(Func<RegistryKey?> openKey)
    {
        try
        {
            using var key = openKey();
            return key?.GetValue(EnabledValueName) is int value && value == 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static void Save(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue(EnabledValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    private static RegistryKey? OpenSettingsKey() =>
        Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
}
