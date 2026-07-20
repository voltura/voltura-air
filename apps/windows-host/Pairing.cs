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

    public PairingResult AcceptPairing(
        string clientId,
        string deviceName,
        string pairToken,
        DateTimeOffset? now = null,
        string? reconnectPublicKey = null,
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
        bool pairingCodeInvalidated;
        bool connectionChanged;
        PairingResult result;

        lock (_gate)
        {
            var existing = _devices.Find(clientId);
            if (_tokens.Validate(pairToken, acceptedAt) is { } rejectionReason)
            {
                return new PairingResult(false, rejectionReason);
            }

            var validatedReconnectPublicKey = reconnectPublicKey;
            if (validatedReconnectPublicKey is null || !IsValidReconnectPublicKey(validatedReconnectPublicKey))
            {
                return new PairingResult(false, "invalid-message");
            }

            var replacesExistingClient = existing is not null;
            _devices.UpsertAndSave(new PairingRecord(
                clientId,
                validatedReconnectPublicKey,
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

            result = new PairingResult(true, "paired");
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

    public string? CreateReconnectChallenge(string clientId)
    {
        lock (_gate)
        {
            return _devices.Find(clientId) is null ? null : CreateBase64UrlRandom(32);
        }
    }

    public PairingResult AcceptReconnectProof(
        string clientId,
        string challenge,
        string signature,
        string deviceName,
        string? platform = null,
        string? browser = null,
        string? displayMode = null,
        DateTimeOffset? now = null)
    {
        var acceptedAt = now ?? DateTimeOffset.UtcNow;
        var normalizedDeviceName = PairedDeviceRegistry.NormalizeDeviceName(deviceName);
        var normalizedPlatform = PairedDeviceRegistry.NormalizeMetadata(platform);
        var normalizedBrowser = PairedDeviceRegistry.NormalizeMetadata(browser);
        var normalizedDisplayMode = PairedDeviceRegistry.NormalizeMetadata(displayMode);
        bool connectionChanged;

        lock (_gate)
        {
            if (_devices.Find(clientId) is not { } existing)
            {
                return new PairingResult(false, "device-revoked");
            }

            if (!IsValidReconnectSignature(existing, clientId, challenge, signature))
            {
                return new PairingResult(false, "invalid-proof");
            }

            connectionChanged = _devices.UpdateDeviceDetails(
                clientId,
                normalizedDeviceName,
                normalizedPlatform,
                normalizedBrowser,
                normalizedDisplayMode,
                acceptedAt);
        }

        if (connectionChanged)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return new PairingResult(true, "paired");
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

    public int GetDevicePointerSpeed(string clientId) { lock (_gate) return _devices.GetDevicePointerSpeed(clientId); }

    public bool GetDeviceShowModeButtons(string clientId) { lock (_gate) return _devices.GetDeviceShowModeButtons(clientId); }

    public bool SetDevicePointerSpeedOverride(string clientId, int? pointerSpeed) =>
        UpdateDeviceProfile(() => _devices.SetPointerSpeedOverride(clientId, pointerSpeed));

    public bool SetDeviceShowModeButtonsOverride(string clientId, bool? showModeButtons) =>
        UpdateDeviceProfile(() => _devices.SetShowModeButtonsOverride(clientId, showModeButtons));

    private bool UpdateDeviceProfile(Func<bool> update)
    {
        bool changed;
        lock (_gate) changed = update();
        if (changed) DeviceProfileChanged?.Invoke(this, EventArgs.Empty);
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

    public static string GetReconnectSigningPayload(string clientId, string challenge) =>
        $"VolturaAir reconnect:v1:{clientId}:{challenge}";

    internal static bool IsValidReconnectPublicKey(string? reconnectPublicKey)
    {
        if (string.IsNullOrWhiteSpace(reconnectPublicKey) ||
            reconnectPublicKey.Length > 512 ||
            !IsBase64Url(reconnectPublicKey))
        {
            return false;
        }

        try
        {
            using var ecdsa = CreateReconnectPublicKey(DecodeBase64Url(reconnectPublicKey));
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static bool IsValidReconnectSignature(PairingRecord existing, string clientId, string challenge, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature) ||
            signature.Length > 512 ||
            !IsBase64Url(signature))
        {
            return false;
        }

        try
        {
            using var ecdsa = CreateReconnectPublicKey(DecodeBase64Url(existing.ReconnectPublicKey));
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(GetReconnectSigningPayload(clientId, challenge)),
                DecodeBase64Url(signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static ECDsa CreateReconnectPublicKey(byte[] publicKey)
    {
        if (publicKey.Length != 65 || publicKey[0] != 0x04)
        {
            throw new CryptographicException("Invalid reconnect public key.");
        }

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = publicKey[1..33],
                Y = publicKey[33..65]
            }
        };
        var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(parameters);
        return ecdsa;
    }

    private static string CreateBase64UrlRandom(int byteCount) =>
        EncodeBase64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static bool IsBase64Url(string value) =>
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

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
