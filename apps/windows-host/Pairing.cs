using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host;

public sealed record PairingRecord(
    string ClientId,
    string SecretHash,
    string DeviceName,
    DateTimeOffset AddedAt = default,
    DateTimeOffset? LastConnectedAt = null,
    DateTimeOffset? LastDisconnectedAt = null,
    DateTimeOffset? LastRenamedAt = null,
    string Platform = "",
    string Browser = "",
    string DisplayMode = "",
    DevicePermissionOverrides? PermissionOverrides = null);

public sealed record PairedDeviceStatus(
    string ClientId,
    string DeviceName,
    bool IsActive,
    int ActiveConnections,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastRenamedAt,
    string Platform,
    string Browser,
    string DisplayMode,
    DevicePermissionOverrides PermissionOverrides)
{
    public DateTimeOffset LatestActivityAt => new[] { AddedAt, LastConnectedAt, LastDisconnectedAt, LastRenamedAt }
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .DefaultIfEmpty(DateTimeOffset.MinValue)
        .Max();
}
public sealed class PairingRevokedEventArgs : EventArgs
{
    public PairingRevokedEventArgs(string? clientId)
    {
        ClientId = clientId;
    }

    public string? ClientId { get; }
}

public sealed class PairingStore
{
    private readonly string _filePath;

    public PairingStore(string? rootFolder = null)
    {
        var folder = Path.Combine(rootFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltura Air");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "pairing.json");
    }

    public IReadOnlyList<PairingRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<PairingRecord>();
        }

        try
        {
            var data = JsonSerializer.Deserialize<PairingData>(File.ReadAllText(_filePath), JsonOptions.Default);
            return data?.Devices ?? Array.Empty<PairingRecord>();
        }
        catch
        {
            return Array.Empty<PairingRecord>();
        }
    }

    public void Save(IReadOnlyCollection<PairingRecord> records)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(new PairingData(records.ToArray()), JsonOptions.Default));
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private sealed record PairingData(IReadOnlyList<PairingRecord> Devices);
}

public sealed class PairingManager
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    private readonly PairingStore _store;
    private readonly object _gate = new();
    private PairingToken? _currentToken;
    private readonly List<PairingRecord> _records;
    private readonly Dictionary<string, int> _activeConnections = new(StringComparer.Ordinal);

    public PairingManager(PairingStore store)
    {
        _store = store;
        _records = store.Load().Select(NormalizeRecord).ToList();
    }

    public event EventHandler? ConnectionChanged;
    public event EventHandler? PermissionsChanged;
    public event EventHandler<PairingRevokedEventArgs>? PairingRevoked;

    public bool IsPaired
    {
        get
        {
            lock (_gate)
            {
                return _records.Count > 0;
            }
        }
    }

    public bool HasActiveController
    {
        get
        {
            lock (_gate)
            {
                return _activeConnections.Count > 0;
            }
        }
    }

    public int PairedDeviceCount
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    public int ActiveControllerCount
    {
        get
        {
            lock (_gate)
            {
                return _activeConnections.Values.Sum();
            }
        }
    }

    public IReadOnlyList<string> ActiveDeviceNames
    {
        get
        {
            lock (_gate)
            {
                return _records
                    .Where(record => _activeConnections.ContainsKey(record.ClientId))
                    .Select(record => record.DeviceName)
                    .ToArray();
            }
        }
    }

    public string PairedDeviceSummary
    {
        get
        {
            lock (_gate)
            {
                return SummarizeDevices(_records.Select(record => record.DeviceName));
            }
        }
    }

    public string ActiveDeviceSummary => SummarizeDevices(ActiveDeviceNames);

    public IReadOnlyList<PairedDeviceStatus> GetDevices()
    {
        lock (_gate)
        {
            return BuildDeviceStatuses()
                .OrderByDescending(device => device.LatestActivityAt)
                .ThenBy(device => device.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<PairedDeviceStatus> GetDuplicateCleanupCandidates()
    {
        lock (_gate)
        {
            return GetDuplicateCleanupCandidatesCore().ToArray();
        }
    }

    public DevicePermissionOverrides GetDevicePermissionOverrides(string clientId)
    {
        lock (_gate)
        {
            return FindRecord(clientId)?.PermissionOverrides ?? new DevicePermissionOverrides();
        }
    }

    public HostPermissionSet GetEffectivePermissions(string clientId, HostPermissionSet globalPermissions)
    {
        lock (_gate)
        {
            return HostPermissions.Resolve(globalPermissions, FindRecord(clientId)?.PermissionOverrides);
        }
    }

    public bool SetDevicePermissionOverrides(string clientId, DevicePermissionOverrides permissionOverrides)
    {
        lock (_gate)
        {
            var index = _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
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
        }

        PermissionsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public int CleanUpDuplicateDevices()
    {
        string[] removedClientIds;
        lock (_gate)
        {
            var candidates = GetDuplicateCleanupCandidatesCore().ToArray();
            if (candidates.Length == 0)
            {
                return 0;
            }

            removedClientIds = candidates.Select(device => device.ClientId).ToArray();
            _records.RemoveAll(record => removedClientIds.Contains(record.ClientId, StringComparer.Ordinal));
            foreach (var clientId in removedClientIds)
            {
                _activeConnections.Remove(clientId);
            }

            if (_records.Count > 0)
            {
                _store.Save(_records);
            }
            else
            {
                _store.Clear();
            }
        }

        foreach (var clientId in removedClientIds)
        {
            PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(clientId));
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return removedClientIds.Length;
    }

    public string CreatePairingToken(DateTimeOffset? now = null)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        lock (_gate)
        {
            _currentToken = new PairingToken(token, (now ?? DateTimeOffset.UtcNow).Add(TokenLifetime));
        }

        return token;
    }

    public PairingResult Accept(
        string clientId,
        string deviceName,
        string? pairToken,
        string? secret,
        DateTimeOffset? now = null,
        string? platform = null,
        string? browser = null,
        string? displayMode = null)
    {
        var acceptedAt = now ?? DateTimeOffset.UtcNow;
        var normalizedDeviceName = NormalizeDeviceName(deviceName);
        var normalizedPlatform = NormalizeMetadata(platform);
        var normalizedBrowser = NormalizeMetadata(browser);
        var normalizedDisplayMode = NormalizeMetadata(displayMode);
        string? revokedClientId = null;
        var connectionChanged = false;
        PairingResult result;

        lock (_gate)
        {
            var existing = FindRecord(clientId);
            if (existing is not null && secret is not null && existing.SecretHash == HashSecret(secret))
            {
                connectionChanged = UpdateDeviceDetails(clientId, normalizedDeviceName, normalizedPlatform, normalizedBrowser, normalizedDisplayMode, acceptedAt);
                result = new PairingResult(true, secret, "paired");
            }
            else
            {
                if (_currentToken is null || pairToken is null)
                {
                    return new PairingResult(false, null, "missing-token");
                }

                if (_currentToken.ExpiresAt <= acceptedAt || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_currentToken.Value), Encoding.UTF8.GetBytes(pairToken)))
                {
                    return new PairingResult(false, null, "invalid-token");
                }

                var newSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var replacesExistingClient = existing is not null;
                UpsertRecord(new PairingRecord(clientId, HashSecret(newSecret), normalizedDeviceName, acceptedAt, Platform: normalizedPlatform, Browser: normalizedBrowser, DisplayMode: normalizedDisplayMode));
                _store.Save(_records);
                _currentToken = null;
                connectionChanged = true;

                if (replacesExistingClient)
                {
                    revokedClientId = clientId;
                }

                result = new PairingResult(true, newSecret, "paired-with-new-secret");
            }
        }

        if (revokedClientId is not null)
        {
            PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(revokedClientId));
        }

        if (connectionChanged)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public string RotateSecret(string clientId, string deviceName)
    {
        var now = DateTimeOffset.UtcNow;
        var newSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        lock (_gate)
        {
            UpsertRecord(new PairingRecord(clientId, HashSecret(newSecret), NormalizeDeviceName(deviceName), now));
            _store.Save(_records);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return newSecret;
    }

    public bool RenameDevice(string clientId, string deviceName, DateTimeOffset? now = null)
    {
        var renamed = false;
        lock (_gate)
        {
            renamed = UpdateDeviceDetails(clientId, NormalizeDeviceName(deviceName), null, null, null, now ?? DateTimeOffset.UtcNow);
        }

        if (renamed)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return renamed;
    }

    public void ClearPairing()
    {
        lock (_gate)
        {
            _records.Clear();
            _currentToken = null;
            _activeConnections.Clear();
            _store.Clear();
        }

        PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(null));
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool DisconnectDevice(string clientId)
    {
        var removed = false;
        lock (_gate)
        {
            var index = _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            _records.RemoveAt(index);
            _activeConnections.Remove(clientId);
            if (_records.Count > 0)
            {
                _store.Save(_records);
            }
            else
            {
                _store.Clear();
            }

            removed = true;
        }

        if (removed)
        {
            PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(clientId));
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public IDisposable TrackConnection(string clientId, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            _activeConnections[clientId] = _activeConnections.GetValueOrDefault(clientId) + 1;
            UpdateConnectionTimestamp(clientId, now ?? DateTimeOffset.UtcNow);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return new ConnectionScope(this, clientId);
    }

    public static string HashSecret(string secret)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private sealed record PairingToken(string Value, DateTimeOffset ExpiresAt);

    private static PairingRecord NormalizeRecord(PairingRecord record)
    {
        return (record.AddedAt == default ? record with { AddedAt = DateTimeOffset.UtcNow } : record) with
        {
            Platform = NormalizeMetadata(record.Platform),
            Browser = NormalizeMetadata(record.Browser),
            DisplayMode = NormalizeMetadata(record.DisplayMode),
            PermissionOverrides = NormalizePermissionOverrides(record.PermissionOverrides)
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
                    record.PermissionOverrides ?? new DevicePermissionOverrides());
            })
            .ToArray();
    }

    private static DevicePermissionOverrides NormalizePermissionOverrides(DevicePermissionOverrides? permissionOverrides)
    {
        return new DevicePermissionOverrides(
            permissionOverrides?.AllowPcSleep,
            permissionOverrides?.AllowVolumeControl);
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
                PermissionOverrides = existing.PermissionOverrides
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

public sealed record PairingResult(bool Accepted, string? Secret, string Reason);
