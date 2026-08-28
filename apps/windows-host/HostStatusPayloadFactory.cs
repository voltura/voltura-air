using VolturaAir.Host.Features.PhoneWebcam;
using VolturaAir.Host.Features.AiAssistant;

namespace VolturaAir.Host;

internal sealed class HostStatusPayloadFactory(
    PairingManager pairingManager,
    ISystemPowerController powerController,
    IAwakeService awakeService,
    IWorkstationLockPolicy workstationLockPolicy,
    IAppLaunchService appLaunchService,
    CustomScreenService customScreenService,
    ITextDestinationService textDestinationService,
    Func<HostNetworkSnapshot> getNetwork,
    Func<bool> isInputBlockedByElevation,
    Func<bool> isPresentationLaserPointerEnabled,
    Func<PresentationLaserColor?> getPresentationLaserPointerColor,
    Func<PresentationBlankOverlaySnapshot?> getPresentationBlank,
    Func<PowerPointAutomationSnapshot> getPowerPointSnapshot,
    Func<PowerPointSessionSnapshot> getPowerPointSession,
    PowerPointPresentationCatalog presentationCatalog,
    Func<bool>? enhancedCapabilitiesEnabled = null,
    Func<PhoneWebcamFeatureStatus>? getPhoneWebcamStatus = null,
    Func<bool>? isPhoneMicrophoneAvailable = null,
    Func<string, TerminalCapabilityState>? getTerminalStatus = null,
    Func<string, AiAssistantCapabilityState>? getAiAssistantStatus = null)
{
    private static readonly string DeveloperSessionId = Guid.NewGuid().ToString("N");
    private const int LegacyScreenViewWidthMarker = 1920;
    private const int LegacyScreenViewHeightMarker = 1080;
    private const int LegacyScreenViewFrameRateMarker = 30;

    public object CreateConnectedStatus(string clientId)
    {
        var permissions = GetEffectivePermissions(clientId);
        return new
        {
            type = "status",
            connected = true,
            message = "Connected",
            pcName = ProtocolStringLimits.Limit(Environment.MachineName, ProtocolStringLimits.PcName),
            capabilities = CreateCapabilities(clientId, permissions),
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
            pcName = ProtocolStringLimits.Limit(Environment.MachineName, ProtocolStringLimits.PcName),
            paired = true,
            hostIdentity = new { publicKey = pairingManager.HostIdentity.PublicKey, fingerprint = pairingManager.HostIdentity.Fingerprint },
            capabilities = CreateCapabilities(clientId, permissions),
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
            message = ProtocolStringLimits.Limit(message, ProtocolStringLimits.HumanMessage),
            pcName = ProtocolStringLimits.Limit(Environment.MachineName, ProtocolStringLimits.PcName),
            capabilities = CreateCapabilities(clientId, permissions),
            host = CreateHostStatus(clientId, permissions)
        };
    }

    public bool CanSleepPc(string clientId) => GetEffectivePermissions(clientId).AllowPcSleep;
    public bool CanUseRemoteInput(string clientId) => GetEffectivePermissions(clientId).AllowRemoteInput;
    public (bool CanUseRemoteInput, bool CanControlHostApplication) GetInputAccess(string clientId)
    {
        var access = pairingManager.GetInputAccess(clientId, AppPermissionSettings.Load());
        return (
            access.AllowRemoteInput,
            AppClientControlSettings.AllowsDevice(access.AccessProfile));
    }
    public bool CanControlHostApplication(string clientId) =>
        AppClientControlSettings.AllowsDevice(pairingManager.GetDeviceAccessProfile(clientId));
    public bool CanControlVolume(string clientId) => GetEffectivePermissions(clientId).AllowVolumeControl;
    public bool CanControlPresentations(string clientId) => GetEffectivePermissions(clientId).AllowPresentationControl;
    public bool CanUseCustomScreen(string clientId, string screenId) =>
        customScreenService.Find(screenId)?.AssignedClientIds.Contains(clientId, StringComparer.Ordinal) == true;
    internal PowerPointAutomationSnapshot GetPowerPointSnapshot() => getPowerPointSnapshot();
    public bool CanLaunchRemoteApps(string clientId) => GetEffectivePermissions(clientId).AllowRemoteAppLaunch;
    public bool CanOpenUrls(string clientId) => GetEffectivePermissions(clientId).AllowUrlOpen;
    public bool CanReadClipboard(string clientId) => GetEffectivePermissions(clientId).AllowClipboardRead;
    public bool CanViewDiagnostics(string clientId) => GetEffectivePermissions(clientId).AllowDiagnostics;
    public bool CanUseTerminal(string clientId) =>
        pairingManager.HasCurrentHostIdentity(clientId) && GetEffectivePermissions(clientId).AllowTerminal;
    public bool CanUseAiAssistant(string clientId) =>
        pairingManager.HasCurrentHostIdentity(clientId) &&
        pairingManager.GetDeviceAccessProfile(clientId) == DeviceAccessProfile.MyDevice;
    public bool CanBrowseFiles(string clientId) => GetEffectivePermissions(clientId).AllowFileBrowsing;
    public bool CanChangeFiles(string clientId) => GetEffectivePermissions(clientId).AllowFileBrowsing && GetEffectivePermissions(clientId).AllowFileChanges;
    public bool CanTransferFiles(string clientId) =>
        pairingManager.HasCurrentHostIdentity(clientId) && GetEffectivePermissions(clientId).AllowFileTransfer;
    public bool HideProtectedFileSystemItems(string clientId) => GetEffectivePermissions(clientId).HideProtectedFileSystemItems;
    public bool CanViewScreen(string clientId) =>
        pairingManager.HasCurrentHostIdentity(clientId) &&
        GetEffectivePermissions(clientId).AllowScreenViewing;
    public bool CanUsePhoneWebcam(string clientId) =>
        pairingManager.HasCurrentHostIdentity(clientId) &&
        GetEffectivePermissions(clientId).AllowPhoneWebcam &&
        getPhoneWebcamStatus?.Invoke().IsInstalled == true;
    public bool CanControlAwake(string clientId) => GetEffectivePermissions(clientId).AllowAwakeControl;
    public HostPermissionSet GetEffectivePermissions(string clientId) =>
        pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load());

    private object CreateCapabilities(string clientId, HostPermissionSet permissions) => new
    {
        sleep = permissions.AllowPcSleep,
        remoteInput = permissions.AllowRemoteInput,
        power = CreatePowerCapabilities(permissions),
        awake = CreateAwakeCapability(permissions),
        volume = permissions.AllowVolumeControl,
        presentation = new
        {
            canControl = permissions.AllowPresentationControl,
            canSaveReports = permissions.AllowPresentationControl,
            laserPointerActive = isPresentationLaserPointerEnabled(),
            laserPointerColor = ToLaserColor(getPresentationLaserPointerColor()),
            laserPointerDefaultColor = ToLaserColor(
                AppPointerSettings.GetPresentationLaserPointer().Color),
            powerPoint = permissions.AllowPresentationControl
                ? CreatePowerPointCapability(clientId)
                : null
        },
        remoteLaunch = permissions.AllowRemoteAppLaunch,
        customScreens = new
        {
            catalogRevision = customScreenService.CatalogRevision,
            screens = customScreenService.GetAssignedSummaries(clientId)
        },
        urlOpen = new { canOpen = permissions.AllowUrlOpen },
        textTransfer = permissions.AllowRemoteInput,
        clipboardRead = permissions.AllowClipboardRead,
        diagnostics = new { canView = permissions.AllowDiagnostics },
        terminal = CreateTerminalCapability(clientId, permissions),
        aiAssistant = CreateAiAssistantCapability(clientId),
        gestureDebug = AppDeveloperSettings.EnableGestureDebug(),
        inputAck = true,
        inputContextV1 = true,
        enhancedCapabilities = new { enabled = enhancedCapabilitiesEnabled?.Invoke() == true },
        screenView = new
        {
            enabled = true,
            permissionGranted = permissions.AllowScreenViewing,
            canView = permissions.AllowScreenViewing && pairingManager.HasCurrentHostIdentity(clientId),
            requiresRepair = !pairingManager.HasCurrentHostIdentity(clientId),
            encrypted = true,
            maxWidth = LegacyScreenViewWidthMarker,
            maxHeight = LegacyScreenViewHeightMarker,
            maxFramesPerSecond = LegacyScreenViewFrameRateMarker,
            receiverQualityFeedback = true,
            screenshot = new
            {
                transferPermissionGranted = permissions.AllowFileTransfer && pairingManager.HasCurrentHostIdentity(clientId),
                format = ScreenViewScreenshotLimits.Format,
                maxPixels = ScreenViewScreenshotLimits.MaximumPixels,
                maxBytes = ScreenViewScreenshotLimits.MaximumEncodedBytes
            },
            directPointer = new
            {
                permissionGranted = permissions.AllowRemoteInput
            }
        },
        phoneWebcam = new
        {
            enabled = getPhoneWebcamStatus?.Invoke().IsInstalled == true,
            permissionGranted = permissions.AllowPhoneWebcam,
            canUse = permissions.AllowPhoneWebcam &&
                pairingManager.HasCurrentHostIdentity(clientId) &&
                getPhoneWebcamStatus?.Invoke().IsInstalled == true,
            requiresRepair = !pairingManager.HasCurrentHostIdentity(clientId),
            microphoneAvailable = isPhoneMicrophoneAvailable?.Invoke() == true,
            maxWidth = 1920,
            maxHeight = 1080,
            maxFramesPerSecond = 30
        },
        fileManager = new
        {
            canBrowse = permissions.AllowFileBrowsing,
            canModify = permissions.AllowFileBrowsing && permissions.AllowFileChanges,
            canTransfer = permissions.AllowFileTransfer && pairingManager.HasCurrentHostIdentity(clientId),
            hidesProtectedSystemItems = permissions.HideProtectedFileSystemItems,
            maxPageSize = FileManagerProtocol.PageSize
        }
    };

    private object CreateTerminalCapability(string clientId, HostPermissionSet permissions)
    {
        TerminalCapabilityState state = getTerminalStatus?.Invoke(clientId) ?? new(false, false, null, null);
        return new
        {
            enabled = true,
            permissionGranted = permissions.AllowTerminal,
            canUse = permissions.AllowTerminal && pairingManager.HasCurrentHostIdentity(clientId),
            requiresRepair = !pairingManager.HasCurrentHostIdentity(clientId),
            active = state.Active,
            ownedByClient = state.OwnedByClient,
            terminalId = state.TerminalId,
            shell = "windows-powershell",
            reconnectGraceSeconds = (int)TerminalProtocol.ReconnectLifetime.TotalSeconds
        };
    }

    private object CreateAiAssistantCapability(string clientId)
    {
        AiAssistantCapabilityState state = getAiAssistantStatus?.Invoke(clientId) ?? new(false, false, false, false, false, null);
        bool hasIdentity = pairingManager.HasCurrentHostIdentity(clientId);
        bool myDevice = pairingManager.GetDeviceAccessProfile(clientId) == DeviceAccessProfile.MyDevice;
        return new
        {
            enabled = state.Enabled,
            available = state.Available,
            permissionGranted = myDevice,
            canUse = state.Available && myDevice && hasIdentity,
            requiresRepair = !hasIdentity,
            active = state.Active,
            ownedByClient = state.OwnedByClient,
            working = state.Working,
            failureCode = state.FailureCode
        };
    }

    private static string? ToLaserColor(PresentationLaserColor? color) => color switch
    {
        PresentationLaserColor.Red => "red",
        PresentationLaserColor.Green => "green",
        PresentationLaserColor.Blue => "blue",
        _ => null
    };

    private object CreatePowerPointCapability(string clientId)
    {
        var snapshot = getPowerPointSnapshot();
        var session = getPowerPointSession();
        return new
        {
            state = PresentationCommandHandler.ToProtocolState(snapshot.State),
            foregroundActivationSupported = true,
            presentations = snapshot.Presentations.Select(
                presentation => PresentationCommandHandler.ToProtocolPresentation(
                    presentation,
                    getPresentationBlank())),
            availablePresentations = presentationCatalog.GetAvailable(snapshot).Select(candidate => new
            {
                presentationId = candidate.PresentationId,
                title = candidate.Title,
                fileName = candidate.FileName
            }),
            session = new
            {
                state = session.State,
                runtimePresentationId = session.RuntimePresentationId,
                presentationName = session.PresentationName,
                ownerDeviceName = session.OwnerDeviceName,
                isOwner = string.Equals(
                    session.OwnerClientId,
                    clientId,
                    StringComparison.Ordinal),
                startedAt = session.StartedAt?.ToString("O"),
                elapsedSeconds = session.ElapsedSeconds,
                breakActive = session.BreakActive,
                breakElapsedSeconds = session.BreakElapsedSeconds,
                currentSlideIndex = session.CurrentSlideIndex,
                slideCount = session.SlideCount,
                slideShowState = session.SlideShowState
            }
        };
    }

    private object CreatePowerCapabilities(HostPermissionSet permissions)
    {
        var lockStatus = workstationLockPolicy.GetStatus();
        return new
        {
            @lock = permissions.AllowPcLock,
            lockAvailability = ToProtocolLockAvailability(lockStatus.State),
            blackoutDisplay = permissions.AllowBlackoutDisplay,
            displayOff = permissions.AllowDisplayControl,
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
            ProtocolStringLimits.Limit(AppVersion.Display, ProtocolStringLimits.BuildOrSessionId),
            ProtocolStringLimits.LimitOptional(webClientBuildId, ProtocolStringLimits.BuildOrSessionId),
            ProtocolStringLimits.Limit(Environment.MachineName, ProtocolStringLimits.PcName),
            ProtocolStringLimits.Limit(network.SelectedAdapterName, ProtocolStringLimits.AdapterName),
            ProtocolStringLimits.Limit(network.AdvertisedHostAddress, ProtocolStringLimits.IpAddress),
            network.Port,
            ProtocolStringLimits.Limit(network.WebSocketUrl, ProtocolStringLimits.Url),
            AppRemoteSettings.ToProtocolId(AppRemoteSettings.GetDefaultRemoteMode()),
            permissions.AllowRemoteAppLaunch ? appLaunchService.GetActions() : [],
            new TextTransferTargetMetadata(textDestination.Mode, textDestination.DisplayName, textDestination.Available),
            pairingManager.GetDevicePointerSpeed(clientId),
            AppPointerSettings.GetCustomPointer().Enabled,
            pairingManager.GetDeviceShowModeButtons(clientId),
            pairingManager.GetDeviceControlDepth(clientId),
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
