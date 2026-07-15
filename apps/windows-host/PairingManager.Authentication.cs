using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

public sealed partial class PairingManager
{
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
        bool connectionChanged;
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
                if (pairToken is null)
                {
                    return new PairingResult(false, null, secret is null ? "missing-token" : "secret-revoked");
                }

                if (_currentToken is null)
                {
                    return new PairingResult(false, null, "stale-token");
                }

                if (_currentToken.ExpiresAt <= acceptedAt)
                {
                    return new PairingResult(false, null, "expired-token");
                }

                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_currentToken.Value), Encoding.UTF8.GetBytes(pairToken)))
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
            _activeConnections.Clear();
            _store.Clear();
        }

        PairingRevoked?.Invoke(this, new PairingRevokedEventArgs(null));
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

    private sealed record PairingToken(string Value, DateTimeOffset ExpiresAt);
}
