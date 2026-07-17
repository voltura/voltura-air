using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppDeveloperSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string DeveloperModeValueName = "DeveloperMode";
    private const string EnableAlphaFeaturesValueName = "EnableAlphaFeatures";
    private const string EnableGestureDebugValueName = "EnableGestureDebug";
    private static int _alphaFeaturesEnabled;

    static AppDeveloperSettings()
    {
        HostSettingsRegistry.SettingsScopeChanged += RefreshCachedAlphaFeatures;
        RefreshCachedAlphaFeatures();
    }

    public static event EventHandler? Changed;

    public static bool EnableGestureDebug()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(EnableGestureDebugValueName) is int value && value != 0;
    }

    public static bool EnableAlphaFeatures()
    {
        return Volatile.Read(ref _alphaFeaturesEnabled) != 0;
    }

    public static bool DeveloperMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(DeveloperModeValueName) is int value && value != 0;
    }

    public static void SetEnableGestureDebug(bool enabled)
    {
        var current = EnableGestureDebug();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(EnableGestureDebugValueName, enabled ? 1 : 0, RegistryValueKind.DWord);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void SetEnableAlphaFeatures(bool enabled)
    {
        var current = EnableAlphaFeatures();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(EnableAlphaFeaturesValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        Volatile.Write(ref _alphaFeaturesEnabled, enabled ? 1 : 0);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void RefreshCachedAlphaFeatures()
    {
        Volatile.Write(ref _alphaFeaturesEnabled, ReadAlphaFeaturesEnabled() ? 1 : 0);
    }

    private static bool ReadAlphaFeaturesEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            return key?.GetValue(EnableAlphaFeaturesValueName) is int value && value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static void SetDeveloperMode(bool enabled)
    {
        var current = DeveloperMode();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(DeveloperModeValueName, enabled ? 1 : 0, RegistryValueKind.DWord);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
