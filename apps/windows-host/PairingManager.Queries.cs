namespace VolturaAir.Host;

public sealed partial class PairingManager
{
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
            return GetDuplicateCleanupCandidatesCore();
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

    public int GetDevicePointerSpeed(string clientId)
    {
        lock (_gate)
        {
            return GetEffectivePointerSpeed(FindRecord(clientId));
        }
    }

    public string ActiveDeviceSummary => SummarizeDevices(ActiveDeviceNames);
}
