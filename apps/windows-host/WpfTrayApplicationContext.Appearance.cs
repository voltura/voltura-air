using System.Drawing;
using Forms = System.Windows.Forms;

namespace VolturaAir.Host;

internal sealed partial class WpfTrayApplicationContext
{
    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ApplyMenuTheme();
            }
        });
    }

    private void OnAwakeStateChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ApplyAwakeMenuState();
            }
        });
    }

    private void ApplyTrayConnectionState(bool holdConnectedDuringReconnect = false)
    {
        if (_disposed)
        {
            return;
        }

        var state = _trayConnectionIndicator.Update(
            _pairingManager.IsPaired,
            _pairingManager.HasActiveController,
            holdConnectedDuringReconnect,
            holdInitialDisconnectedState: _pendingStartupConnectionGrace is not null);
        _trayIcon.Text = BuildTrayTooltip(state);
        _trayIcon.Icon = GetTrayIcon(state);
    }

    private Icon GetTrayIcon(TrayConnectionState state)
    {
        return _trayIcons.TryGetValue(state, out var icon)
            ? icon
            : _trayIcons[TrayConnectionState.NoDevicesRegistered];
    }

    private string BuildTrayTooltip(TrayConnectionState state)
    {
        var status = state switch
        {
            TrayConnectionState.Starting => "waiting for paired devices to reconnect",
            TrayConnectionState.Connected => BuildConnectedTooltipStatus(),
            TrayConnectionState.Disconnected => "no devices connected",
            _ => "no devices paired yet"
        };

        return TruncateTrayTooltip($"Voltura Air - {status}");
    }

    private string BuildConnectedTooltipStatus()
    {
        var activeDeviceCount = _pairingManager.ActiveDeviceNames.Count;
        if (activeDeviceCount <= 0)
        {
            return "connected";
        }

        var deviceLabel = activeDeviceCount == 1 ? "device" : "devices";
        return $"{activeDeviceCount} {deviceLabel} connected: {_pairingManager.ActiveDeviceSummary}";
    }

    private static string TruncateTrayTooltip(string value)
    {
        return value.Length <= MaxTrayTooltipLength ? value : $"{value[..(MaxTrayTooltipLength - 3)]}...";
    }

    private static Dictionary<TrayConnectionState, Icon> LoadTrayIcons()
    {
        var normal = LoadTrayIcon(DefaultTrayIconFileName);
        return new Dictionary<TrayConnectionState, Icon>
        {
            [TrayConnectionState.Starting] = (Icon)normal.Clone(),
            [TrayConnectionState.NoDevicesRegistered] = normal,
            [TrayConnectionState.Disconnected] = LoadTrayIconOrDefault(DisconnectedTrayIconFileName, normal),
            [TrayConnectionState.Connected] = LoadTrayIconOrDefault(ConnectedTrayIconFileName, normal)
        };
    }

    private static Icon LoadTrayIconOrDefault(string fileName, Icon fallback)
    {
        var iconPath = GetAssetPath(fileName);
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)fallback.Clone();
    }

    private static Icon LoadTrayIcon(string fileName)
    {
        var iconPath = GetAssetPath(fileName);
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }

}
