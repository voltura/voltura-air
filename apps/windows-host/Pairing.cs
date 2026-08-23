using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

public sealed class PairingManager
{
    private readonly ScreenViewHostIdentity _hostIdentity;
    private readonly bool _ownsHostIdentity;
    internal static readonly TimeSpan TokenLifetime = PairingTokenAuthority.TokenLifetime;
    internal static readonly TimeSpan TokenRotationOverlap = PairingTokenAuthority.RotationOverlap;

    private readonly Lock _gate = new();
    private readonly PairingTokenAuthority _tokens = new();
    private readonly PairedDeviceRegistry _devices;
    private readonly Dictionary<string, long> _pairingEpochs = new(StringComparer.Ordinal);

    public PairingManager(PairingStore store)
        : this(store, ScreenViewHostIdentity.OpenCurrentUser(), ownsHostIdentity: true)
    {
    }

    internal PairingManager(PairingStore store, ScreenViewHostIdentity hostIdentity, bool ownsHostIdentity = false)
    {
        _devices = new PairedDeviceRegistry(store);
        _hostIdentity = hostIdentity;
        _ownsHostIdentity = ownsHostIdentity;
    }

    public event EventHandler? ConnectionChanged;
    public event EventHandler? PermissionsChanged;
    public event EventHandler? DeviceProfileChanged;
    public event EventHandler<PairingRevokedEventArgs>? PairingRevoked;
    internal event EventHandler? PairingCodeInvalidated;
    internal ScreenViewHostIdentity HostIdentity => _hostIdentity;

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

    internal PairingBootstrapStartResult BeginPairingBootstrap(
        string clientId,
        string deviceName,
        string pairTokenId,
        string clientNonce,
        string reconnectPublicKey,
        DateTimeOffset? now = null,
        string? platform = null,
        string? browser = null,
        string? displayMode = null)
    {
        var acceptedAt = now ?? DateTimeOffset.UtcNow;
        lock (_gate)
        {
            var rejectionReason = _tokens.ResolveById(pairTokenId, acceptedAt, out var token);
            if (rejectionReason is not null || token is null)
            {
                return new PairingBootstrapStartResult(false, rejectionReason ?? "invalid-token");
            }

            if (!IsValidReconnectPublicKey(reconnectPublicKey) || !IsBase64Url(clientNonce) || clientNonce.Length != 43)
            {
                return new PairingBootstrapStartResult(false, "invalid-message");
            }

            var serverNonce = PairingBootstrapCrypto.CreateNonce();
            var hostProof = PairingBootstrapCrypto.CreateHostProof(
                token,
                clientId,
                clientNonce,
                serverNonce,
                reconnectPublicKey,
                _hostIdentity.PublicKey,
                _hostIdentity.Fingerprint);
            var clientProof = PairingBootstrapCrypto.CreateClientProof(
                token,
                clientId,
                clientNonce,
                serverNonce,
                reconnectPublicKey,
                _hostIdentity.PublicKey,
                _hostIdentity.Fingerprint);
            return new PairingBootstrapStartResult(
                true,
                "challenge",
                new PairingBootstrapPending(
                    clientId,
                    PairedDeviceRegistry.NormalizeDeviceName(deviceName),
                    token,
                    clientNonce,
                    serverNonce,
                    reconnectPublicKey,
                    _hostIdentity.PublicKey,
                    _hostIdentity.Fingerprint,
                    hostProof,
                    clientProof,
                    PairedDeviceRegistry.NormalizeMetadata(platform),
                    PairedDeviceRegistry.NormalizeMetadata(browser),
                    PairedDeviceRegistry.NormalizeMetadata(displayMode)));
        }
    }

    internal PairingResult CompletePairingBootstrap(PairingBootstrapPending pending, string clientProof, DateTimeOffset? now = null)
    {
        var verification = VerifyPairingBootstrap(pending, clientProof);
        if (!verification.Accepted)
        {
            return verification;
        }

        return CommitPairingBootstrap(pending, now);
    }

    internal static PairingResult VerifyPairingBootstrap(PairingBootstrapPending pending, string clientProof) =>
        PairingBootstrapCrypto.ProofsMatch(pending.ExpectedClientProof, clientProof)
            ? new PairingResult(true, string.Empty)
            : new PairingResult(false, "invalid-proof");

    internal PairingResult CommitPairingBootstrap(PairingBootstrapPending pending, DateTimeOffset? now = null)
    {
        return AcceptPairing(
            pending.ClientId,
            pending.DeviceName,
            pending.Token,
            now,
            pending.ReconnectPublicKey,
            pending.Platform,
            pending.Browser,
            pending.DisplayMode);
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
                DisplayMode: normalizedDisplayMode,
                HostIdentityFingerprint: _hostIdentity.Fingerprint));
            _tokens.Invalidate();
            pairingCodeInvalidated = true;
            connectionChanged = true;

            if (replacesExistingClient)
            {
                InvalidatePairingLocked(clientId);
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
            foreach (var clientId in _devices.GetDevices().Select(device => device.ClientId))
            {
                InvalidatePairingLocked(clientId);
            }
            _devices.Clear();
            _tokens.Invalidate();
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

    internal bool TryTrackConnection(
        string clientId,
        Action registerTransport,
        out IDisposable? connection,
        out long pairingEpoch,
        DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            if (_devices.Find(clientId) is null)
            {
                connection = null;
                pairingEpoch = 0;
                return false;
            }

            pairingEpoch = GetPairingEpochLocked(clientId);
            registerTransport();
            _devices.AddConnection(clientId, now ?? DateTimeOffset.UtcNow);
            connection = new ConnectionScope(this, clientId);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal bool IsCurrentPairing(string clientId, long pairingEpoch)
    {
        lock (_gate)
        {
            return _devices.Find(clientId) is not null && GetPairingEpochLocked(clientId) == pairingEpoch;
        }
    }

    public IReadOnlyList<PairedDeviceStatus> GetDevices()
    {
        lock (_gate)
        {
            return _devices.GetDevices();
        }
    }

    public string? GetDeviceName(string clientId)
    {
        lock (_gate)
        {
            return _devices.Find(clientId)?.DeviceName;
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

    internal bool HasCurrentHostIdentity(string clientId)
    {
        lock (_gate)
        {
            return string.Equals(
                _devices.Find(clientId)?.HostIdentityFingerprint,
                _hostIdentity.Fingerprint,
                StringComparison.Ordinal);
        }
    }

    internal bool VerifyClientSignature(string clientId, ReadOnlySpan<byte> payload, string signature)
    {
        lock (_gate)
        {
            var record = _devices.Find(clientId);
            if (record is null || string.IsNullOrWhiteSpace(signature) || signature.Length > 512 || !IsBase64Url(signature))
            {
                return false;
            }

            try
            {
                using var ecdsa = CreateReconnectPublicKey(DecodeBase64Url(record.ReconnectPublicKey));
                return ecdsa.VerifyData(
                    payload,
                    DecodeBase64Url(signature),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return false;
            }
        }
    }

    internal static bool VerifyPublicKeySignature(string publicKey, ReadOnlySpan<byte> payload, string signature)
    {
        if (!IsValidReconnectPublicKey(publicKey) || string.IsNullOrWhiteSpace(signature) || signature.Length > 512 || !IsBase64Url(signature))
        {
            return false;
        }

        try
        {
            using var ecdsa = CreateReconnectPublicKey(DecodeBase64Url(publicKey));
            return ecdsa.VerifyData(
                payload,
                DecodeBase64Url(signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    public int GetDevicePointerSpeed(string clientId) { lock (_gate) return _devices.GetDevicePointerSpeed(clientId); }

    public bool GetDeviceShowModeButtons(string clientId) { lock (_gate) return _devices.GetDeviceShowModeButtons(clientId); }

    public bool GetDeviceControlDepth(string clientId) { lock (_gate) return _devices.GetDeviceControlDepth(clientId); }

    public CustomScreenViewport? GetCustomScreenViewport(string clientId)
    {
        lock (_gate) return _devices.GetCustomScreenViewport(clientId);
    }

    public bool SetDevicePointerSpeedOverride(string clientId, int? pointerSpeed) =>
        UpdateDeviceProfile(() => _devices.SetPointerSpeedOverride(clientId, pointerSpeed));

    public bool SetDeviceShowModeButtonsOverride(string clientId, bool? showModeButtons) =>
        UpdateDeviceProfile(() => _devices.SetShowModeButtonsOverride(clientId, showModeButtons));

    public bool SetDeviceControlDepthOverride(string clientId, bool? controlDepth) =>
        UpdateDeviceProfile(() => _devices.SetControlDepthOverride(clientId, controlDepth));

    public bool SetCustomScreenViewport(string clientId, CustomScreenViewport viewport) =>
        UpdateDeviceProfile(() => _devices.SetCustomScreenViewport(clientId, viewport));

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
            foreach (var clientId in removedClientIds)
            {
                InvalidatePairingLocked(clientId);
            }
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
            if (removed)
            {
                InvalidatePairingLocked(clientId);
            }
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

    private long GetPairingEpochLocked(string clientId) => _pairingEpochs.GetValueOrDefault(clientId);

    private void InvalidatePairingLocked(string clientId) =>
        _pairingEpochs[clientId] = GetPairingEpochLocked(clientId) + 1;

    private sealed class ConnectionScope(PairingManager manager, string clientId) : IDisposable
    {
        private PairingManager? _manager = manager;
        private int _disposeState;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            {
                return;
            }

            try
            {
                _manager?.ReleaseConnection(clientId);
                _manager = null;
                Volatile.Write(ref _disposeState, 2);
            }
            catch
            {
                Volatile.Write(ref _disposeState, 0);
                throw;
            }
        }
    }

    internal void DisposeHostIdentity()
    {
        if (_ownsHostIdentity)
        {
            _hostIdentity.Dispose();
        }
    }

}
