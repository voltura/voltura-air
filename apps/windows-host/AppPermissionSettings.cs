namespace VolturaAir.Host;

public static class AppPermissionSettings
{
    internal const string ValueName = "PermissionsJson";
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
        HideProtectedFileSystemItems: true);
    private static HostPermissionSet _cachedPermissions = HostPermissions.DefaultGlobal;

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
