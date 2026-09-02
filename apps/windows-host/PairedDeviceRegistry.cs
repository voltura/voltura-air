using System.Security;

namespace VolturaAir.Host;

internal sealed class PairedDeviceRegistry
{
    private readonly PairingStore _store;
    private readonly List<PairingRecord> _records;
    private readonly Dictionary<string, int> _activeConnections = new(StringComparer.Ordinal);

    public PairedDeviceRegistry(PairingStore store, HostPermissionSet legacyGlobalPermissions)
    {
        _store = store;
        var loaded = store.Load();
        _records = [.. loaded.Select(record => PairingRecordNormalization.Normalize(record, legacyGlobalPermissions))];
        var persistedProfileNormalization = loaded.Zip(
            _records,
            static (original, normalized) => original with
            {
                AccessProfile = normalized.AccessProfile,
                PermissionOverrides = normalized.PermissionOverrides,
                InitialAccessNoticePending = normalized.InitialAccessNoticePending
            }).ToList();
        if (!loaded.SequenceEqual(persistedProfileNormalization))
        {
            try
            {
                store.Save(persistedProfileNormalization);
            }
            catch (Exception exception) when (IsRecoverablePersistenceFailure(exception))
            {
                // The normalized snapshot remains authoritative for this run. The unchanged
                // legacy data on disk is retried on the next launch.
            }
        }
    }

    public bool IsPaired => _records.Count > 0;
    public bool HasActiveController => _activeConnections.Count > 0;
    public int PairedDeviceCount => _records.Count;
    public int ActiveControllerCount => _activeConnections.Values.Sum();
    public IReadOnlyList<string> ActiveDeviceNames => [.. _records
        .Where(record => _activeConnections.ContainsKey(record.ClientId))
        .Select(record => record.DeviceName)];
    public string PairedDeviceSummary => SummarizeDevices(_records.Select(record => record.DeviceName));
    public string ActiveDeviceSummary => SummarizeDevices(ActiveDeviceNames);
    public bool HasActivePendingInitialAccessNotice => _records.Any(record =>
        record.InitialAccessNoticePending == true && _activeConnections.ContainsKey(record.ClientId));

    public PairingRecord? Find(string clientId) =>
        _records.FirstOrDefault(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));

    public IReadOnlyList<PairedDeviceStatus> GetDevices() => [.. BuildDeviceStatuses()
        .OrderByDescending(device => device.LatestActivityAt)
        .ThenBy(device => device.DeviceName, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<PairedDeviceStatus> GetDuplicateCleanupCandidates() => GetDuplicateCleanupCandidatesCore();

    public DevicePermissionOverrides GetDevicePermissionOverrides(string clientId)
    {
        var record = Find(clientId);
        return record is null
            ? new DevicePermissionOverrides()
            : record.PermissionOverrides ?? new DevicePermissionOverrides();
    }

    public HostPermissionSet GetEffectivePermissions(string clientId, HostPermissionSet globalPermissions) =>
        Find(clientId) is { } record
            ? GetEffectivePermissions(record, globalPermissions)
            : DeviceAccessProfiles.AllBlocked with
            {
                HideProtectedFileSystemItems = globalPermissions.HideProtectedFileSystemItems
            };

    public DeviceAccessProfile GetAccessProfile(string clientId) =>
        Find(clientId)?.AccessProfile ?? DeviceAccessProfile.Invalid;

    public (bool AllowRemoteInput, DeviceAccessProfile AccessProfile) GetInputAccess(
        string clientId,
        HostPermissionSet globalPermissions)
    {
        var record = Find(clientId);
        return record is null
            ? (false, DeviceAccessProfile.Invalid)
            : (GetEffectivePermissions(record, globalPermissions).AllowRemoteInput,
                record.AccessProfile ?? DeviceAccessProfile.Custom);
    }

    public int GetDevicePointerSpeed(string clientId) => GetEffectivePointerSpeed(Find(clientId));

    public bool GetDeviceShowModeButtons(string clientId) => GetEffectiveShowModeButtons(Find(clientId));

    public bool GetDeviceControlDepth(string clientId) => GetEffectiveControlDepth(Find(clientId));

    public string? GetDeviceAccentColor(string clientId) => GetEffectiveAccentColor(Find(clientId));

    public bool GetDeviceAccentColorOverridden(string clientId) => Find(clientId)?.AccentColorOverride is not null;

    public CustomScreenViewport? GetCustomScreenViewport(string clientId) => Find(clientId)?.CustomScreenViewport;

    public void UpsertAndSave(PairingRecord record)
    {
        var next = Snapshot();
        PairingRecordNormalization.Upsert(next, record);
        PersistAndPublish(next);
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

        var records = Snapshot();
        records[index] = next;
        PersistAndPublish(records);
        return true;
    }

    public InitialDeviceConnectionNotice? AddConnection(
        string clientId,
        DateTimeOffset connectedAt,
        DeviceConnectionMethod connectionMethod)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return null;
        }

        var existing = _records[index];
        var pendingInitialNotice = existing.InitialAccessNoticePending == true;
        var next = Snapshot();
        next[index] = existing with
        {
            LastConnectedAt = connectedAt,
            LastConnectionMethod = connectionMethod == DeviceConnectionMethod.Unknown
                ? existing.LastConnectionMethod
                : connectionMethod
        };
        try
        {
            PersistAndPublish(next);
        }
        catch (Exception exception) when (
            pendingInitialNotice &&
            IsRecoverablePersistenceFailure(exception))
        {
            // Authentication succeeds. The pending marker remains durable so a
            // later authenticated connection can retry notification delivery.
        }

        _activeConnections[clientId] = _activeConnections.GetValueOrDefault(clientId) + 1;
        return pendingInitialNotice
            ? new InitialDeviceConnectionNotice(
                existing.ClientId,
                existing.DeviceName,
                existing.AccessProfile ?? DeviceAccessProfile.Custom)
            : null;
    }

    public InitialDeviceConnectionNotice? TryClaimInitialConnectionNotice(string clientId)
    {
        var index = FindIndex(clientId);
        if (index < 0 || _records[index].InitialAccessNoticePending != true)
        {
            return null;
        }

        var existing = _records[index];
        var next = Snapshot();
        next[index] = existing with { InitialAccessNoticePending = null };
        try
        {
            PersistAndPublish(next);
        }
        catch (Exception exception) when (IsRecoverablePersistenceFailure(exception))
        {
            return null;
        }

        return new InitialDeviceConnectionNotice(
            existing.ClientId,
            existing.DeviceName,
            existing.AccessProfile ?? DeviceAccessProfile.Custom);
    }

    public void RemoveConnection(string clientId, DateTimeOffset disconnectedAt)
    {
        if (_activeConnections.TryGetValue(clientId, out var count) && count > 1)
        {
            _activeConnections[clientId] = count - 1;
            return;
        }

        UpdateDisconnectionTimestamp(clientId, disconnectedAt);
        _activeConnections.Remove(clientId);
    }

    public void Clear()
    {
        _store.Clear();
        _records.Clear();
        _activeConnections.Clear();
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

        var next = Snapshot();
        next[index] = existing with { PointerSpeedOverride = normalized };
        PersistAndPublish(next);
        return true;
    }

    public bool SetShowModeButtonsOverride(string clientId, bool? showModeButtons)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        var existing = _records[index];
        if (existing.ShowModeButtonsOverride == showModeButtons)
        {
            return false;
        }

        var next = Snapshot();
        next[index] = existing with { ShowModeButtonsOverride = showModeButtons };
        PersistAndPublish(next);
        return true;
    }

    public bool SetControlDepthOverride(string clientId, bool? controlDepth)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        var existing = _records[index];
        if (existing.ControlDepthOverride == controlDepth)
        {
            return false;
        }

        var next = Snapshot();
        next[index] = existing with { ControlDepthOverride = controlDepth };
        PersistAndPublish(next);
        return true;
    }

    public bool SetAccentColorOverride(string clientId, string? accentColor)
    {
        if (accentColor is not null && !AccentColor.IsCanonical(accentColor))
        {
            return false;
        }

        return UpdateRecord(
            clientId,
            existing => string.Equals(existing.AccentColorOverride, accentColor, StringComparison.Ordinal)
                ? null
                : existing with { AccentColorOverride = accentColor });
    }

    public bool SetCustomScreenViewport(string clientId, CustomScreenViewport viewport)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        var existing = _records[index];
        if (existing.CustomScreenViewport == viewport)
        {
            return false;
        }

        var next = Snapshot();
        next[index] = existing with { CustomScreenViewport = viewport };
        PersistAndPublish(next);
        return true;
    }

    public bool SetPermissionOverrides(string clientId, DevicePermissionOverrides permissionOverrides) =>
        UpdateRecord(
            clientId,
            existing => DeviceAccessProfilePersistence.ApplyPermissionOverrides(
                existing,
                permissionOverrides,
                AppPermissionSettings.Load()));

    public bool SetAccessProfile(string clientId, DeviceAccessProfile profile) =>
        UpdateRecord(
            clientId,
            existing => DeviceAccessProfilePersistence.ApplyProfile(
                existing,
                profile,
                AppPermissionSettings.Load()));

    public bool SetPermission(string clientId, DevicePermissionKind kind, bool allowed) =>
        UpdateRecord(
            clientId,
            existing => DeviceAccessProfilePersistence.ApplyPermission(
                existing,
                kind,
                allowed,
                AppPermissionSettings.Load()));

    public bool SetProtectedFileFilterOverride(string clientId, bool? hideProtected) =>
        UpdateRecord(
            clientId,
            existing => DeviceAccessProfilePersistence.ApplyProtectedFileFilter(
                existing,
                hideProtected));

    public string[] CleanUpDuplicateDevices()
    {
        var candidates = GetDuplicateCleanupCandidatesCore();
        if (candidates.Length == 0)
        {
            return [];
        }

        var removedClientIds = candidates.Select(device => device.ClientId).ToArray();
        var next = Snapshot();
        next.RemoveAll(record => removedClientIds.Contains(record.ClientId, StringComparer.Ordinal));
        PersistAndPublish(next);
        foreach (var clientId in removedClientIds)
        {
            _activeConnections.Remove(clientId);
        }
        return removedClientIds;
    }

    public bool DisconnectDevice(string clientId)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return false;
        }

        var next = Snapshot();
        next.RemoveAt(index);
        PersistAndPublish(next);
        _activeConnections.Remove(clientId);
        return true;
    }

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
            record.HostIdentityFingerprint,
            record.AccessProfile ?? DeviceAccessProfile.Custom,
            record.PermissionOverrides ?? new DevicePermissionOverrides(),
            record.PointerSpeedOverride,
            GetEffectivePointerSpeed(record),
            record.ShowModeButtonsOverride,
            GetEffectiveShowModeButtons(record),
            record.ControlDepthOverride,
            GetEffectiveControlDepth(record),
            record.AccentColorOverride,
            GetEffectiveAccentColor(record),
            record.CustomScreenViewport,
            record.LastConnectionMethod);
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

    private void UpdateDisconnectionTimestamp(string clientId, DateTimeOffset disconnectedAt)
    {
        var index = FindIndex(clientId);
        if (index < 0)
        {
            return;
        }

        var next = Snapshot();
        next[index] = next[index] with { LastDisconnectedAt = disconnectedAt };
        PersistAndPublish(next);
    }

    private List<PairingRecord> Snapshot() => [.. _records];

    private bool UpdateRecord(string clientId, Func<PairingRecord, PairingRecord?> update)
    {
        var index = FindIndex(clientId);
        if (index < 0 || update(_records[index]) is not { } updated || updated == _records[index])
        {
            return false;
        }

        var next = Snapshot();
        next[index] = updated;
        PersistAndPublish(next);
        return true;
    }

    private void PersistAndPublish(List<PairingRecord> next)
    {
        if (next.Count > 0)
        {
            _store.Save(next);
        }
        else
        {
            _store.Clear();
        }

        _records.Clear();
        _records.AddRange(next);
    }

    private int FindIndex(string clientId) =>
        _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));

    private static HostPermissionSet GetEffectivePermissions(PairingRecord record, HostPermissionSet globalPermissions) =>
        HostPermissions.Resolve(
            record.AccessProfile ?? DeviceAccessProfile.Custom,
            record.PermissionOverrides,
            globalPermissions);

    private static bool IsRecoverablePersistenceFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException;

    private static int GetEffectivePointerSpeed(PairingRecord? record) =>
        record?.PointerSpeedOverride ?? AppPointerSettings.GetDefaultPointerSpeed();

    private static bool GetEffectiveShowModeButtons(PairingRecord? record) =>
        record?.ShowModeButtonsOverride ?? AppAppearanceSettings.ShowModeButtons();

    private static bool GetEffectiveControlDepth(PairingRecord? record) =>
        record?.ControlDepthOverride ?? AppAppearanceSettings.DeviceControlDepth();

    private static string? GetEffectiveAccentColor(PairingRecord? record) =>
        record?.AccentColorOverride ?? AppAppearanceSettings.DeviceAccentColor();

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
