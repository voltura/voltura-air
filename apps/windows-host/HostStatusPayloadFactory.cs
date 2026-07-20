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

    public object CreateConnectedStatus(string clientId) => new
    {
        type = "status",
        connected = true,
        message = "Connected",
        pcName = Environment.MachineName,
        capabilities = CreateCapabilities(clientId),
        host = CreateHostStatus(clientId)
    };

    public object CreatePairAccepted(string clientId) => new
    {
        type = "pair.accepted",
        clientId,
        pcName = Environment.MachineName,
        paired = true,
        capabilities = CreateCapabilities(clientId),
        host = CreateHostStatus(clientId)
    };

    public object CreateDisconnectedStatus(string clientId, string message) => new
    {
        type = "status",
        connected = false,
        message,
        pcName = Environment.MachineName,
        capabilities = CreateCapabilities(clientId),
        host = CreateHostStatus(clientId)
    };

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

    private object CreateCapabilities(string clientId) => new
    {
        sleep = CanSleepPc(clientId),
        remoteInput = CanUseRemoteInput(clientId),
        power = CreatePowerCapabilities(clientId),
        awake = CreateAwakeCapability(clientId),
        volume = CanControlVolume(clientId),
        presentation = AppDeveloperSettings.EnableAlphaFeatures()
            ? new { canControl = CanControlPresentations(clientId) }
            : null,
        remoteLaunch = CanLaunchRemoteApps(clientId),
        urlOpen = new { canOpen = CanOpenUrls(clientId) },
        textTransfer = CanUseRemoteInput(clientId),
        clipboardRead = CanReadClipboard(clientId),
        gestureDebug = AppDeveloperSettings.EnableGestureDebug(),
        inputAck = true
    };

    private object CreatePowerCapabilities(string clientId)
    {
        var permissions = GetEffectivePermissions(clientId);
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

    private object CreateAwakeCapability(string clientId)
    {
        var state = awakeService.State;
        return new
        {
            canControl = CanControlAwake(clientId),
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

    private HostStatusMetadata CreateHostStatus(string clientId)
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
            CanLaunchRemoteApps(clientId) ? appLaunchService.GetActions() : [],
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
