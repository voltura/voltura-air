using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

public sealed partial class PairingManager
{
    public string CreatePairingToken(DateTimeOffset? now = null)
    {
        return CreatePairingCode(now).Value;
    }

    internal PairingCode CreatePairingCode(DateTimeOffset? now = null)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var createdAt = now ?? DateTimeOffset.UtcNow;
        var expiresAt = createdAt.Add(TokenLifetime);
        lock (_gate)
        {
            _previousToken = CreateOverlappingPreviousToken(_currentToken, createdAt);
            _currentToken = new PairingToken(token, expiresAt);
        }

        return new PairingCode(token, expiresAt, expiresAt.Subtract(TokenRotationOverlap));
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
        var pairingCodeInvalidated = false;
        bool connectionChanged;
        PairingResult result;

        lock (_gate)
        {
            var existing = FindRecord(clientId);
            if (pairToken is not null)
            {
                var matchingToken = FindMatchingToken(pairToken);
                if (matchingToken is null && _currentToken is null && _previousToken is null)
                {
                    return new PairingResult(false, null, "stale-token");
                }

                if (matchingToken is null)
                {
                    return new PairingResult(false, null, "invalid-token");
                }

                if (matchingToken.ExpiresAt <= acceptedAt)
                {
                    return new PairingResult(false, null, "expired-token");
                }

                var newSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var replacesExistingClient = existing is not null;
                UpsertRecord(new PairingRecord(clientId, HashSecret(newSecret), normalizedDeviceName, acceptedAt, Platform: normalizedPlatform, Browser: normalizedBrowser, DisplayMode: normalizedDisplayMode));
                _store.Save(_records);
                _currentToken = null;
                _previousToken = null;
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
                connectionChanged = UpdateDeviceDetails(clientId, normalizedDeviceName, normalizedPlatform, normalizedBrowser, normalizedDisplayMode, acceptedAt);
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
            UpsertRecord(new PairingRecord(clientId, HashSecret(newSecret), NormalizeDeviceName(deviceName), now));
            _store.Save(_records);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return newSecret;
    }

    public bool RenameDevice(string clientId, string deviceName, DateTimeOffset? now = null)
    {
        bool renamed;
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
            _previousToken = null;
            _activeConnections.Clear();
            _store.Clear();
        }

        PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(null));
        PairingCodeInvalidated?.Invoke(this, EventArgs.Empty);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
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

    private PairingToken? FindMatchingToken(string pairToken)
    {
        if (_currentToken is not null && TokensMatch(_currentToken.Value, pairToken))
        {
            return _currentToken;
        }

        return _previousToken is not null && TokensMatch(_previousToken.Value, pairToken)
            ? _previousToken
            : null;
    }

    private static PairingToken? CreateOverlappingPreviousToken(PairingToken? currentToken, DateTimeOffset rotatedAt)
    {
        if (currentToken is null || currentToken.ExpiresAt <= rotatedAt)
        {
            return null;
        }

        var overlapEndsAt = rotatedAt.Add(TokenRotationOverlap);
        return currentToken with
        {
            ExpiresAt = currentToken.ExpiresAt < overlapEndsAt ? currentToken.ExpiresAt : overlapEndsAt
        };
    }

    private static bool TokensMatch(string expected, string candidate)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(candidate));
    }

    private sealed record PairingToken(string Value, DateTimeOffset ExpiresAt);
}
