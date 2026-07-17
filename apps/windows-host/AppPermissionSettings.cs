using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppPermissionSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string AllowPcSleepValueName = "AllowPcSleep";
    private const string AllowVolumeControlValueName = "AllowVolumeControl";
    private const string AllowPresentationControlValueName = "AllowPresentationControl";
    private const string AllowRemoteAppLaunchValueName = "AllowRemoteAppLaunch";
    private const string AllowUrlOpenValueName = "AllowUrlOpen";
    private const string AllowPcLockValueName = "AllowPcLock";
    private const string AllowBlackoutDisplayValueName = "AllowBlackoutDisplay";
    private const string AllowDisplayOffValueName = "AllowDisplayOff";
    private const string AllowScreenSaverValueName = "AllowScreenSaver";
    private const string AllowAwakeControlValueName = "AllowAwakeControl";
    private const string AllowClipboardReadValueName = "AllowClipboardRead";
    private const string AllowSignOutValueName = "AllowSignOut";
    private const string AllowRestartValueName = "AllowRestart";
    private const string AllowShutdownValueName = "AllowShutdown";

    public static event EventHandler? Changed;

    public static HostPermissionSet Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return new HostPermissionSet(
            AllowPcSleep: GetBooleanValue(key, AllowPcSleepValueName, HostPermissions.DefaultGlobal.AllowPcSleep),
            AllowVolumeControl: GetBooleanValue(key, AllowVolumeControlValueName, HostPermissions.DefaultGlobal.AllowVolumeControl),
            AllowPresentationControl: GetBooleanValue(key, AllowPresentationControlValueName, HostPermissions.DefaultGlobal.AllowPresentationControl),
            AllowRemoteAppLaunch: GetBooleanValue(key, AllowRemoteAppLaunchValueName, HostPermissions.DefaultGlobal.AllowRemoteAppLaunch),
            AllowUrlOpen: GetBooleanValue(key, AllowUrlOpenValueName, HostPermissions.DefaultGlobal.AllowUrlOpen),
            AllowPcLock: GetBooleanValue(key, AllowPcLockValueName, HostPermissions.DefaultGlobal.AllowPcLock),
            AllowBlackoutDisplay: GetBooleanValue(key, AllowBlackoutDisplayValueName, HostPermissions.DefaultGlobal.AllowBlackoutDisplay),
            AllowDisplayOff: GetBooleanValue(key, AllowDisplayOffValueName, HostPermissions.DefaultGlobal.AllowDisplayOff),
            AllowScreenSaver: GetBooleanValue(key, AllowScreenSaverValueName, HostPermissions.DefaultGlobal.AllowScreenSaver),
            AllowAwakeControl: GetBooleanValue(key, AllowAwakeControlValueName, HostPermissions.DefaultGlobal.AllowAwakeControl),
            AllowClipboardRead: GetBooleanValue(key, AllowClipboardReadValueName, HostPermissions.DefaultGlobal.AllowClipboardRead),
            AllowSignOut: GetBooleanValue(key, AllowSignOutValueName, HostPermissions.DefaultGlobal.AllowSignOut),
            AllowRestart: GetBooleanValue(key, AllowRestartValueName, HostPermissions.DefaultGlobal.AllowRestart),
            AllowShutdown: GetBooleanValue(key, AllowShutdownValueName, HostPermissions.DefaultGlobal.AllowShutdown));
    }

    public static void Save(HostPermissionSet permissions)
    {
        var current = Load();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);

        key.SetValue(AllowPcSleepValueName, permissions.AllowPcSleep ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowVolumeControlValueName, permissions.AllowVolumeControl ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowPresentationControlValueName, permissions.AllowPresentationControl ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowRemoteAppLaunchValueName, permissions.AllowRemoteAppLaunch ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowUrlOpenValueName, permissions.AllowUrlOpen ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowPcLockValueName, permissions.AllowPcLock ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowBlackoutDisplayValueName, permissions.AllowBlackoutDisplay ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowDisplayOffValueName, permissions.AllowDisplayOff ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowScreenSaverValueName, permissions.AllowScreenSaver ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowAwakeControlValueName, permissions.AllowAwakeControl ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowClipboardReadValueName, permissions.AllowClipboardRead ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowSignOutValueName, permissions.AllowSignOut ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowRestartValueName, permissions.AllowRestart ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowShutdownValueName, permissions.AllowShutdown ? 1 : 0, RegistryValueKind.DWord);

        if (current != permissions)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static bool GetBooleanValue(RegistryKey? key, string valueName, bool defaultValue)
    {
        return key?.GetValue(valueName) is int value ? value != 0 : defaultValue;
    }
}
