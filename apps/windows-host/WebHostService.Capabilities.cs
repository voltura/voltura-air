namespace VolturaAir.Host;

public sealed partial class WebHostService
{
    private object CreateCapabilities(string clientId)
    {
        return new
        {
            sleep = CanSleepPc(clientId),
            power = CreatePowerCapabilities(clientId),
            awake = CreateAwakeCapability(clientId),
            volume = CanControlVolume(clientId),
            presentation = CreatePresentationCapability(clientId),
            remoteLaunch = CanLaunchRemoteApps(clientId),
            urlOpen = new { canOpen = CanOpenUrls(clientId) },
            textTransfer = true,
            clipboardRead = CanReadClipboard(clientId),
            gestureDebug = AppDeveloperSettings.EnableGestureDebug(),
            inputAck = true
        };
    }

    private bool CanSleepPc(string clientId)
    {
        return GetEffectivePermissions(clientId).AllowPcSleep;
    }

    private bool CanControlVolume(string clientId)
    {
        return GetEffectivePermissions(clientId).AllowVolumeControl;
    }

    private bool CanControlPresentations(string clientId)
    {
        return GetEffectivePermissions(clientId).AllowPresentationControl;
    }

    private object? CreatePresentationCapability(string clientId)
    {
        return AlphaFeaturesEnabled
            ? new { canControl = CanControlPresentations(clientId) }
            : null;
    }

    private static bool AlphaFeaturesEnabled => AppDeveloperSettings.EnableAlphaFeatures();

    private bool CanLaunchRemoteApps(string clientId)
    {
        return GetEffectivePermissions(clientId).AllowRemoteAppLaunch;
    }

    private bool CanOpenUrls(string clientId)
    {
        return GetEffectivePermissions(clientId).AllowUrlOpen;
    }

    private bool CanReadClipboard(string clientId)
    {
        return GetEffectivePermissions(clientId).AllowClipboardRead;
    }

    private HostPermissionSet GetEffectivePermissions(string clientId)
    {
        return _pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load());
    }
}
