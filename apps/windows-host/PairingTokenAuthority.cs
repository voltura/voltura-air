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

    public string? ResolveById(string tokenId, DateTimeOffset acceptedAt, out string? token)
    {
        token = null;
        var matchingToken = FindMatchingTokenById(tokenId);
        if (matchingToken is null)
        {
            return _currentToken is null && _previousToken is null ? "stale-token" : "invalid-token";
        }

        if (matchingToken.ExpiresAt <= acceptedAt)
        {
            return "expired-token";
        }

        token = matchingToken.Value;
        return null;
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

    private PairingToken? FindMatchingTokenById(string candidate)
    {
        if (_currentToken is not null && TokenIdsMatch(_currentToken.Value, candidate))
        {
            return _currentToken;
        }

        return _previousToken is not null && TokenIdsMatch(_previousToken.Value, candidate)
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

    internal static string CreateTokenId(string token) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool TokenIdsMatch(string token, string candidate) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(CreateTokenId(token)),
            Encoding.ASCII.GetBytes(candidate));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private sealed record PairingToken(string Value, DateTimeOffset ExpiresAt);
}
