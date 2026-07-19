namespace VolturaAir.Host;

internal sealed class PairedDeviceRegistry(PairingStore store)
{
    private readonly PairingStore _store = store;
    private readonly List<PairingRecord> _records = [.. store.Load().Select(NormalizeRecord)];
    private readonly Dictionary<string, int> _activeConnections = new(StringComparer.Ordinal);

    public bool IsPaired => _records.Count > 0;
    public bool HasActiveController => _activeConnections.Count > 0;
    public int PairedDeviceCount => _records.Count;
    public int ActiveControllerCount => _activeConnections.Values.Sum();
    public IReadOnlyList<string> ActiveDeviceNames => [.. _records
        .Where(record => _activeConnections.ContainsKey(record.ClientId))
        .Select(record => record.DeviceName)];
    public string PairedDeviceSummary => SummarizeDevices(_records.Select(record => record.DeviceName));
    public string ActiveDeviceSummary => SummarizeDevices(ActiveDeviceNames);

    public PairingRecord? Find(string clientId) =>
        _records.FirstOrDefault(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));

    public IReadOnlyList<PairedDeviceStatus> GetDevices() => [.. BuildDeviceStatuses()
        .OrderByDescending(device => device.LatestActivityAt)
        .ThenBy(device => device.DeviceName, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<PairedDeviceStatus> GetDuplicateCleanupCandidates() => GetDuplicateCleanupCandidatesCore();

    public DevicePermissionOverrides GetDevicePermissionOverrides(string clientId) =>
        Find(clientId)?.PermissionOverrides ?? new DevicePermissionOverrides();

    public HostPermissionSet GetEffectivePermissions(string clientId, HostPermissionSet globalPermissions) =>
        HostPermissions.Resolve(globalPermissions, Find(clientId)?.PermissionOverrides);

    public int GetDevicePointerSpeed(string clientId) => GetEffectivePointerSpeed(Find(clientId));

    public void UpsertAndSave(PairingRecord record)
    {
        Upsert(record);
        _store.Save(_records);
    }

    public bool UpdateDeviceDetails(
        string clientId,
        string deviceName,
        string? platform,
        string? browser,
        string? displayMode,
        DateTimeOffset updatedAt)
    {
        var index = FindIndex(clientId);
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

    public void AddConnection(string clientId, DateTimeOffset connectedAt)
    {
        _activeConnections[clientId] = _activeConnections.GetValueOrDefault(clientId) + 1;
        UpdateConnectionTimestamp(clientId, connectedAt);
    }

    public void RemoveConnection(string clientId, DateTimeOffset disconnectedAt)
    {
        if (_activeConnections.TryGetValue(clientId, out var count) && count > 1)
        {
            _activeConnections[clientId] = count - 1;
            return;
        }

        _activeConnections.Remove(clientId);
        UpdateDisconnectionTimestamp(clientId, disconnectedAt);
    }

    public void Clear()
    {
        _records.Clear();
        _activeConnections.Clear();
        _store.Clear();
    }

    public bool SetPointerSpeedOverride(string clientId, int? pointerSpeed)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        int? normalized = pointerSpeed is null ? null : DevicePointerProfile.NormalizePointerSpeed(pointerSpeed.Value);
        var existing = _records[index];
        if (existing.PointerSpeedOverride == normalized)
        {
            return false;
        }

        _records[index] = existing with { PointerSpeedOverride = normalized };
        _store.Save(_records);
        return true;
    }

    public bool SetPermissionOverrides(string clientId, DevicePermissionOverrides permissionOverrides)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        var normalized = NormalizePermissionOverrides(permissionOverrides);
        var existing = _records[index];
        if (existing.PermissionOverrides == normalized)
        {
            return false;
        }

        _records[index] = existing with { PermissionOverrides = normalized };
        _store.Save(_records);
        return true;
    }

    public string[] CleanUpDuplicateDevices()
    {
        var candidates = GetDuplicateCleanupCandidatesCore();
        if (candidates.Length == 0)
        {
            return [];
        }

        var removedClientIds = candidates.Select(device => device.ClientId).ToArray();
        _records.RemoveAll(record => removedClientIds.Contains(record.ClientId, StringComparer.Ordinal));
        foreach (var clientId in removedClientIds)
        {
            _activeConnections.Remove(clientId);
        }

        SaveOrClear();
        return removedClientIds;
    }

    public bool DisconnectDevice(string clientId)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        _records.RemoveAt(index);
        _activeConnections.Remove(clientId);
        SaveOrClear();
        return true;
    }

    public static string NormalizeDeviceName(string deviceName)
    {
        var trimmed = deviceName.Trim();
        return trimmed.Length > 0 ? trimmed : "Mobile device";
    }

    public static string NormalizeMetadata(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }

    private static PairingRecord NormalizeRecord(PairingRecord record) =>
        (record.AddedAt == default ? record with { AddedAt = DateTimeOffset.UtcNow } : record) with
        {
            Platform = NormalizeMetadata(record.Platform),
            Browser = NormalizeMetadata(record.Browser),
            DisplayMode = NormalizeMetadata(record.DisplayMode),
            PermissionOverrides = NormalizePermissionOverrides(record.PermissionOverrides),
            PointerSpeedOverride = NormalizePointerSpeedOverride(record.PointerSpeedOverride)
        };

    private PairedDeviceStatus[] BuildDeviceStatuses() => [.. _records.Select(record =>
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
    })];

    private PairedDeviceStatus[] GetDuplicateCleanupCandidatesCore() => [.. BuildDeviceStatuses()
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
        .OrderByDescending(device => device.LatestActivityAt)];

    private void Upsert(PairingRecord record)
    {
        var index = FindIndex(record.ClientId);
        if (index < 0)
        {
            _records.Add(record);
            return;
        }

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
    }

    private void UpdateConnectionTimestamp(string clientId, DateTimeOffset connectedAt)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return;
        }

        _records[index] = _records[index] with { LastConnectedAt = connectedAt };
        _store.Save(_records);
    }

    private void UpdateDisconnectionTimestamp(string clientId, DateTimeOffset disconnectedAt)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return;
        }

        _records[index] = _records[index] with { LastDisconnectedAt = disconnectedAt };
        _store.Save(_records);
    }

    private void SaveOrClear()
    {
        if (_records.Count > 0)
        {
            _store.Save(_records);
        }
        else
        {
            _store.Clear();
        }
    }

    private int FindIndex(string clientId) =>
        _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));

    private static DevicePermissionOverrides NormalizePermissionOverrides(DevicePermissionOverrides? permissionOverrides) => new(
        AllowRemoteInput: permissionOverrides?.AllowRemoteInput,
        AllowPcSleep: permissionOverrides?.AllowPcSleep,
        AllowVolumeControl: permissionOverrides?.AllowVolumeControl,
        AllowPresentationControl: permissionOverrides?.AllowPresentationControl,
        AllowRemoteAppLaunch: permissionOverrides?.AllowRemoteAppLaunch,
        AllowUrlOpen: permissionOverrides?.AllowUrlOpen,
        AllowPcLock: permissionOverrides?.AllowPcLock,
        AllowBlackoutDisplay: permissionOverrides?.AllowBlackoutDisplay,
        AllowDisplayOff: permissionOverrides?.AllowDisplayOff,
        AllowScreenSaver: permissionOverrides?.AllowScreenSaver,
        AllowAwakeControl: permissionOverrides?.AllowAwakeControl,
        AllowClipboardRead: permissionOverrides?.AllowClipboardRead,
        AllowSignOut: permissionOverrides?.AllowSignOut,
        AllowRestart: permissionOverrides?.AllowRestart,
        AllowShutdown: permissionOverrides?.AllowShutdown);

    private static int? NormalizePointerSpeedOverride(int? pointerSpeedOverride) => pointerSpeedOverride is not null
        ? DevicePointerProfile.NormalizePointerSpeed(pointerSpeedOverride.Value)
        : null;

    private static int GetEffectivePointerSpeed(PairingRecord? record) =>
        record?.PointerSpeedOverride ?? AppPointerSettings.GetDefaultPointerSpeed();

    private static string SummarizeDevices(IEnumerable<string> deviceNames)
    {
        var names = deviceNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return names.Length switch
        {
            0 => "no devices",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} and {names.Length - 1} more"
        };
    }
}
