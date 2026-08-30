namespace VolturaAir.Host;

public static class AppPermissionSettings
{
    internal const string ValueName = "PermissionsJson";
    internal const string DefaultAccessProfileValueName = "DefaultAccessProfileJson";
    private static HostPermissionSet MalformedPermissions { get; } = new(
        AllowRemoteInput: false,
        AllowPcSleep: false,
        AllowVolumeControl: false,
        AllowPresentationControl: false,
        AllowRemoteAppLaunch: false,
        AllowUrlOpen: false,
        AllowPcLock: false,
        AllowBlackoutDisplay: false,
        AllowDisplayControl: false,
        AllowScreenSaver: false,
        AllowAwakeControl: false,
        AllowClipboardRead: false,
        AllowScreenViewing: false,
        AllowPhoneWebcam: false,
        AllowSignOut: false,
        AllowRestart: false,
        AllowShutdown: false,
        AllowFileBrowsing: false,
        AllowFileChanges: false,
        AllowFileTransfer: false,
        AllowDiagnostics: false,
        AllowTerminal: false,
        AllowAppsControl: false,
        HideProtectedFileSystemItems: true);
    private static HostPermissionSet _cachedPermissions = HostPermissions.DefaultGlobal;
    private static int _cachedDefaultAccessProfile = (int)DeviceAccessProfile.MyDevice;

    static AppPermissionSettings()
    {
        HostSettingsRegistry.SettingsScopeChanged += RefreshCachedPermissions;
        RefreshCachedPermissions();
    }

    public static event EventHandler? Changed;

    public static HostPermissionSet Load()
    {
        return Volatile.Read(ref _cachedPermissions);
    }

    public static DeviceAccessProfile LoadDefaultAccessProfile() =>
        (DeviceAccessProfile)Volatile.Read(ref _cachedDefaultAccessProfile);

    public static void SaveDefaultAccessProfile(DeviceAccessProfile profile)
    {
        if (!DeviceAccessProfiles.IsBuiltIn(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        HostSettingsJsonValue.Save(
            DefaultAccessProfileValueName,
            new DefaultDeviceAccessSettings(profile));
        Volatile.Write(ref _cachedDefaultAccessProfile, (int)profile);
    }

    public static void Save(HostPermissionSet permissions)
    {
        var current = Load();
        HostSettingsJsonValue.Save(ValueName, permissions);
        Volatile.Write(ref _cachedPermissions, permissions);

        if (current != permissions)
        {
            NotifyChanged();
        }
    }

    private static HostPermissionSet ReadPermissions() =>
        HostSettingsJsonValue.Load(ValueName, HostPermissions.DefaultGlobal, MalformedPermissions);

    private static void RefreshCachedPermissions()
    {
        Volatile.Write(ref _cachedPermissions, ReadPermissions());
        var defaultAccess = HostSettingsJsonValue.Load(
            DefaultAccessProfileValueName,
            new DefaultDeviceAccessSettings(DeviceAccessProfile.MyDevice),
            new DefaultDeviceAccessSettings(DeviceAccessProfile.MyDevice),
            settings => DeviceAccessProfiles.IsBuiltIn(settings.Profile));
        Volatile.Write(ref _cachedDefaultAccessProfile, (int)defaultAccess.Profile);
    }

    internal static void RefreshForTests() => RefreshCachedPermissions();

    private static void NotifyChanged()
    {
        foreach (EventHandler subscriber in Changed?.GetInvocationList().Cast<EventHandler>() ?? [])
        {
            try
            {
                subscriber(null, EventArgs.Empty);
            }
            catch (Exception ex) when (!IsFatal(ex))
            {
                System.Diagnostics.Trace.TraceError(
                    "A permission-settings change subscriber failed: {0}",
                    ex.GetType().Name);
            }
        }
    }

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}

internal sealed record DefaultDeviceAccessSettings(DeviceAccessProfile Profile);
