namespace VolturaAir.Host;

public sealed partial class PairingManager
{
    public bool SetDevicePointerSpeedOverride(string clientId, int? pointerSpeed)
    {
        lock (_gate)
        {
            var index = _records.FindIndex(record => string.Equals(record.ClientId, clientId, StringComparison.Ordinal));
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
        }

        DeviceProfileChanged?.Invoke(this, EventArgs.Empty);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
}
