using System.Globalization;
using VolturaAir.Host.Features.Devices;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private DeviceListItem[] GetDeviceItems()
    {
        return [.. _pairingManager.GetDevices()
            .Select(device => new DeviceListItem(
                device.ClientId,
                device.DeviceName,
                device.IsActive ? "Connected" : "Not connected",
                GetDeviceActivityText(device),
                GetDeviceMetadataText(device)))];
    }

    private string GetConnectionStatus()
    {
        return _pairingManager.IsPaired
            ? _pairingManager.HasActiveController
                ? $"Connected to {_pairingManager.ActiveDeviceSummary}"
                : $"{_pairingManager.PairedDeviceCount} paired device{Plural(_pairingManager.PairedDeviceCount)}. Ready for another."
            : "Waiting for a phone or tablet on the same network";
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

        if (device.LastConnectedAt is not null)
        {
            return $"Last connected {FormatDeviceTime(device.LastConnectedAt.Value)}";
        }

        return $"Added {FormatDeviceTime(device.AddedAt)}";
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

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
