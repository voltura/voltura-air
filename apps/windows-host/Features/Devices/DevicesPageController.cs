using System.Globalization;
using System.Windows;

namespace VolturaAir.Host.Features.Devices;

internal sealed class DevicesPageController(
    Window owner,
    PairingManager pairingManager,
    ISystemPowerController powerController,
    Action requestViewRefresh)
{
    private string? _expandedClientId;
    private DevicesPageView? _currentView;

    public DevicesPageView CreateView()
    {
        _currentView = new DevicesPageView(
            GetDeviceItems(),
            ExpandDevice,
            CollapseDevice,
            SetDeviceShowModeButtonsOverride,
            SetDevicePointerSpeedOverride,
            UseGlobalPointerSpeed,
            SetDevicePermission,
            RemoveDevice,
            CleanUpDuplicates,
            RemoveAllDevices);
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
            }
        }
    }

    public void ResetDisclosureState()
    {
        _expandedClientId = null;
        _currentView = null;
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

    private int? UseGlobalPointerSpeed(string clientId)
    {
        pairingManager.SetDevicePointerSpeedOverride(clientId, null);
        return pairingManager.GetDevices()
            .FirstOrDefault(device => device.ClientId == clientId)
            ?.PointerSpeed;
    }

    private bool SetDevicePermission(string clientId, DevicePermissionKind kind, bool? value)
    {
        var current = pairingManager.GetDevicePermissionOverrides(clientId);
        var updated = kind switch
        {
            DevicePermissionKind.RemoteInput => current with { AllowRemoteInput = value },
            DevicePermissionKind.PcSleep => current with { AllowPcSleep = value },
            DevicePermissionKind.VolumeControl => current with { AllowVolumeControl = value },
            DevicePermissionKind.PresentationControl => current with { AllowPresentationControl = value },
            DevicePermissionKind.RemoteAppLaunch => current with { AllowRemoteAppLaunch = value },
            DevicePermissionKind.UrlOpen => current with { AllowUrlOpen = value },
            DevicePermissionKind.PcLock => current with { AllowPcLock = value },
            DevicePermissionKind.BlackoutDisplay => current with { AllowBlackoutDisplay = value },
            DevicePermissionKind.DisplayOff => current with { AllowDisplayOff = value },
            DevicePermissionKind.ScreenSaver => current with { AllowScreenSaver = value },
            DevicePermissionKind.AwakeControl => current with { AllowAwakeControl = value },
            DevicePermissionKind.ClipboardRead => current with { AllowClipboardRead = value },
            DevicePermissionKind.SignOut => current with { AllowSignOut = value },
            DevicePermissionKind.Restart => current with { AllowRestart = value },
            DevicePermissionKind.Shutdown => current with { AllowShutdown = value },
            _ => current
        };
        return pairingManager.SetDevicePermissionOverrides(clientId, updated);
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
            .Select(device => new DeviceListItem(
                device.ClientId,
                device.DeviceName,
                device.IsActive ? "Connected" : "Not connected",
                device.IsActive,
                GetDeviceActivityText(device),
                GetDeviceMetadataText(device) is { Length: > 0 } metadata ? metadata : "No device metadata",
                device.PointerSpeed,
                device.PointerSpeedOverride is not null,
                device.ShowModeButtonsOverride,
                device.ShowModeButtons,
                GetPermissionItems(device, globalPermissions),
                device.ClientId == _expandedClientId))];
    }

    private List<DevicePermissionItem> GetPermissionItems(PairedDeviceStatus device, HostPermissionSet global)
    {
        var permissions = new List<DevicePermissionItem>
        {
            CreatePermission(device.ClientId, DevicePermissionKind.RemoteInput, "Pointer and keyboard", device.PermissionOverrides.AllowRemoteInput, global.AllowRemoteInput),
            CreatePermission(device.ClientId, DevicePermissionKind.PcSleep, "PC sleep", device.PermissionOverrides.AllowPcSleep, global.AllowPcSleep),
            CreatePermission(device.ClientId, DevicePermissionKind.VolumeControl, "Volume control", device.PermissionOverrides.AllowVolumeControl, global.AllowVolumeControl)
        };
        if (AppDeveloperSettings.EnableAlphaFeatures())
        {
            permissions.Add(CreatePermission(device.ClientId, DevicePermissionKind.PresentationControl, "Presentation control", device.PermissionOverrides.AllowPresentationControl, global.AllowPresentationControl));
        }

        permissions.AddRange([
            CreatePermission(device.ClientId, DevicePermissionKind.RemoteAppLaunch, "Application launch", device.PermissionOverrides.AllowRemoteAppLaunch, global.AllowRemoteAppLaunch),
            CreatePermission(device.ClientId, DevicePermissionKind.UrlOpen, "Open web addresses", device.PermissionOverrides.AllowUrlOpen, global.AllowUrlOpen),
            CreatePermission(device.ClientId, DevicePermissionKind.PcLock, "Lock PC", device.PermissionOverrides.AllowPcLock, global.AllowPcLock),
            CreatePermission(device.ClientId, DevicePermissionKind.BlackoutDisplay, "Blackout display", device.PermissionOverrides.AllowBlackoutDisplay, global.AllowBlackoutDisplay),
            CreatePermission(device.ClientId, DevicePermissionKind.DisplayOff, "Turn off display", device.PermissionOverrides.AllowDisplayOff, global.AllowDisplayOff),
            CreatePermission(device.ClientId, DevicePermissionKind.AwakeControl, "Keep awake", device.PermissionOverrides.AllowAwakeControl, global.AllowAwakeControl),
            CreatePermission(device.ClientId, DevicePermissionKind.ClipboardRead, "Read PC clipboard", device.PermissionOverrides.AllowClipboardRead, global.AllowClipboardRead)
        ]);
        if (powerController.IsActionAvailable(SystemPowerActions.ScreenSaver))
        {
            permissions.Add(CreatePermission(device.ClientId, DevicePermissionKind.ScreenSaver, "Screen saver", device.PermissionOverrides.AllowScreenSaver, global.AllowScreenSaver));
        }

        permissions.AddRange([
            CreatePermission(device.ClientId, DevicePermissionKind.SignOut, "Sign out", device.PermissionOverrides.AllowSignOut, global.AllowSignOut),
            CreatePermission(device.ClientId, DevicePermissionKind.Restart, "Restart PC", device.PermissionOverrides.AllowRestart, global.AllowRestart),
            CreatePermission(device.ClientId, DevicePermissionKind.Shutdown, "Shut down PC", device.PermissionOverrides.AllowShutdown, global.AllowShutdown)
        ]);
        return permissions;
    }

    private static DevicePermissionItem CreatePermission(string clientId, DevicePermissionKind kind, string title, bool? overrideValue, bool inheritedAllow) =>
        new(clientId, kind, title, overrideValue, inheritedAllow);

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
