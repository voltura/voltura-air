using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace VolturaAir.Host.Features.Devices;

internal sealed class DevicesPageController(
    Window owner,
    PairingManager pairingManager,
    ISystemPowerController powerController,
    HostVisualFactory visuals,
    Action requestViewRefresh)
{
    private const string PermissionChoiceCheckPrefix = "\u2713 ";
    private ListBox? _devicesList;
    private StackPanel? _deviceDetailsPanel;

    public DevicesPageView CreateView()
    {
        var root = new DevicesPageView(
            GetDeviceItems(),
            RefreshDeviceDetails,
            CleanUpDuplicates,
            DisconnectAllDevices);
        _devicesList = root.Devices;
        _deviceDetailsPanel = root.Details;
        RefreshDeviceDetails();
        return root;
    }

    private void RefreshDeviceDetails()
    {
        if (_deviceDetailsPanel is null)
        {
            return;
        }

        _deviceDetailsPanel.Children.Clear();
        if (_devicesList?.SelectedItem is not DeviceListItem selected)
        {
            _deviceDetailsPanel.Children.Add(visuals.CreateSectionHeading("Device details"));
            _deviceDetailsPanel.Children.Add(visuals.CreateMutedText("Select a device to manage connection and permissions."));
            return;
        }

        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == selected.ClientId);
        if (device is null)
        {
            return;
        }

        _deviceDetailsPanel.Children.Add(visuals.CreateSectionHeading(device.DeviceName));
        _deviceDetailsPanel.Children.Add(visuals.CreateMutedText(selected.Status));
        _deviceDetailsPanel.Children.Add(visuals.CreateCardText("Activity", selected.Activity));
        _deviceDetailsPanel.Children.Add(visuals.CreateCardText("Details", selected.Metadata.Length == 0 ? "No device metadata" : selected.Metadata));

        _deviceDetailsPanel.Children.Add(visuals.CreateSectionHeading("Trackpad profile"));
        AddPointerSpeedProfile(_deviceDetailsPanel, device);

        _deviceDetailsPanel.Children.Add(visuals.CreateSectionHeading("Permissions"));
        AddPermissionChoices(_deviceDetailsPanel, device, "PC sleep", PermissionKind.PcSleep);
        AddPermissionChoices(_deviceDetailsPanel, device, "Volume control", PermissionKind.VolumeControl);
        if (AppDeveloperSettings.EnableAlphaFeatures())
        {
            AddPermissionChoices(_deviceDetailsPanel, device, "Presentation control", PermissionKind.PresentationControl);
        }

        AddPermissionChoices(_deviceDetailsPanel, device, "Application launch", PermissionKind.RemoteAppLaunch);
        AddPermissionChoices(_deviceDetailsPanel, device, "Open web addresses", PermissionKind.UrlOpen);
        AddPermissionChoices(_deviceDetailsPanel, device, "Lock PC", PermissionKind.PcLock);
        AddPermissionChoices(_deviceDetailsPanel, device, "Blackout display", PermissionKind.BlackoutDisplay);
        AddPermissionChoices(_deviceDetailsPanel, device, "Turn off display", PermissionKind.DisplayOff);
        AddPermissionChoices(_deviceDetailsPanel, device, "Keep awake", PermissionKind.AwakeControl);
        AddPermissionChoices(_deviceDetailsPanel, device, "Read PC clipboard", PermissionKind.ClipboardRead);
        if (powerController.IsActionAvailable(SystemPowerActions.ScreenSaver))
        {
            AddPermissionChoices(_deviceDetailsPanel, device, "Screen saver", PermissionKind.ScreenSaver);
        }

        AddPermissionChoices(_deviceDetailsPanel, device, "Sign out", PermissionKind.SignOut);
        AddPermissionChoices(_deviceDetailsPanel, device, "Restart PC", PermissionKind.Restart);
        AddPermissionChoices(_deviceDetailsPanel, device, "Shut down PC", PermissionKind.Shutdown);

        var actions = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        actions.Children.Add(visuals.CreateButton(device.IsActive ? "Disconnect" : "Remove", (_, _) =>
        {
            pairingManager.DisconnectDevice(device.ClientId);
            requestViewRefresh();
        }, danger: true));
        _deviceDetailsPanel.Children.Add(actions);
        ApplyPermissionChoiceVisuals();
    }

    private void AddPointerSpeedProfile(StackPanel parent, PairedDeviceStatus device)
    {
        parent.Children.Add(visuals.CreateLabel("Pointer speed override"));
        parent.Children.Add(visuals.CreateMutedText(device.PointerSpeedOverride is null
            ? $"Using global default: {device.PointerSpeed.ToString(CultureInfo.InvariantCulture)}%."
            : $"Override active. Effective speed: {device.PointerSpeed.ToString(CultureInfo.InvariantCulture)}%."));
        var row = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceMd);
        var slider = new Slider
        {
            Style = visuals.Style("ModernSliderStyle"),
            Minimum = DevicePointerProfile.MinPointerSpeed,
            Maximum = DevicePointerProfile.MaxPointerSpeed,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = device.PointerSpeed
        };
        var output = new TextBlock
        {
            Text = $"{device.PointerSpeed.ToString(CultureInfo.InvariantCulture)}%",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = visuals.Brush("TextBrush"),
            MinWidth = 48
        };
        slider.ValueChanged += (_, _) =>
        {
            output.Text = $"{Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture)}%";
        };
        row.Children.Add(slider);
        row.Children.Add(output);
        parent.Children.Add(row);

        var actions = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        actions.Children.Add(visuals.CreateButton("Save speed", (_, _) => SetDevicePointerSpeedOverride(device.ClientId, (int)Math.Round(slider.Value)), primary: true));
        actions.Children.Add(visuals.CreateButton("Use Global", (_, _) => SetDevicePointerSpeedOverride(device.ClientId, null)));
        parent.Children.Add(actions);
    }

    private void AddPermissionChoices(StackPanel parent, PairedDeviceStatus device, string title, PermissionKind kind)
    {
        parent.Children.Add(visuals.CreateLabel(title));
        var row = HostVisualFactory.CreateHorizontalStack(UiTokens.SpaceSm);
        var current = GetPermissionOverride(device.PermissionOverrides, kind);
        row.Children.Add(visuals.CreateButton("Use global", (_, _) => SetDevicePermission(device.ClientId, kind, null), primary: current is null));
        row.Children.Add(visuals.CreateButton("Allow", (_, _) => SetDevicePermission(device.ClientId, kind, true), primary: current == true));
        row.Children.Add(visuals.CreateButton("Block", (_, _) => SetDevicePermission(device.ClientId, kind, false), primary: current == false, danger: current == false));
        parent.Children.Add(row);
    }

    private void SetDevicePermission(string clientId, PermissionKind kind, bool? value)
    {
        var current = pairingManager.GetDevicePermissionOverrides(clientId);
        var updated = kind switch
        {
            PermissionKind.PcSleep => current with { AllowPcSleep = value },
            PermissionKind.VolumeControl => current with { AllowVolumeControl = value },
            PermissionKind.PresentationControl => current with { AllowPresentationControl = value },
            PermissionKind.RemoteAppLaunch => current with { AllowRemoteAppLaunch = value },
            PermissionKind.UrlOpen => current with { AllowUrlOpen = value },
            PermissionKind.PcLock => current with { AllowPcLock = value },
            PermissionKind.BlackoutDisplay => current with { AllowBlackoutDisplay = value },
            PermissionKind.DisplayOff => current with { AllowDisplayOff = value },
            PermissionKind.ScreenSaver => current with { AllowScreenSaver = value },
            PermissionKind.AwakeControl => current with { AllowAwakeControl = value },
            PermissionKind.ClipboardRead => current with { AllowClipboardRead = value },
            PermissionKind.SignOut => current with { AllowSignOut = value },
            PermissionKind.Restart => current with { AllowRestart = value },
            PermissionKind.Shutdown => current with { AllowShutdown = value },
            _ => current
        };
        pairingManager.SetDevicePermissionOverrides(clientId, updated);
        RefreshDevicesAndSelect(clientId);
    }

    private static bool? GetPermissionOverride(DevicePermissionOverrides permissions, PermissionKind kind)
    {
        return kind switch
        {
            PermissionKind.PcSleep => permissions.AllowPcSleep,
            PermissionKind.VolumeControl => permissions.AllowVolumeControl,
            PermissionKind.PresentationControl => permissions.AllowPresentationControl,
            PermissionKind.RemoteAppLaunch => permissions.AllowRemoteAppLaunch,
            PermissionKind.UrlOpen => permissions.AllowUrlOpen,
            PermissionKind.PcLock => permissions.AllowPcLock,
            PermissionKind.BlackoutDisplay => permissions.AllowBlackoutDisplay,
            PermissionKind.DisplayOff => permissions.AllowDisplayOff,
            PermissionKind.ScreenSaver => permissions.AllowScreenSaver,
            PermissionKind.AwakeControl => permissions.AllowAwakeControl,
            PermissionKind.ClipboardRead => permissions.AllowClipboardRead,
            PermissionKind.SignOut => permissions.AllowSignOut,
            PermissionKind.Restart => permissions.AllowRestart,
            PermissionKind.Shutdown => permissions.AllowShutdown,
            _ => null
        };
    }

    private void SetDevicePointerSpeedOverride(string clientId, int? pointerSpeed)
    {
        pairingManager.SetDevicePointerSpeedOverride(clientId, pointerSpeed);
        RefreshDevicesAndSelect(clientId);
    }

    private void RefreshDevicesAndSelect(string clientId)
    {
        requestViewRefresh();
        _devicesList?.SelectedItem = _devicesList.Items
            .OfType<DeviceListItem>()
            .FirstOrDefault(device => device.ClientId == clientId);
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

    private void DisconnectAllDevices()
    {
        if (pairingManager.PairedDeviceCount == 0)
        {
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            owner,
            "Disconnect all",
            "Disconnect and remove all paired devices?",
            "Disconnect all",
            "Cancel",
            ConfirmationTone.Warning);
        if (confirmed)
        {
            pairingManager.ClearPairing();
            requestViewRefresh();
        }
    }

    private DeviceListItem[] GetDeviceItems()
    {
        return [.. pairingManager.GetDevices()
            .Select(device => new DeviceListItem(
                device.ClientId,
                device.DeviceName,
                device.IsActive ? "Connected" : "Not connected",
                device.IsActive,
                GetDeviceActivityText(device),
                GetDeviceMetadataText(device)))];
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

        return device.LastConnectedAt is not null
            ? $"Last connected {FormatDeviceTime(device.LastConnectedAt.Value)}"
            : $"Added {FormatDeviceTime(device.AddedAt)}";
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

    private static string FormatDeviceTime(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private void ApplyPermissionChoiceVisuals()
    {
        if (_deviceDetailsPanel is null || _devicesList?.SelectedItem is not DeviceListItem selected)
        {
            return;
        }

        var device = pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == selected.ClientId);
        if (device is null)
        {
            return;
        }

        var globalPermissions = AppPermissionSettings.Load();
        ApplyPermissionChoiceVisuals("PC sleep", device.PermissionOverrides.AllowPcSleep, globalPermissions.AllowPcSleep);
        ApplyPermissionChoiceVisuals("Volume control", device.PermissionOverrides.AllowVolumeControl, globalPermissions.AllowVolumeControl);
        if (AppDeveloperSettings.EnableAlphaFeatures())
        {
            ApplyPermissionChoiceVisuals("Presentation control", device.PermissionOverrides.AllowPresentationControl, globalPermissions.AllowPresentationControl);
        }

        ApplyPermissionChoiceVisuals("Application launch", device.PermissionOverrides.AllowRemoteAppLaunch, globalPermissions.AllowRemoteAppLaunch);
        ApplyPermissionChoiceVisuals("Open web addresses", device.PermissionOverrides.AllowUrlOpen, globalPermissions.AllowUrlOpen);
        ApplyPermissionChoiceVisuals("Lock PC", device.PermissionOverrides.AllowPcLock, globalPermissions.AllowPcLock);
        ApplyPermissionChoiceVisuals("Blackout display", device.PermissionOverrides.AllowBlackoutDisplay, globalPermissions.AllowBlackoutDisplay);
        ApplyPermissionChoiceVisuals("Turn off display", device.PermissionOverrides.AllowDisplayOff, globalPermissions.AllowDisplayOff);
        ApplyPermissionChoiceVisuals("Keep awake", device.PermissionOverrides.AllowAwakeControl, globalPermissions.AllowAwakeControl);
        ApplyPermissionChoiceVisuals("Read PC clipboard", device.PermissionOverrides.AllowClipboardRead, globalPermissions.AllowClipboardRead);
        ApplyPermissionChoiceVisuals("Screen saver", device.PermissionOverrides.AllowScreenSaver, globalPermissions.AllowScreenSaver);
        ApplyPermissionChoiceVisuals("Sign out", device.PermissionOverrides.AllowSignOut, globalPermissions.AllowSignOut);
        ApplyPermissionChoiceVisuals("Restart PC", device.PermissionOverrides.AllowRestart, globalPermissions.AllowRestart);
        ApplyPermissionChoiceVisuals("Shut down PC", device.PermissionOverrides.AllowShutdown, globalPermissions.AllowShutdown);
    }

    private void ApplyPermissionChoiceVisuals(string label, bool? overrideValue, bool inheritedAllow)
    {
        var row = FindPermissionChoiceRow(label);
        if (row is null)
        {
            return;
        }

        var allowButton = FindPermissionButton(row, "Allow");
        var blockButton = FindPermissionButton(row, "Block");
        ResetPermissionButtonVisual(allowButton, "Allow", primary: overrideValue == true, danger: false);
        ResetPermissionButtonVisual(blockButton, "Block", primary: overrideValue == false, danger: overrideValue == false);

        var effectiveAllow = overrideValue ?? inheritedAllow;
        if (effectiveAllow)
        {
            ApplyEffectivePermissionVisual(allowButton, "Allow", isInherited: overrideValue is null, danger: false);
        }
        else
        {
            ApplyEffectivePermissionVisual(blockButton, "Block", isInherited: overrideValue is null, danger: true);
        }
    }

    private StackPanel? FindPermissionChoiceRow(string label)
    {
        if (_deviceDetailsPanel is null)
        {
            return null;
        }

        for (var index = 0; index < _deviceDetailsPanel.Children.Count - 1; index++)
        {
            if (_deviceDetailsPanel.Children[index] is TextBlock { Text: var text } &&
                string.Equals(text, label, StringComparison.Ordinal) &&
                _deviceDetailsPanel.Children[index + 1] is StackPanel row)
            {
                return row;
            }
        }

        return null;
    }

    private static Button? FindPermissionButton(StackPanel row, string text)
    {
        return row.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(GetPermissionButtonText(button), text, StringComparison.Ordinal));
    }

    private static string GetPermissionButtonText(Button button)
    {
        return button.Content is string text && text.StartsWith(PermissionChoiceCheckPrefix, StringComparison.Ordinal)
            ? text[PermissionChoiceCheckPrefix.Length..]
            : button.Content as string ?? string.Empty;
    }

    private void ResetPermissionButtonVisual(Button? button, string text, bool primary, bool danger)
    {
        if (button is null)
        {
            return;
        }

        button.Content = text;
        button.Background = primary ? visuals.Brush("AccentBrush") : visuals.Brush("SurfaceRaisedBrush");
        button.Foreground = primary ? visuals.Brush("AccentTextBrush") : danger ? visuals.Brush("DangerBrush") : visuals.Brush("TextBrush");
        button.BorderBrush = primary ? visuals.Brush("AccentBrush") : visuals.Brush("BorderBrush");
        button.BorderThickness = new Thickness(1);
        button.FontWeight = FontWeights.Normal;
        button.Opacity = 1;
    }

    private void ApplyEffectivePermissionVisual(Button? button, string text, bool isInherited, bool danger)
    {
        if (button is null)
        {
            return;
        }

        button.Content = PermissionChoiceCheckPrefix + text;
        button.FontWeight = FontWeights.SemiBold;
        if (!isInherited)
        {
            return;
        }

        var brush = visuals.Brush(danger ? "DangerBrush" : "AccentBrush");
        button.Foreground = brush;
        button.BorderBrush = brush;
        button.BorderThickness = new Thickness(2);
    }

    private enum PermissionKind
    {
        PcSleep,
        VolumeControl,
        PresentationControl,
        RemoteAppLaunch,
        UrlOpen,
        PcLock,
        BlackoutDisplay,
        DisplayOff,
        ScreenSaver,
        AwakeControl,
        ClipboardRead,
        SignOut,
        Restart,
        Shutdown
    }
}
