namespace VolturaAir.Host;

internal sealed class HostStatusPayloadFactory(
    PairingManager pairingManager,
    ISystemPowerController powerController,
    IAwakeService awakeService,
    IWorkstationLockPolicy workstationLockPolicy,
    IAppLaunchService appLaunchService,
    ITextDestinationService textDestinationService,
    Func<HostNetworkSnapshot> getNetwork,
    Func<bool> isInputBlockedByElevation)
{
    private static readonly string DeveloperSessionId = Guid.NewGuid().ToString("N");

    public object CreateConnectedStatus(string clientId)
    {
        var permissions = GetEffectivePermissions(clientId);
        return new
        {
            type = "status",
            connected = true,
            message = "Connected",
            pcName = Environment.MachineName,
            capabilities = CreateCapabilities(permissions),
            host = CreateHostStatus(clientId, permissions)
        };
    }

    public object CreatePairAccepted(string clientId)
    {
        var permissions = GetEffectivePermissions(clientId);
        return new
        {
            type = "pair.accepted",
            clientId,
            pcName = Environment.MachineName,
            paired = true,
            capabilities = CreateCapabilities(permissions),
            host = CreateHostStatus(clientId, permissions)
        };
    }

    public object CreateDisconnectedStatus(string clientId, string message)
    {
        var permissions = GetEffectivePermissions(clientId);
        return new
        {
            type = "status",
            connected = false,
            message,
            pcName = Environment.MachineName,
            capabilities = CreateCapabilities(permissions),
            host = CreateHostStatus(clientId, permissions)
        };
    }

    public bool CanSleepPc(string clientId) => GetEffectivePermissions(clientId).AllowPcSleep;
    public bool CanUseRemoteInput(string clientId) => GetEffectivePermissions(clientId).AllowRemoteInput;
    public bool CanControlVolume(string clientId) => GetEffectivePermissions(clientId).AllowVolumeControl;
    public bool CanControlPresentations(string clientId) => GetEffectivePermissions(clientId).AllowPresentationControl;
    public bool CanLaunchRemoteApps(string clientId) => GetEffectivePermissions(clientId).AllowRemoteAppLaunch;
    public bool CanOpenUrls(string clientId) => GetEffectivePermissions(clientId).AllowUrlOpen;
    public bool CanReadClipboard(string clientId) => GetEffectivePermissions(clientId).AllowClipboardRead;
    public bool CanControlAwake(string clientId) => GetEffectivePermissions(clientId).AllowAwakeControl;
    public HostPermissionSet GetEffectivePermissions(string clientId) =>
        pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load());

    private object CreateCapabilities(HostPermissionSet permissions) => new
    {
        sleep = permissions.AllowPcSleep,
        remoteInput = permissions.AllowRemoteInput,
        power = CreatePowerCapabilities(permissions),
        awake = CreateAwakeCapability(permissions),
        volume = permissions.AllowVolumeControl,
        presentation = AppDeveloperSettings.EnableAlphaFeatures()
            ? new { canControl = permissions.AllowPresentationControl }
            : null,
        remoteLaunch = permissions.AllowRemoteAppLaunch,
        urlOpen = new { canOpen = permissions.AllowUrlOpen },
        textTransfer = permissions.AllowRemoteInput,
        clipboardRead = permissions.AllowClipboardRead,
        gestureDebug = AppDeveloperSettings.EnableGestureDebug(),
        inputAck = true
    };

    private object CreatePowerCapabilities(HostPermissionSet permissions)
    {
        var lockStatus = workstationLockPolicy.GetStatus();
        return new
        {
            @lock = permissions.AllowPcLock,
            lockAvailability = ToProtocolLockAvailability(lockStatus.State),
            blackoutDisplay = permissions.AllowBlackoutDisplay,
            displayOff = permissions.AllowDisplayOff,
            screenSaver = permissions.AllowScreenSaver,
            screenSaverAvailable = powerController.IsActionAvailable(SystemPowerActions.ScreenSaver),
            signOut = permissions.AllowSignOut,
            restart = permissions.AllowRestart,
            shutdown = permissions.AllowShutdown
        };
    }

    private object CreateAwakeCapability(HostPermissionSet permissions)
    {
        var state = awakeService.State;
        return new
        {
            canControl = permissions.AllowAwakeControl,
            active = state.IsActive,
            mode = state.Mode switch
            {
                AwakeMode.Indefinite => "indefinite",
                AwakeMode.Timed => "timed",
                AwakeMode.Expiration => "expiration",
                _ => "off"
            },
            expiresAt = state.ExpiresAt?.ToUniversalTime().ToString("O")
        };
    }

    private HostStatusMetadata CreateHostStatus(string clientId, HostPermissionSet permissions)
    {
        var network = getNetwork();
        var developerMode = AppDeveloperSettings.DeveloperMode();
        var webClientBuildId = WebHostStaticFiles.ReadWebClientBuildId(WebHostStaticFiles.ResolveStaticRoot());
        var textDestination = textDestinationService.GetMetadata();
        return new HostStatusMetadata(
            AppVersion.Display,
            webClientBuildId,
            Environment.MachineName,
            network.SelectedAdapterName,
            network.AdvertisedHostAddress,
            network.Port,
            network.WebSocketUrl,
            AppRemoteSettings.ToProtocolId(AppRemoteSettings.GetDefaultRemoteMode()),
            permissions.AllowRemoteAppLaunch ? appLaunchService.GetActions() : [],
            new TextTransferTargetMetadata(textDestination.Mode, textDestination.DisplayName, textDestination.Available),
            pairingManager.GetDevicePointerSpeed(clientId),
            AppPointerSettings.GetCustomPointer().Enabled,
            pairingManager.GetDeviceShowModeButtons(clientId),
            developerMode,
            developerMode ? DeveloperSessionId : null,
            isInputBlockedByElevation());
    }

    private static string ToProtocolLockAvailability(WorkstationLockPolicyState state) => state switch
    {
        WorkstationLockPolicyState.NotExplicitlyDisabled => "notExplicitlyDisabled",
        WorkstationLockPolicyState.Disabled => "disabledByPolicy",
        _ => "unavailable"
    };
}

internal sealed record HostNetworkSnapshot(
    string SelectedAdapterName,
    string AdvertisedHostAddress,
    int Port,
    string WebSocketUrl);
