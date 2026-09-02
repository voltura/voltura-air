using System.Globalization;
using System.Windows;

namespace VolturaAir.Host.Features.Devices;

internal sealed record DeviceAccessViewState(
    DeviceAccessProfile Profile,
    HostPermissionSet Permissions);

internal sealed class DevicesPageController(
    Window owner,
    PairingManager pairingManager,
    WebHostService webHost,
    ISystemPowerController powerController,
    Action requestViewRefresh)
{
    private string? _expandedClientId;
    private string? _focusAccessClientId;
    private DevicesPageView? _currentView;

    public DevicesPageView CreateView()
    {
        var devices = GetDeviceItems();
        _currentView = new DevicesPageView(
            devices,
            ExpandDevice,
            CollapseDevice,
            SetDeviceShowModeButtonsOverride,
            SetDeviceControlDepthOverride,
            SetDeviceScreenSoundQualityOverride,
            SetDevicePointerSpeedOverride,
            UseGlobalPointerSpeed,
            SetDeviceAccessProfile,
            SetDevicePermission,
            SetProtectedFileFilter,
            RemoveDevice,
            CleanUpDuplicates,
            RemoveAllDevices);
        if (_focusAccessClientId is { } focusClientId)
        {
            if (devices.All(device => !string.Equals(device.ClientId, focusClientId, StringComparison.Ordinal)))
            {
                _focusAccessClientId = null;
            }
            else
            {
                var focusView = _currentView;
                focusView.FocusAccessProfile(focusClientId, focused =>
                {
                    if (focused &&
                        ReferenceEquals(_currentView, focusView) &&
                        string.Equals(_focusAccessClientId, focusClientId, StringComparison.Ordinal))
                    {
                        _focusAccessClientId = null;
                    }
                });
            }
        }
        return _currentView;
    }

    public void RefreshDeviceProfiles()
    {
        if (_currentView is null)
        {
            return;
        }

        var profiles = pairingManager.GetDevices().ToDictionary(device => device.ClientId, StringComparer.Ordinal);
        foreach (var item in _currentView.Devices.Items.OfType<DeviceListItem>())
        {
            if (profiles.TryGetValue(item.ClientId, out var profile))
            {
                item.ApplyPointerSpeed(profile.PointerSpeed, profile.PointerSpeedOverride is not null);
                item.ApplyShowModeButtons(profile.ShowModeButtonsOverride, profile.ShowModeButtons);
                item.ApplyControlDepth(profile.ControlDepthOverride, profile.ControlDepth);
                item.ApplyScreenSoundQuality(profile.ScreenSoundQualityOverride, profile.ScreenSoundQuality);
                var effectivePermissions = pairingManager.GetEffectivePermissions(
                    profile.ClientId,
                    AppPermissionSettings.Load());
                item.ApplyAccessProfile(profile.AccessProfile, effectivePermissions);
                item.ProtectedFileFilter.Apply(
                    pairingManager.GetDevicePermissionOverrides(profile.ClientId).HideProtectedFileSystemItems,
                    effectivePermissions.HideProtectedFileSystemItems);
            }
        }
    }

    public void ResetDisclosureState()
    {
        _expandedClientId = null;
        _focusAccessClientId = null;
        _currentView = null;
    }

    public void OpenDeviceAccess(string clientId)
    {
        var exists = pairingManager.GetDevices().Any(device =>
            string.Equals(device.ClientId, clientId, StringComparison.Ordinal));
        _expandedClientId = exists ? clientId : null;
        _focusAccessClientId = exists ? clientId : null;
    }

    private void ExpandDevice(string clientId)
    {
        _expandedClientId = clientId;
    }

    private void CollapseDevice(string clientId)
    {
        if (_expandedClientId == clientId)
        {
            _expandedClientId = null;
        }
    }

    private bool SetDevicePointerSpeedOverride(string clientId, int pointerSpeed)
    {
        return pairingManager.SetDevicePointerSpeedOverride(clientId, pointerSpeed);
    }

    private (bool? Override, bool Effective)? SetDeviceShowModeButtonsOverride(string clientId, bool? showModeButtons)
    {
        pairingManager.SetDeviceShowModeButtonsOverride(clientId, showModeButtons);
        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == clientId);
        return device is null ? null : (device.ShowModeButtonsOverride, device.ShowModeButtons);
    }

    private (bool? Override, bool Effective)? SetDeviceControlDepthOverride(string clientId, bool? controlDepth)
    {
        pairingManager.SetDeviceControlDepthOverride(clientId, controlDepth);
        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == clientId);
        return device is null ? null : (device.ControlDepthOverride, device.ControlDepth);
    }

    private (ScreenViewSoundQuality? Override, ScreenViewSoundQuality Effective)? SetDeviceScreenSoundQualityOverride(
        string clientId,
        ScreenViewSoundQuality? soundQuality)
    {
        pairingManager.SetDeviceScreenSoundQualityOverride(clientId, soundQuality);
        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == clientId);
        return device is null ? null : (device.ScreenSoundQualityOverride, device.ScreenSoundQuality);
    }

    private int? UseGlobalPointerSpeed(string clientId)
    {
        pairingManager.SetDevicePointerSpeedOverride(clientId, null);
        return pairingManager.GetDevices()
            .FirstOrDefault(device => device.ClientId == clientId)
            ?.PointerSpeed;
    }

    private DeviceAccessViewState? SetDeviceAccessProfile(string clientId, DeviceAccessProfile profile)
    {
        pairingManager.SetDeviceAccessProfile(clientId, profile);
        return GetDeviceAccessState(clientId);
    }

    private DeviceAccessViewState? SetDevicePermission(
        string clientId,
        DevicePermissionKind kind,
        bool value)
    {
        pairingManager.SetDevicePermission(clientId, kind, value);
        return GetDeviceAccessState(clientId);
    }

    private DeviceAccessViewState? GetDeviceAccessState(string clientId)
    {
        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == clientId);
        return device is null
            ? null
            : new DeviceAccessViewState(
                device.AccessProfile,
                pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()));
    }

    private (bool? Override, bool Effective)? SetProtectedFileFilter(string clientId, bool? hideProtected)
    {
        pairingManager.SetDeviceProtectedFileFilterOverride(clientId, hideProtected);
        if (pairingManager.GetDevices().All(device => device.ClientId != clientId))
        {
            return null;
        }

        return (
            pairingManager.GetDevicePermissionOverrides(clientId).HideProtectedFileSystemItems,
            pairingManager.GetEffectivePermissions(clientId, AppPermissionSettings.Load()).HideProtectedFileSystemItems);
    }

    private void RemoveDevice(string clientId)
    {
        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == clientId);
        if (device is null)
        {
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            owner,
            "Remove device",
            $"Remove {device.DeviceName}? This device will need to pair again.",
            "Remove",
            "Cancel",
            ConfirmationTone.Warning);
        if (!confirmed)
        {
            return;
        }

        pairingManager.DisconnectDevice(clientId);
        _expandedClientId = null;
        requestViewRefresh();
    }

    private void CleanUpDuplicates()
    {
        var candidates = pairingManager.GetDuplicateCleanupCandidates();
        if (candidates.Count == 0)
        {
            requestViewRefresh();
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            owner,
            "Clean up duplicates",
            $"Remove {candidates.Count} older disconnected duplicate pairing{(candidates.Count == 1 ? string.Empty : "s")}? Connected devices are kept.",
            "Clean up",
            "Cancel",
            ConfirmationTone.Question);
        if (confirmed)
        {
            pairingManager.CleanUpDuplicateDevices();
            requestViewRefresh();
        }
    }

    private void RemoveAllDevices()
    {
        if (pairingManager.PairedDeviceCount == 0)
        {
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            owner,
            "Remove all devices",
            "Remove all paired devices? Every device will need to pair again.",
            "Remove all",
            "Cancel",
            ConfirmationTone.Warning);
        if (confirmed)
        {
            pairingManager.ClearPairing();
            _expandedClientId = null;
            requestViewRefresh();
        }
    }

    private DeviceListItem[] GetDeviceItems()
    {
        var globalPermissions = AppPermissionSettings.Load();
        return [.. pairingManager.GetDevices()
            .Select(device =>
            {
                var connectionAvailable = webHost.IsDeviceConnectionMethodAvailable(device.LastConnectionMethod);
                return new DeviceListItem(
                    device.ClientId,
                    device.DeviceName,
                    device.IsActive
                        ? "Connected"
                        : connectionAvailable
                            ? "Not connected"
                            : "Unavailable in current mode",
                    device.IsActive,
                    connectionAvailable,
                    GetDeviceSummaryText(device),
                    GetDeviceMetadataText(device) is { Length: > 0 } metadata ? metadata : "No device metadata",
                    device.PointerSpeed,
                    device.PointerSpeedOverride is not null,
                    device.ShowModeButtonsOverride,
                    device.ShowModeButtons,
                    device.ControlDepthOverride,
                    device.ControlDepth,
                    device.ScreenSoundQualityOverride,
                    device.ScreenSoundQuality,
                    device.AccessProfile,
                    GetPermissionItems(device, pairingManager.GetEffectivePermissions(device.ClientId, globalPermissions)),
                    new ProtectedFileFilterItem(
                        device.ClientId,
                        device.PermissionOverrides.HideProtectedFileSystemItems,
                        pairingManager.GetEffectivePermissions(device.ClientId, globalPermissions).HideProtectedFileSystemItems),
                    device.ClientId == _expandedClientId);
            })];
    }

    private List<DevicePermissionItem> GetPermissionItems(PairedDeviceStatus device, HostPermissionSet effective)
    {
        return [.. DeviceAccessProfiles.Permissions
            .Where(permission =>
                permission.Kind != DevicePermissionKind.ScreenSaver ||
                powerController.IsActionAvailable(SystemPowerActions.ScreenSaver))
            .Select(permission => new DevicePermissionItem(
                device.ClientId,
                permission.Kind,
                permission.DisplayName,
                permission.Read(effective)))];
    }

    private static string GetDeviceActivityText(PairedDeviceStatus device)
    {
        if (device.IsActive)
        {
            return $"Connected since {FormatDeviceTime(device.LastConnectedAt ?? device.LatestActivityAt)}";
        }

        if (device.LastDisconnectedAt is not null && device.LastDisconnectedAt >= (device.LastConnectedAt ?? DateTimeOffset.MinValue))
        {
            return $"Disconnected {FormatDeviceTime(device.LastDisconnectedAt.Value)}";
        }

        return $"Last active {FormatDeviceTime(device.LatestActivityAt)}";
    }

    private static string GetDeviceSummaryText(PairedDeviceStatus device) =>
        FormatConnectionActivity(device.LastConnectionMethod, GetDeviceActivityText(device));

    internal static string FormatConnectionActivity(
        DeviceConnectionMethod connectionMethod,
        string activity)
    {
        var connection = connectionMethod switch
        {
            DeviceConnectionMethod.StandardLocal => "Standard Local",
            DeviceConnectionMethod.EnhancedDirect => "Enhanced Direct",
            DeviceConnectionMethod.DebugDirect => "Debug Direct",
            DeviceConnectionMethod.CloudRelay => "Cloud Relay",
            _ => null
        };
        return connection is null ? activity : $"Connection: {connection}, {activity}";
    }

    private static string GetDeviceMetadataText(PairedDeviceStatus device)
    {
        var displayMode = device.DisplayMode.Equals("installed", StringComparison.OrdinalIgnoreCase)
            ? "Installed app"
            : device.DisplayMode.Equals("browser", StringComparison.OrdinalIgnoreCase)
                ? "Browser"
                : string.Empty;
        var parts = new[] { device.Platform, device.Browser, displayMode }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join(" / ", parts);
    }

    private static string FormatDeviceTime(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
}
