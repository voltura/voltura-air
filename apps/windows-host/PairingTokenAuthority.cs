using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host;

internal sealed class PairingTokenAuthority
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RotationOverlap = TimeSpan.FromSeconds(15);

    private PairingToken? _currentToken;
    private PairingToken? _previousToken;

    public PairingCode CreateCode(DateTimeOffset createdAt)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var expiresAt = createdAt.Add(TokenLifetime);
        _previousToken = CreateOverlappingPreviousToken(_currentToken, createdAt);
        _currentToken = new PairingToken(token, expiresAt);
        return new PairingCode(token, expiresAt, expiresAt.Subtract(RotationOverlap));
    }

    public string? Validate(string candidate, DateTimeOffset acceptedAt)
    {
        var matchingToken = FindMatchingToken(candidate);
        if (matchingToken is null)
        {
            return _currentToken is null && _previousToken is null ? "stale-token" : "invalid-token";
        }

        return matchingToken.ExpiresAt <= acceptedAt ? "expired-token" : null;
    }

    public void Invalidate()
    {
        _currentToken = null;
        _previousToken = null;
    }

    private PairingToken? FindMatchingToken(string candidate)
    {
        if (_currentToken is not null && TokensMatch(_currentToken.Value, candidate))
        {
            return _currentToken;
        }

        return _previousToken is not null && TokensMatch(_previousToken.Value, candidate)
            ? _previousToken
            : null;
    }

    private static PairingToken? CreateOverlappingPreviousToken(PairingToken? currentToken, DateTimeOffset rotatedAt)
    {
        if (currentToken is null || currentToken.ExpiresAt <= rotatedAt)
        {
            return null;
        }

        var overlapEndsAt = rotatedAt.Add(RotationOverlap);
        return currentToken with
        {
            ExpiresAt = currentToken.ExpiresAt < overlapEndsAt ? currentToken.ExpiresAt : overlapEndsAt
        };
    }

    private static bool TokensMatch(string expected, string candidate) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(candidate));

    private sealed record PairingToken(string Value, DateTimeOffset ExpiresAt);
}
