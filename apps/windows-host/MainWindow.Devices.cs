using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void RefreshDeviceDetails()
    {
        if (_deviceDetailsPanel is null)
        {
            return;
        }

        _deviceDetailsPanel.Children.Clear();
        if (_devicesList?.SelectedItem is not ListBoxItem { Tag: DeviceListItem selected })
        {
            _deviceDetailsPanel.Children.Add(CreateSectionHeading("Device details"));
            _deviceDetailsPanel.Children.Add(CreateMutedText("Select a device to manage connection and permissions."));
            return;
        }

        var device = _pairingManager.GetDevices().FirstOrDefault(item => item.ClientId == selected.ClientId);
        if (device is null)
        {
            return;
        }

        _deviceDetailsPanel.Children.Add(CreateSectionHeading(device.DeviceName));
        _deviceDetailsPanel.Children.Add(CreateMutedText(selected.Status));
        _deviceDetailsPanel.Children.Add(CreateCardText("Activity", selected.Activity));
        _deviceDetailsPanel.Children.Add(CreateCardText("Details", selected.Metadata.Length == 0 ? "No device metadata" : selected.Metadata));

        _deviceDetailsPanel.Children.Add(CreateSectionHeading("Trackpad profile"));
        AddPointerSpeedProfile(_deviceDetailsPanel, device);

        _deviceDetailsPanel.Children.Add(CreateSectionHeading("Permissions"));
        AddPermissionChoices(_deviceDetailsPanel, device, "PC sleep", PermissionKind.PcSleep);
        AddPermissionChoices(_deviceDetailsPanel, device, "Volume control", PermissionKind.VolumeControl);
        AddPermissionChoices(_deviceDetailsPanel, device, "Presentation control", PermissionKind.PresentationControl);
        AddPermissionChoices(_deviceDetailsPanel, device, "Application launch", PermissionKind.RemoteAppLaunch);
        AddPermissionChoices(_deviceDetailsPanel, device, "Open web addresses", PermissionKind.UrlOpen);
        AddPermissionChoices(_deviceDetailsPanel, device, "Lock PC", PermissionKind.PcLock);
        AddPermissionChoices(_deviceDetailsPanel, device, "Blackout display", PermissionKind.BlackoutDisplay);
        AddPermissionChoices(_deviceDetailsPanel, device, "Turn off display", PermissionKind.DisplayOff);
        AddPermissionChoices(_deviceDetailsPanel, device, "Keep awake", PermissionKind.AwakeControl);
        AddPermissionChoices(_deviceDetailsPanel, device, "Read PC clipboard", PermissionKind.ClipboardRead);
        if (_powerController.IsActionAvailable(SystemPowerActions.ScreenSaver))
        {
            AddPermissionChoices(_deviceDetailsPanel, device, "Screen saver", PermissionKind.ScreenSaver);
        }
        AddPermissionChoices(_deviceDetailsPanel, device, "Sign out", PermissionKind.SignOut);
        AddPermissionChoices(_deviceDetailsPanel, device, "Restart PC", PermissionKind.Restart);
        AddPermissionChoices(_deviceDetailsPanel, device, "Shut down PC", PermissionKind.Shutdown);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(CreateButton(device.IsActive ? "Disconnect" : "Remove", (_, _) =>
        {
            _pairingManager.DisconnectDevice(device.ClientId);
            SelectPage(HostPage.Devices);
        }, danger: true));
        _deviceDetailsPanel.Children.Add(actions);
        ApplyPermissionChoiceVisuals();
    }

    private void AddPointerSpeedProfile(StackPanel parent, PairedDeviceStatus device)
    {
        parent.Children.Add(CreateLabel("Pointer speed override"));
        parent.Children.Add(CreateMutedText(device.PointerSpeedOverride is null
            ? $"Using global default: {device.PointerSpeed.ToString(CultureInfo.InvariantCulture)}%."
            : $"Override active. Effective speed: {device.PointerSpeed.ToString(CultureInfo.InvariantCulture)}%."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        var slider = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
            Minimum = DevicePointerProfile.MinPointerSpeed,
            Maximum = DevicePointerProfile.MaxPointerSpeed,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = device.PointerSpeed,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var output = new TextBlock
        {
            Text = $"{device.PointerSpeed.ToString(CultureInfo.InvariantCulture)}%",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["TextBrush"],
            MinWidth = 48
        };
        slider.ValueChanged += (_, _) =>
        {
            output.Text = $"{Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture)}%";
        };
        row.Children.Add(slider);
        row.Children.Add(output);
        parent.Children.Add(row);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        actions.Children.Add(CreateButton("Save speed", (_, _) => SetDevicePointerSpeedOverride(device.ClientId, (int)Math.Round(slider.Value)), primary: true));
        actions.Children.Add(CreateButton("Use Global", (_, _) => SetDevicePointerSpeedOverride(device.ClientId, null)));
        parent.Children.Add(actions);
    }

    private void AddPermissionChoices(StackPanel parent, PairedDeviceStatus device, string title, PermissionKind kind)
    {
        parent.Children.Add(CreateLabel(title));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var current = GetPermissionOverride(device.PermissionOverrides, kind);
        row.Children.Add(CreateButton("Use global", (_, _) => SetDevicePermission(device.ClientId, kind, null), primary: current is null));
        row.Children.Add(CreateButton("Allow", (_, _) => SetDevicePermission(device.ClientId, kind, true), primary: current == true));
        row.Children.Add(CreateButton("Block", (_, _) => SetDevicePermission(device.ClientId, kind, false), primary: current == false, danger: current == false));
        parent.Children.Add(row);
    }

    private void SetDevicePermission(string clientId, PermissionKind kind, bool? value)
    {
        var current = _pairingManager.GetDevicePermissionOverrides(clientId);
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
        _pairingManager.SetDevicePermissionOverrides(clientId, updated);
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
        _pairingManager.SetDevicePointerSpeedOverride(clientId, pointerSpeed);
        RefreshDevicesAndSelect(clientId);
    }

    private void RefreshDevicesAndSelect(string clientId)
    {
        SelectPage(HostPage.Devices);
        _devicesList?.SelectedItem = _devicesList.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Tag is DeviceListItem device && device.ClientId == clientId);
    }

    private void CleanUpDuplicates()
    {
        var candidates = _pairingManager.GetDuplicateCleanupCandidates();
        if (candidates.Count == 0)
        {
            SelectPage(HostPage.Devices);
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            this,
            "Clean up duplicates",
            $"Remove {candidates.Count} older disconnected duplicate pairing{(candidates.Count == 1 ? string.Empty : "s")}? Connected devices are kept.",
            "Clean up",
            "Cancel",
            ConfirmationTone.Question);
        if (confirmed)
        {
            _pairingManager.CleanUpDuplicateDevices();
            SelectPage(HostPage.Devices);
        }
    }

    private void DisconnectAllDevices()
    {
        if (_pairingManager.PairedDeviceCount == 0)
        {
            return;
        }

        var confirmed = ThemedConfirmationDialog.Show(
            this,
            "Disconnect all",
            "Disconnect and remove all paired devices?",
            "Disconnect all",
            "Cancel",
            ConfirmationTone.Warning);
        if (confirmed)
        {
            _pairingManager.ClearPairing();
            SelectPage(HostPage.Devices);
        }
    }
}
