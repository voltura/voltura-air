namespace VolturaAir.Host;

public sealed partial class PairingManager
{
    private static PairingRecord NormalizeRecord(PairingRecord record)
    {
        return (record.AddedAt == default ? record with { AddedAt = DateTimeOffset.UtcNow } : record) with
        {
            Platform = NormalizeMetadata(record.Platform),
            Browser = NormalizeMetadata(record.Browser),
            DisplayMode = NormalizeMetadata(record.DisplayMode),
            PermissionOverrides = NormalizePermissionOverrides(record.PermissionOverrides),
            PointerSpeedOverride = NormalizePointerSpeedOverride(record.PointerSpeedOverride)
        };
    }

    private IReadOnlyList<PairedDeviceStatus> BuildDeviceStatuses()
    {
        return _records
            .Select(record =>
            {
                var activeConnections = _activeConnections.GetValueOrDefault(record.ClientId);
                return new PairedDeviceStatus(
                    record.ClientId,
                    record.DeviceName,
                    activeConnections > 0,
                    activeConnections,
                    record.AddedAt,
                    record.LastConnectedAt,
                    record.LastDisconnectedAt,
                    record.LastRenamedAt,
                    record.Platform,
                    record.Browser,
                    record.DisplayMode,
                    record.PermissionOverrides ?? new DevicePermissionOverrides(),
                    record.PointerSpeedOverride,
                    GetEffectivePointerSpeed(record));
            })
            .ToArray();
    }

    private static DevicePermissionOverrides NormalizePermissionOverrides(DevicePermissionOverrides? permissionOverrides)
    {
        return new DevicePermissionOverrides(
            permissionOverrides?.AllowPcSleep,
            permissionOverrides?.AllowVolumeControl,
            permissionOverrides?.AllowPcLock,
            permissionOverrides?.AllowBlackoutDisplay,
            permissionOverrides?.AllowDisplayOff,
            permissionOverrides?.AllowScreenSaver,
            permissionOverrides?.AllowSignOut,
            permissionOverrides?.AllowRestart,
            permissionOverrides?.AllowShutdown);
    }

    private IReadOnlyList<PairedDeviceStatus> GetDuplicateCleanupCandidatesCore()
    {
        return BuildDeviceStatuses()
            .GroupBy(device => device.DeviceName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group =>
            {
                var deviceToKeep = group
                    .OrderByDescending(device => device.IsActive)
                    .ThenByDescending(device => device.LatestActivityAt)
                    .First();

                return group.Where(device =>
                    !device.IsActive &&
                    !string.Equals(device.ClientId, deviceToKeep.ClientId, StringComparison.Ordinal));
            })
            .OrderByDescending(device => device.LatestActivityAt)
            .ToArray();
    }

    private PairingRecord? FindRecord(string clientId)
    {
        return _records.FirstOrDefault(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
    }

    private bool UpdateDeviceDetails(string clientId, string deviceName, string? platform, string? browser, string? displayMode, DateTimeOffset updatedAt)
    {
        var index = _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        var existing = _records[index];
        var next = existing with
        {
            DeviceName = deviceName,
            Platform = platform ?? existing.Platform,
            Browser = browser ?? existing.Browser,
            DisplayMode = displayMode ?? existing.DisplayMode
        };

        if (!string.Equals(existing.DeviceName, deviceName, StringComparison.Ordinal))
        {
            next = next with { LastRenamedAt = updatedAt };
        }

        if (next == existing)
        {
            return false;
        }

        _records[index] = next;
        _store.Save(_records);
        return true;
    }

    private void UpdateConnectionTimestamp(string clientId, DateTimeOffset connectedAt)
    {
        var index = _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        _records[index] = _records[index] with { LastConnectedAt = connectedAt };
        _store.Save(_records);
    }

    private void UpdateDisconnectionTimestamp(string clientId, DateTimeOffset disconnectedAt)
    {
        var index = _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        _records[index] = _records[index] with { LastDisconnectedAt = disconnectedAt };
        _store.Save(_records);
    }

    private void UpsertRecord(PairingRecord record)
    {
        var index = _records.FindIndex(existing => string.Equals(existing.ClientId, record.ClientId, StringComparison.Ordinal));
        if (index >= 0)
        {
            var existing = _records[index];
            _records[index] = record with
            {
                AddedAt = existing.AddedAt == default ? record.AddedAt : existing.AddedAt,
                LastConnectedAt = existing.LastConnectedAt,
                LastDisconnectedAt = existing.LastDisconnectedAt,
                LastRenamedAt = existing.LastRenamedAt,
                Platform = string.IsNullOrWhiteSpace(record.Platform) ? existing.Platform : record.Platform,
                Browser = string.IsNullOrWhiteSpace(record.Browser) ? existing.Browser : record.Browser,
                DisplayMode = string.IsNullOrWhiteSpace(record.DisplayMode) ? existing.DisplayMode : record.DisplayMode,
                PermissionOverrides = existing.PermissionOverrides,
                PointerSpeedOverride = existing.PointerSpeedOverride
            };
            return;
        }

        _records.Add(record);
    }

    private static string NormalizeDeviceName(string deviceName)
    {
        var trimmed = deviceName.Trim();
        return trimmed.Length > 0 ? trimmed : "Mobile device";
    }

    private static string NormalizeMetadata(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }

    private static int? NormalizePointerSpeedOverride(int? pointerSpeedOverride)
    {
        if (pointerSpeedOverride is not null)
        {
            return DevicePointerProfile.NormalizePointerSpeed(pointerSpeedOverride.Value);
        }

        return null;
    }

    private static int GetEffectivePointerSpeed(PairingRecord? record)
    {
        return record?.PointerSpeedOverride ?? AppPointerSettings.GetDefaultPointerSpeed();
    }

    private static string SummarizeDevices(IEnumerable<string> deviceNames)
    {
        var names = deviceNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
        return names.Length switch
        {
            0 => "no devices",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} and {names.Length - 1} more"
        };
    }

    private sealed class ConnectionScope : IDisposable
    {
        private readonly PairingManager _manager;
        private readonly string _clientId;
        private bool _disposed;

        public ConnectionScope(PairingManager manager, string clientId)
        {
            _manager = manager;
            _clientId = clientId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_manager._gate)
            {
                if (_manager._activeConnections.TryGetValue(_clientId, out var count) && count > 1)
                {
                    _manager._activeConnections[_clientId] = count - 1;
                }
                else
                {
                    _manager._activeConnections.Remove(_clientId);
                    _manager.UpdateDisconnectionTimestamp(_clientId, DateTimeOffset.UtcNow);
                }
            }

            _disposed = true;
            _manager.ConnectionChanged?.Invoke(_manager, EventArgs.Empty);
        }
    }
}
