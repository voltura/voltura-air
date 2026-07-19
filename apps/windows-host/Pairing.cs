using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

public sealed class PairingManager(PairingStore store)
{
    internal static readonly TimeSpan TokenLifetime = PairingTokenAuthority.TokenLifetime;
    internal static readonly TimeSpan TokenRotationOverlap = PairingTokenAuthority.RotationOverlap;

    private readonly Lock _gate = new();
    private readonly PairingTokenAuthority _tokens = new();
    private readonly PairedDeviceRegistry _devices = new(store);

    public event EventHandler? ConnectionChanged;
    public event EventHandler? PermissionsChanged;
    public event EventHandler? DeviceProfileChanged;
    public event EventHandler<PairingRevokedEventArgs>? PairingRevoked;
    internal event EventHandler? PairingCodeInvalidated;

    public bool IsPaired
    {
        get
        {
            lock (_gate)
            {
                return _devices.IsPaired;
            }
        }
    }

    public bool HasActiveController
    {
        get
        {
            lock (_gate)
            {
                return _devices.HasActiveController;
            }
        }
    }

    public int PairedDeviceCount
    {
        get
        {
            lock (_gate)
            {
                return _devices.PairedDeviceCount;
            }
        }
    }

    public int ActiveControllerCount
    {
        get
        {
            lock (_gate)
            {
                return _devices.ActiveControllerCount;
            }
        }
    }

    public IReadOnlyList<string> ActiveDeviceNames
    {
        get
        {
            lock (_gate)
            {
                return _devices.ActiveDeviceNames;
            }
        }
    }

    public string PairedDeviceSummary
    {
        get
        {
            lock (_gate)
            {
                return _devices.PairedDeviceSummary;
            }
        }
    }

    public string ActiveDeviceSummary
    {
        get
        {
            lock (_gate)
            {
                return _devices.ActiveDeviceSummary;
            }
        }
    }

    public string CreatePairingToken(DateTimeOffset? now = null) => CreatePairingCode(now).Value;

    internal PairingCode CreatePairingCode(DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            return _tokens.CreateCode(now ?? DateTimeOffset.UtcNow);
        }
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
        var normalizedDeviceName = PairedDeviceRegistry.NormalizeDeviceName(deviceName);
        var normalizedPlatform = PairedDeviceRegistry.NormalizeMetadata(platform);
        var normalizedBrowser = PairedDeviceRegistry.NormalizeMetadata(browser);
        var normalizedDisplayMode = PairedDeviceRegistry.NormalizeMetadata(displayMode);
        string? revokedClientId = null;
        var pairingCodeInvalidated = false;
        bool connectionChanged;
        PairingResult result;

        lock (_gate)
        {
            var existing = _devices.Find(clientId);
            if (pairToken is not null)
            {
                if (_tokens.Validate(pairToken, acceptedAt) is { } rejectionReason)
                {
                    return new PairingResult(false, null, rejectionReason);
                }

                var newSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var replacesExistingClient = existing is not null;
                _devices.UpsertAndSave(new PairingRecord(
                    clientId,
                    HashSecret(newSecret),
                    normalizedDeviceName,
                    acceptedAt,
                    Platform: normalizedPlatform,
                    Browser: normalizedBrowser,
                    DisplayMode: normalizedDisplayMode));
                _tokens.Invalidate();
                pairingCodeInvalidated = true;
                connectionChanged = true;

                if (replacesExistingClient)
                {
                    revokedClientId = clientId;
                }

                result = new PairingResult(true, newSecret, "paired-with-new-secret");
            }
            else if (existing is not null && secret is not null && existing.SecretHash == HashSecret(secret))
            {
                connectionChanged = _devices.UpdateDeviceDetails(
                    clientId,
                    normalizedDeviceName,
                    normalizedPlatform,
                    normalizedBrowser,
                    normalizedDisplayMode,
                    acceptedAt);
                result = new PairingResult(true, secret, "paired");
            }
            else
            {
                return new PairingResult(false, null, secret is null ? "missing-token" : "secret-revoked");
            }
        }

        if (revokedClientId is not null)
        {
            PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(revokedClientId));
        }

        if (pairingCodeInvalidated)
        {
            PairingCodeInvalidated?.Invoke(this, EventArgs.Empty);
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
            _devices.UpsertAndSave(new PairingRecord(
                clientId,
                HashSecret(newSecret),
                PairedDeviceRegistry.NormalizeDeviceName(deviceName),
                now));
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return newSecret;
    }

    public bool RenameDevice(string clientId, string deviceName, DateTimeOffset? now = null)
    {
        bool renamed;
        lock (_gate)
        {
            renamed = _devices.UpdateDeviceDetails(
                clientId,
                PairedDeviceRegistry.NormalizeDeviceName(deviceName),
                null,
                null,
                null,
                now ?? DateTimeOffset.UtcNow);
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
            _tokens.Invalidate();
            _devices.Clear();
        }

        PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(null));
        PairingCodeInvalidated?.Invoke(this, EventArgs.Empty);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public IDisposable TrackConnection(string clientId, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            _devices.AddConnection(clientId, now ?? DateTimeOffset.UtcNow);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return new ConnectionScope(this, clientId);
    }

    public IReadOnlyList<PairedDeviceStatus> GetDevices()
    {
        lock (_gate)
        {
            return _devices.GetDevices();
        }
    }

    public IReadOnlyList<PairedDeviceStatus> GetDuplicateCleanupCandidates()
    {
        lock (_gate)
        {
            return _devices.GetDuplicateCleanupCandidates();
        }
    }

    public DevicePermissionOverrides GetDevicePermissionOverrides(string clientId)
    {
        lock (_gate)
        {
            return _devices.GetDevicePermissionOverrides(clientId);
        }
    }

    public HostPermissionSet GetEffectivePermissions(string clientId, HostPermissionSet globalPermissions)
    {
        lock (_gate)
        {
            return _devices.GetEffectivePermissions(clientId, globalPermissions);
        }
    }

    public int GetDevicePointerSpeed(string clientId)
    {
        lock (_gate)
        {
            return _devices.GetDevicePointerSpeed(clientId);
        }
    }

    public bool SetDevicePointerSpeedOverride(string clientId, int? pointerSpeed)
    {
        bool changed;
        lock (_gate)
        {
            changed = _devices.SetPointerSpeedOverride(clientId, pointerSpeed);
        }

        if (changed)
        {
            DeviceProfileChanged?.Invoke(this, EventArgs.Empty);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public bool SetDevicePermissionOverrides(string clientId, DevicePermissionOverrides permissionOverrides)
    {
        bool changed;
        lock (_gate)
        {
            changed = _devices.SetPermissionOverrides(clientId, permissionOverrides);
        }

        if (changed)
        {
            PermissionsChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public int CleanUpDuplicateDevices()
    {
        string[] removedClientIds;
        lock (_gate)
        {
            removedClientIds = _devices.CleanUpDuplicateDevices();
        }

        foreach (var clientId in removedClientIds)
        {
            PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(clientId));
        }

        if (removedClientIds.Length > 0)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return removedClientIds.Length;
    }

    public bool DisconnectDevice(string clientId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _devices.DisconnectDevice(clientId);
        }

        if (removed)
        {
            PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(clientId));
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private void ReleaseConnection(string clientId)
    {
        lock (_gate)
        {
            _devices.RemoveConnection(clientId, DateTimeOffset.UtcNow);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ConnectionScope(PairingManager manager, string clientId) : IDisposable
    {
        private PairingManager? _manager = manager;

        public void Dispose()
        {
            Interlocked.Exchange(ref _manager, null)?.ReleaseConnection(clientId);
        }
    }
}
